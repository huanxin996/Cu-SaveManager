using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// Postfix patch <see cref="AdaptiveButton"/>.overlayActive getter：
    /// <see cref="UiBlocker.IsBlocking"/> 为 true 时强制 __result = true，
    /// 让主菜单 5 个 AdaptiveButton 跳过自身命中判定；
    /// 鼠标命中 SaveManager 主菜单按钮矩形时也走相同分支。
    /// </summary>
    [HarmonyPatch(typeof(AdaptiveButton), "overlayActive", MethodType.Getter)]
    internal static class AdaptiveButtonOverlayActiveGuard
    {
        private static void Postfix(ref bool __result)
        {
            if (UiBlocker.IsBlocking) { __result = true; return; }
            // 鼠标在 SaveManager 主菜单按钮矩形内时，让 AdaptiveButton 跳过自身命中判定
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

    /// <summary>
    /// Prefix patch <see cref="AdaptiveButton"/>.Clicked()：
    /// <see cref="UiBlocker.IsBlocking"/> 或鼠标命中 SaveManager 主菜单按钮矩形时 return false。
    /// 用 TargetMethod 显式定位避免 HarmonyX 名称解析失败。
    /// </summary>
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
                ModLog.Info("AdaptiveButtonClickedGuard.TargetMethod 命中 AdaptiveButton.Clicked");
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
                        ModLog.Info("AdaptiveButtonClickedGuard 首次拦截穿透点击");
                    }
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    /// <summary>
    /// Prefix patch <see cref="PlayerCamera"/>.HandleInput：
    /// <see cref="UiBlocker.IsBlocking"/> 为 true 时 return false，吞掉游戏的 ESC / 移动 / 攻击等输入。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), "HandleInput")]
    internal static class PlayerCameraHandleInputGuard
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !UiBlocker.IsBlocking;
        }
    }
}
