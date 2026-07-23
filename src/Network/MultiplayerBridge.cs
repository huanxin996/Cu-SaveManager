using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人 mod (KrokoshaCasualtiesMP) 的反射桥接。所有调用都做软依赖：
    /// dll 不在 / 字段不存在时静默 fallback，绝不抛出阻塞主流程。
    /// 第一次访问时缓存反射句柄；类型/字段变更只影响 mp 联动，不影响单机功能。
    /// </summary>
    internal static class MultiplayerBridge
    {
        private static bool _resolved;
        private static MemberInfo _mIsRunning;        // KrokoshaScavMultiplayer.network_system_is_running (property)
        private static MemberInfo _mIsServer;         // KrokoshaScavMultiplayer.is_server (property)
        private static MemberInfo _mSuccessInit;      // Plugin.SUCCESFULLY_INITIALIZED (property)
        private static MemberInfo _mForceDisable;     // Plugin.FORCE_DISABLE_MP_MOD (property)
        private static FieldInfo _fAllLiving;         // NetPlayer.AllLivingPlayers (field)
        private static FieldInfo _fBodyToPlayer;      // NetPlayer.BodyToPlayerDict (field)
        private static MethodInfo _mAnnounceAlert;    // ServerMain.Server_AnnounceAlert(in string,bool,bool,IReadOnlyList<uint>)
        private static MethodInfo _mLoadVanillaGeneratedWorld; // WorldgenPatches.LoadVanillaGeneratedWorld(bool)
        private static MethodInfo _mGetPersistentId;           // NetPlayer.GetPersistentId()
        private static MethodInfo _mTeleportCharacter;           // NetPlayer.Server_TeleportCharacter(Vector2)
        private static Type _tNetPlayer;
        private static Assembly _krokAssembly;

        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static void TryResolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type tMP = null, tNetPlayer = null, tServerMain = null;
                Assembly krokoshaAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "KrokoshaCasualtiesMP") { krokoshaAsm = asm; break; }
                }
                if (krokoshaAsm == null)
                {
                    ModLog.Info("MultiplayerBridge: 未找到 KrokoshaCasualtiesMP 程序集");
                    return;
                }
                _krokAssembly = krokoshaAsm;
                tMP = krokoshaAsm.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
                tNetPlayer = krokoshaAsm.GetType("KrokoshaCasualtiesMP.NetPlayer");
                _tNetPlayer = tNetPlayer;
                tServerMain = krokoshaAsm.GetType("KrokoshaCasualtiesMP.ServerMain");
                Type tPlugin = krokoshaAsm.GetType("KrokoshaCasualtiesMP.Plugin");
                if (tPlugin != null)
                {
                    _mSuccessInit = ResolveStaticBoolMember(tPlugin, "SUCCESFULLY_INITIALIZED");
                    _mForceDisable = ResolveStaticBoolMember(tPlugin, "FORCE_DISABLE_MP_MOD");
                }
                if (tMP == null) return;
                _mIsRunning = ResolveStaticBoolMember(tMP, "network_system_is_running");
                _mIsServer = ResolveStaticBoolMember(tMP, "is_server");
                if (tNetPlayer != null)
                {
                    _fAllLiving = tNetPlayer.GetField("AllLivingPlayers", AnyStatic);
                    _fBodyToPlayer = tNetPlayer.GetField("BodyToPlayerDict", AnyStatic);
                    _mGetPersistentId = tNetPlayer.GetMethod("GetPersistentId", AnyInstance);
                    _mTeleportCharacter = tNetPlayer.GetMethod("Server_TeleportCharacter",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector2) }, null);
                }
                Type tWorldgenPatches = krokoshaAsm.GetType("KrokoshaCasualtiesMP.WorldgenPatches");
                if (tWorldgenPatches != null)
                {
                    _mLoadVanillaGeneratedWorld = tWorldgenPatches.GetMethod("LoadVanillaGeneratedWorld",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                }
                if (tServerMain != null)
                {
                    foreach (var m in tServerMain.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "Server_AnnounceAlert") continue;
                        var ps = m.GetParameters();
                        if (ps.Length >= 2 && ps[0].ParameterType.GetElementType() == typeof(string))
                        {
                            _mAnnounceAlert = m;
                            break;
                        }
                    }
                }
                ModLog.Info($"MultiplayerBridge 已解析（running{(_mIsRunning != null ? "✓" : "✗")} / server{(_mIsServer != null ? "✓" : "✗")} / 广播{(_mAnnounceAlert != null ? "✓" : "✗")} / 继续游戏{(_mLoadVanillaGeneratedWorld != null ? "✓" : "✗")}）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MultiplayerBridge 反射失败：{ex.Message}");
            }
        }

        /// <summary>解析静态布尔成员：KrokMP 这些成员是 property（=> Net.xxx），field 缺失时回落 property。</summary>
        private static MemberInfo ResolveStaticBoolMember(Type t, string name)
        {
            MemberInfo m = t.GetField(name, AnyStatic);
            if (m == null) m = t.GetProperty(name, AnyStatic);
            return m;
        }

        private static bool ReadStaticBool(MemberInfo m)
        {
            try
            {
                if (m is FieldInfo f) return (bool)f.GetValue(null);
                if (m is PropertyInfo p) return (bool)p.GetValue(null);
            }
            catch { }
            return false;
        }

        internal static bool IsMultiplayerRunning()
        {
            TryResolve();
            return _mIsRunning != null && ReadStaticBool(_mIsRunning);
        }

        /// <summary>多人 mod (KrokMP) 是否已加载（不要求正在联机）。</summary>
        internal static bool IsModPresent()
        {
            TryResolve();
            return _mIsRunning != null;
        }

        /// <summary>KrokMP 多人模式是否启用：mod 初始化成功且未被强制禁用。与是否开房联机无关。</summary>
        internal static bool IsMultiplayerEnabled()
        {
            TryResolve();
            if (_mSuccessInit == null) return false;
            if (!ReadStaticBool(_mSuccessInit)) return false;
            return _mForceDisable == null || !ReadStaticBool(_mForceDisable);
        }

        internal static bool IsServer()
        {
            TryResolve();
            return _mIsServer != null && ReadStaticBool(_mIsServer);
        }

        internal static bool IsDedicatedServer()
        {
            TryResolve();
            if (_krokAssembly == null) return false;

            try
            {
                Type tNet = _krokAssembly.GetType("KrokoshaCasualtiesMP.Net");
                MemberInfo mDedicated = tNet.GetProperty("is_dedicated_server", AnyStatic);

                if (mDedicated != null)
                    return ReadStaticBool(mDedicated);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"获取 dedicated 状态失败：{ex.Message}");
            }

            return false;
        }

        internal static Assembly GetKrokAssembly()
        {
            TryResolve();
            return _krokAssembly;
        }

        /// <summary>本地玩家 KrokMP persistentId（如 STEAM_7656…），用于定位 mp_save 子目录。</summary>
        internal static string GetLocalPersistentId()
        {
            TryResolve();
            try
            {
                var body = PlayerCamera.main?.body;
                if (body == null || _fBodyToPlayer == null || _mGetPersistentId == null) return null;
                var dict = _fBodyToPlayer.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return null;
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    if (kv.Key as Body != body || kv.Value == null) continue;
                    return _mGetPersistentId.Invoke(kv.Value, null) as string;
                }
            }
            catch { }
            return null;
        }

        /// <summary>主机按 persistentId 将各玩家传送到回档坐标（含子端同步）。</summary>
        internal static int RestorePlayerPositions(Dictionary<string, Vector2> positions)
        {
            TryResolve();
            if (positions == null || positions.Count == 0) return 0;
            if (!IsMultiplayerRunning() || !IsServer()) return 0;
            if (_fBodyToPlayer == null || _mGetPersistentId == null || _mTeleportCharacter == null) return 0;
            int n = 0;
            try
            {
                var dict = _fBodyToPlayer.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return 0;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    var plr = entry.Value;
                    if (plr == null) continue;
                    string id = _mGetPersistentId.Invoke(plr, null) as string;
                    if (string.IsNullOrEmpty(id) || !positions.TryGetValue(id, out Vector2 pos)) continue;
                    _mTeleportCharacter.Invoke(plr, new object[] { pos });
                    n++;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"RestorePlayerPositions 失败：{ex.Message}");
            }
            return n;
        }

        /// <summary>回档前触发 KrokMP SaveGame patch，把当前状态写入 mp_save（含各玩家子目录）。</summary>
        internal static bool TrySaveMpGame()
        {
            return GameSaveBridge.TrySaveGame();
        }

        /// <summary>多人回档准备：krok 引擎无需额外写回，位置由 KrokMP LoadGame 逐人恢复；self 引擎由 WorldEngineArbiter 管种子。</summary>
        internal static void PrepareMpRollback(SlotSidecar sidecar)
        {
            TryResolve();
            if (sidecar == null) return;
            if (WorldEngineArbiter.ResolveEffectiveEngineName(
                    string.IsNullOrEmpty(sidecar.WorldEngine) ? sidecar.MpWorldEngine : sidecar.WorldEngine) != "krok")
                return;
            ModLog.Info("KrokMP 引擎：存档/位置交由联机 mod 继续游戏恢复");
        }

        /// <summary>反射调用 KrokMP WorldgenPatches.LoadVanillaGeneratedWorld(loadsave)，走联机 mod 原生读档继续游戏流程。</summary>
        internal static bool TryLoadMultiplayerContinue(bool loadsave = true)
        {
            TryResolve();
            try
            {
                if (!IsMultiplayerEnabled()) return false;
                if (IsMultiplayerRunning() && !IsServer()) return false;
                if (_mLoadVanillaGeneratedWorld == null)
                {
                    ModLog.Warning("TryLoadMultiplayerContinue：WorldgenPatches.LoadVanillaGeneratedWorld 未找到");
                    return false;
                }
                _mLoadVanillaGeneratedWorld.Invoke(null, new object[] { loadsave });
                ModLog.Info($"已通过 KrokMP LoadVanillaGeneratedWorld(loadsave:{loadsave}) 继续游戏");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"TryLoadMultiplayerContinue 失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>当前已死亡玩家数 = BodyToPlayerDict.Count - AllLivingPlayers.Count。任一字段缺失返回 0。</summary>
        internal static int DeadPlayerCount()
        {
            TryResolve();
            try
            {
                if (_fAllLiving == null || _fBodyToPlayer == null) return 0;
                var living = _fAllLiving.GetValue(null) as System.Collections.ICollection;
                var all = _fBodyToPlayer.GetValue(null) as System.Collections.ICollection;
                if (living == null || all == null) return 0;
                return Math.Max(0, all.Count - living.Count);
            }
            catch { return 0; }
        }

        /// <summary>尝试通过 mp mod 广播屏幕中央大字提示。失败返回 false（外部应自行 fallback 调本地 DoAlert）。</summary>
        internal static bool TryAnnounceAlert(string msg)
        {
            TryResolve();
            try
            {
                if (_mAnnounceAlert == null) return false;
                if (!IsMultiplayerRunning() || !IsServer()) return false;
                _mAnnounceAlert.Invoke(null, new object[] { msg, /*important*/ true, /*reliable*/ true, /*targets*/ null });
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"TryAnnounceAlert 失败：{ex.Message}");
                return false;
            }
        }
    }
}
