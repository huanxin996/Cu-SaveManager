using System;
using System.Collections.Generic;
using System.Reflection;

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
        private static MethodInfo _mGetPersistentId;  // NetPlayer.GetPersistentId() -> string
        private static MethodInfo _mTeleportCharacter; // NetPlayer.Server_TeleportCharacter(Vector2)

        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

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
                tMP = krokoshaAsm.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
                tNetPlayer = krokoshaAsm.GetType("KrokoshaCasualtiesMP.NetPlayer");
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
                    _mGetPersistentId = tNetPlayer.GetMethod("GetPersistentId",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    _mTeleportCharacter = tNetPlayer.GetMethod("Server_TeleportCharacter",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
                ModLog.Info($"MultiplayerBridge 已解析（running{(_mIsRunning != null ? "✓" : "✗")} / server{(_mIsServer != null ? "✓" : "✗")} / 广播{(_mAnnounceAlert != null ? "✓" : "✗")}）");
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

        /// <summary>把各玩家传送回保存位置（按 persistentId 匹配）。仅主机有效；返回成功写回的玩家数。
        /// 用 KrokMP 的 Server_TeleportCharacter（自带向客户端同步），不重建角色不掉物品。</summary>
        internal static int RestorePlayerPositions(Dictionary<string, UnityEngine.Vector2> byPersistentId)
        {
            TryResolve();
            if (byPersistentId == null || byPersistentId.Count == 0) return 0;
            if (_fBodyToPlayer == null || _mGetPersistentId == null || _mTeleportCharacter == null) return 0;
            if (!IsMultiplayerRunning() || !IsServer()) return 0;
            int applied = 0;
            try
            {
                var dict = _fBodyToPlayer.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return 0;
                foreach (var player in dict.Values)
                {
                    if (player == null) continue;
                    string pid = _mGetPersistentId.Invoke(player, null) as string;
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!byPersistentId.TryGetValue(pid, out var pos)) continue;
                    _mTeleportCharacter.Invoke(player, new object[] { pos });
                    applied++;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"RestorePlayerPositions 失败：{ex.Message}");
            }
            return applied;
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
