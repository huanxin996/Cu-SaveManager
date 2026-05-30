using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 死亡回档处理中或死亡感知中时阻断游戏自带的"全员死亡 → 自动重生世界 / 自动回主菜单"流程。
    /// 单机正常死亡场景由游戏自身 HandleDeathScreen 处理，不会走这两个方法；
    /// 多人 host 端会在 ServerMain.AutoExitWhenAllDied 触发 ToMainMenu，DrillPod / WorldGenerationContinueRunPatch
    /// 也会触发 RegenerateWorld。
    /// 用 IsActiveGlobal || IsDeathSuspected 双判：从死亡判到的第一帧就拦截，比倒计时启动早。
    /// </summary>
    [HarmonyPatch(typeof(WorldGeneration), "RegenerateWorld")]
    internal static class WorldGenerationRegenerateWorldGuard
    {
        private static bool Prefix()
        {
            return !(RollbackController.IsActiveGlobal || RollbackController.IsDeathSuspected);
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), "ToMainMenu")]
    internal static class PlayerCameraToMainMenuGuard
    {
        private static bool Prefix()
        {
            if (MpRollbackController.ReturningToMenu) return true;
            return !(RollbackController.IsActiveGlobal || RollbackController.IsDeathSuspected);
        }
    }
}
