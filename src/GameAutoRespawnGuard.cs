using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 死亡回档"接管中"或"死亡感知中"时阻断游戏自带的"全员死亡 → 自动重生世界 / 自动回主菜单"流程。
    /// 单机正常死亡场景由游戏自身 HandleDeathScreen 处理，不会走这两个方法；
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
            return !(RollbackController.IsActiveGlobal || RollbackController.IsDeathSuspected);
        }
    }
}
