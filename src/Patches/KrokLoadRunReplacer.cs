using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 替换 KrokMP 对 PreRunScript.LoadRun / StartRun 的行为。
    /// KrokMP 要求 Net.running 才允许继续；主菜单存档并退出后 Net.running=false 会被硬拦截。
    /// </summary>
    internal static class KrokLoadRunReplacer
    {
        private static MethodInfo _mDoStatusLog;
        private static PropertyInfo _pNetRunning;
        private static FieldInfo _fIsClient;

        internal static void TryPatch(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                var asm = MultiplayerBridge.GetKrokAssembly();
                if (asm == null) return;
                ResolveKrokHandles(asm);

                KrokMpHarmonySilencer.TrySilence(harmony);
                UnpatchKrokOwner(harmony, AccessTools.Method(typeof(PreRunScript), "LoadRun"));
                UnpatchKrokOwner(harmony, AccessTools.Method(typeof(PreRunScript), "StartRun"));

                PatchRunMethod(harmony, nameof(LoadRun_Prefix), AccessTools.Method(typeof(PreRunScript), "LoadRun"));
                PatchRunMethod(harmony, nameof(StartRun_Prefix), AccessTools.Method(typeof(PreRunScript), "StartRun"));

                ModLog.Info("KrokLoadRunReplacer：LoadRun/StartRun 已接管");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"KrokLoadRunReplacer.TryPatch 失败：{ex.Message}");
            }
        }

        private static void UnpatchKrokOwner(Harmony harmony, MethodBase method)
        {
            if (method == null) return;
            try
            {
                harmony.Unpatch(method, HarmonyPatchType.Prefix, "KrokoshaCasualtiesMP");
            }
            catch { /* 已移除时忽略 */ }
        }

        private static void PatchRunMethod(Harmony harmony, string handlerName, MethodBase target)
        {
            if (target == null) return;
            var info = Harmony.GetPatchInfo(target);
            if (info?.Prefixes != null)
            {
                foreach (var p in info.Prefixes)
                {
                    if (p.owner == harmony.Id && p.PatchMethod?.Name == handlerName)
                        return;
                }
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(KrokLoadRunReplacer).GetMethod(
                handlerName, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void ResolveKrokHandles(Assembly asm)
        {
            var tMp = asm.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
            if (tMp != null)
            {
                _mDoStatusLog = tMp.GetMethod("DoMultiplayerStatusMessageLog",
                    BindingFlags.Public | BindingFlags.Static);
                _fIsClient = tMp.GetField("is_client", BindingFlags.Public | BindingFlags.Static);
            }
            var tNet = asm.GetType("KrokoshaCasualtiesMP.Net");
            if (tNet != null)
                _pNetRunning = tNet.GetProperty("running", BindingFlags.Public | BindingFlags.Static);
        }

        private static bool LoadRun_Prefix()
        {
            if (!MultiplayerBridge.IsMultiplayerEnabled())
                return true;

            bool netRunning = _pNetRunning != null && (bool)_pNetRunning.GetValue(null);
            ModLog.Info($"LoadRun_Prefix：mp=true netRunning={netRunning} hasSave={HasAnySaveFile()}");

            if (netRunning)
                return HandleNetRunningContinue(loadsave: true);

            if (!HasAnySaveFile())
            {
                KrokLog("You can't play singleplayer with MP mod active.\nDeactivate it in Settings > General");
                return false;
            }

            ModLog.Info("LoadRun：Net.running=false，反射 LoadVanillaGeneratedWorld(loadsave:true)");
            RunSettingsBridge.RestoreFromSaveFile(MpSaveLocator.ResolveLocalPlayerSavePath());
            RestoreMpWorldSeed();
            MultiplayerBridge.TryLoadMultiplayerContinue(loadsave: true);
            return false;
        }

        /// <summary>新游戏：联机未开时放行原版 StartRun（Krok 会硬拦单机新局）。</summary>
        private static bool StartRun_Prefix()
        {
            if (!MultiplayerBridge.IsMultiplayerEnabled())
                return true;

            bool netRunning = _pNetRunning != null && (bool)_pNetRunning.GetValue(null);
            if (!netRunning)
                return true;

            if (_fIsClient != null && (bool)_fIsClient.GetValue(null))
            {
                KrokLog("You can't start the game, server can.");
                return false;
            }

            ModLog.Info("StartRun：Net.running=true，走 KrokMP LoadVanillaGeneratedWorld(loadsave:false)");
            MultiplayerBridge.TryLoadMultiplayerContinue(loadsave: false);
            return false;
        }

        private static bool HandleNetRunningContinue(bool loadsave)
        {
            if (_fIsClient != null && (bool)_fIsClient.GetValue(null))
            {
                KrokLog("You can't start the game, server can.");
                return false;
            }
            ModLog.Info($"LoadRun：Net.running=true，走 KrokMP LoadVanillaGeneratedWorld(loadsave:{loadsave})");
            if (loadsave)
            {
                RunSettingsBridge.RestoreFromSaveFile(MpSaveLocator.ResolveLocalPlayerSavePath());
                RestoreMpWorldSeed();
            }
            MultiplayerBridge.TryLoadMultiplayerContinue(loadsave);
            return false;
        }

        /// <summary>多人续玩（主机 loadsave）按本地玩家存档的 sidecar 还原世界引擎与固定世界种子，
        /// 使 self 引擎多人存档重启后世界可复现；随后 Server_AnnounceSeed 会把种子广播给客户端。</summary>
        private static void RestoreMpWorldSeed()
        {
            try
            {
                var sidecar = SlotSidecar.LoadOrEmpty(MpSaveLocator.ResolveLocalPlayerSavePath());
                WorldEngineArbiter.PrepareMpRollback(sidecar);
                ModLog.Info($"多人续玩：还原世界引擎/种子 engine='{sidecar.WorldEngine}' seed={sidecar.QolSeed}");
            }
            catch (Exception ex) { ModLog.Warning($"多人续玩还原世界种子失败：{ex.Message}"); }
        }

        private static bool HasAnySaveFile()
        {
            if (MpSaveLocator.HasMpSave()) return true;
            string p = Application.persistentDataPath;
            if (File.Exists(Path.Combine(p, "save.sv"))) return true;
            if (File.Exists(Path.Combine(p, "mp_save", "save.sv"))) return true;
            if (File.Exists(Path.Combine(p, "autosave.sv"))) return true;
            return false;
        }

        private static void KrokLog(string msg)
        {
            try { _mDoStatusLog?.Invoke(null, new object[] { msg }); }
            catch { ModLog.Info(msg); }
        }
    }
}
