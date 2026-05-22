using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    internal static class PreRunGuardLog
    {
        internal static ManualLogSource Log;
    }

    [HarmonyPatch(typeof(AdaptiveButton), "overlayActive", MethodType.Getter)]
    internal static class AdaptiveButtonOverlayActiveGuard
    {
        private static void Postfix(ref bool __result)
        {
            if (UiBlocker.IsBlocking) { __result = true; return; }
            // 防止主菜单 5 个按钮（Play/Quit/Tutorial/Settings/Music）穿透接收点击。
            var rt = MenuButtonUiInjector.InjectedRect;
            if (rt == null) return;
            try
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                {
                    __result = true;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class AdaptiveButtonClickedGuard
    {
        private static bool _logged;
        private static bool _patchLogged;

        private static System.Reflection.MethodBase TargetMethod()
        {
            var m = AccessTools.Method(typeof(AdaptiveButton), "Clicked");
            if (m != null && !_patchLogged)
            {
                _patchLogged = true;
                PreRunGuardLog.Log?.LogInfo("[SaveManager] AdaptiveButtonClickedGuard.TargetMethod 命中 AdaptiveButton.Clicked");
            }
            return m;
        }

        private static bool Prefix()
        {
            if (UiBlocker.IsBlocking) return false;
            var rt = MenuButtonUiInjector.InjectedRect;
            if (rt == null) return true;
            try
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                {
                    if (!_logged)
                    {
                        _logged = true;
                        PreRunGuardLog.Log?.LogInfo("[SaveManager] AdaptiveButtonClickedGuard 首次拦截穿透点击");
                    }
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
