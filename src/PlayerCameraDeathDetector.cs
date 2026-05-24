using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// Postfix patch <see cref="PlayerCamera.HandleDeathScreen"/>，
    /// 在 <c>didDeathScreen</c> 由 false 翻成 true 时把 <see cref="LocalDeathLatched"/> 置 true，
    /// 用作单机死亡判据。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), "HandleDeathScreen")]
    internal static class PlayerCameraDeathDetector
    {
        private static bool _lastDidDeathScreen;

        /// <summary>给 RollbackController 当事件钩子用。本地玩家死亡判据满足→true，离开死亡屏→false。</summary>
        internal static bool LocalDeathLatched { get; private set; }

        private static void Postfix(PlayerCamera __instance)
        {
            if (__instance == null) return;
            bool now = __instance.didDeathScreen;
            if (now == _lastDidDeathScreen) return;
            _lastDidDeathScreen = now;
            LocalDeathLatched = now;
        }

        /// <summary>给 RollbackController.OnSceneLoaded / Dispose 调用，重置静态态。</summary>
        internal static void Reset()
        {
            _lastDidDeathScreen = false;
            LocalDeathLatched = false;
        }
    }
}
