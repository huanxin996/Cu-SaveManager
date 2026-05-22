using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;

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
        private static FieldInfo _fIsRunning;        // KrokoshaScavMultiplayer.network_system_is_running
        private static FieldInfo _fIsServer;          // KrokoshaScavMultiplayer.is_server
        private static FieldInfo _fAllLiving;         // NetPlayer.AllLivingPlayers (List<NetPlayer>)
        private static FieldInfo _fBodyToPlayer;      // NetPlayer.BodyToPlayerDict (Dictionary<Body,NetPlayer>)
        private static MethodInfo _mAnnounceAlert;    // ServerMain.Server_AnnounceAlert(in string,bool,bool,IReadOnlyList<uint>)

        private static void TryResolve(ManualLogSource log)
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type tMP = null, tNetPlayer = null, tServerMain = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.Contains("Krokosha")) continue;
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "KrokoshaScavMultiplayer") tMP = t;
                        else if (t.Name == "NetPlayer") tNetPlayer = t;
                        else if (t.Name == "ServerMain") tServerMain = t;
                    }
                    if (tMP != null && tNetPlayer != null && tServerMain != null) break;
                }
                if (tMP == null) return;
                _fIsRunning = tMP.GetField("network_system_is_running",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                _fIsServer = tMP.GetField("is_server",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (tNetPlayer != null)
                {
                    _fAllLiving = tNetPlayer.GetField("AllLivingPlayers",
                        BindingFlags.Public | BindingFlags.Static);
                    _fBodyToPlayer = tNetPlayer.GetField("BodyToPlayerDict",
                        BindingFlags.Public | BindingFlags.Static);
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
                log?.LogInfo($"[SaveManager] MultiplayerBridge 已解析（mp running 字段{(_fIsRunning != null ? "✓" : "✗")} / 广播{(_mAnnounceAlert != null ? "✓" : "✗")}）");
            }
            catch (Exception ex)
            {
                log?.LogWarning($"MultiplayerBridge 反射失败：{ex.Message}");
            }
        }

        internal static bool IsMultiplayerRunning(ManualLogSource log)
        {
            TryResolve(log);
            try { return _fIsRunning != null && (bool)_fIsRunning.GetValue(null); }
            catch { return false; }
        }

        internal static bool IsServer(ManualLogSource log)
        {
            TryResolve(log);
            try { return _fIsServer != null && (bool)_fIsServer.GetValue(null); }
            catch { return false; }
        }

        /// <summary>当前已死亡玩家数 = BodyToPlayerDict.Count - AllLivingPlayers.Count。任一字段缺失返回 0。</summary>
        internal static int DeadPlayerCount(ManualLogSource log)
        {
            TryResolve(log);
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
        internal static bool TryAnnounceAlert(string msg, ManualLogSource log)
        {
            TryResolve(log);
            try
            {
                if (_mAnnounceAlert == null) return false;
                if (!IsMultiplayerRunning(log) || !IsServer(log)) return false;
                _mAnnounceAlert.Invoke(null, new object[] { msg, /*important*/ true, /*reliable*/ true, /*targets*/ null });
                return true;
            }
            catch (Exception ex)
            {
                log?.LogWarning($"TryAnnounceAlert 失败：{ex.Message}");
                return false;
            }
        }
    }
}
