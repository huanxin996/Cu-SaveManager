using System;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人回档两阶段触发：仅主机可用。
    /// 阶段一（游戏内）：mp_save 已被还原后，调 PlayerCamera.ToMainMenu 让全员回主菜单并置待回档标志。
    /// 阶段二（回到主菜单）：PreRunScript.Start 后反射调用 KrokMP LoadVanillaGeneratedWorld(loadsave:true)，走联机 mod 原生继续游戏读档。
    /// </summary>
    internal static class MpRollbackController
    {
        private static bool _pendingHostReload;

        /// <summary>多人回档正在让主机回主菜单的窗口期；GameAutoRespawnGuard 据此放行这次 ToMainMenu。</summary>
        internal static bool ReturningToMenu { get; private set; }

        /// <summary>阶段一：标记待回档并让主机回主菜单。要求调用前 mp_save 已被槽位 zip 覆盖还原。</summary>
        internal static void TriggerHostReload()
        {
            _pendingHostReload = true;
            ModLog.Info(I18n.T("mp.rollback_returning_menu"));
            ReturningToMenu = true;
            try
            {
                var cam = PlayerCamera.main;
                if (cam != null) cam.ToMainMenu();
                else ModLog.Warning("PlayerCamera.main 为空，多人回档无法回主菜单");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpRollbackController.TriggerHostReload 失败：{ex.Message}");
            }
            finally
            {
                ReturningToMenu = false;
            }
        }

        /// <summary>阶段二：主菜单 PreRunScript 就绪后调用。待回档且为主机时反射走 KrokMP 继续游戏。</summary>
        internal static void OnPreRunReady()
        {
            if (!_pendingHostReload) return;
            _pendingHostReload = false;
            if (!MultiplayerBridge.IsServer())
            {
                ModLog.Warning("待多人回档但当前非主机，已取消");
                return;
            }
            try
            {
                ModLog.Info(I18n.T("mp.rollback_reloading"));
                if (!MultiplayerBridge.TryLoadMultiplayerContinue())
                    ModLog.Warning("多人回档：KrokMP 继续游戏调用失败");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpRollbackController.OnPreRunReady 失败：{ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(PreRunScript), "Start")]
    internal static class PreRunScriptReadyForMpRollback
    {
        private static void Postfix()
        {
            MpRollbackController.OnPreRunReady();
        }
    }
}
