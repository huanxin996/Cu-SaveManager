using System.Reflection;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 游戏内（按 ESC 暂停时）的 IMGUI 浮动唤起按钮。
    /// 主菜单按钮不走 IMGUI，已迁移到 <see cref="MenuButtonUiInjector"/>，
    /// 通过 Harmony patch PreRunScript.Start 克隆原版按钮挂到 PreRunScript.transform 下，
    /// 生命周期与可见性都跟随 PreRunScript 父节点，与多人模组 KrokoshaMainmenuBackground 同款做法。
    /// </summary>
    internal sealed class OverlayUI
    {
        private const float ButtonWidth = 320f;
        private const float ButtonHeight = 84f;
        private const float MarginRight = 24f;
        private const float MarginBottom = 24f;

        private static readonly System.Type PauseHandlerType = typeof(PlayerCamera).Assembly.GetType("PauseHandler");
        private static readonly PropertyInfo PauseHandlerPausedProperty =
            PauseHandlerType?.GetProperty("paused", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo PauseHandlerMainField =
            PauseHandlerType?.GetField("main", BindingFlags.Public | BindingFlags.Static);
        private static readonly FieldInfo PauseHandlerContainerField =
            PauseHandlerType?.GetField("pauseContainer", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo BrightnessPanelField =
            typeof(PlayerCamera).GetField("brightnessPanel", BindingFlags.Public | BindingFlags.Instance);

        /// <summary>仅在游戏内暂停菜单打开时显示；兼容旧版 brightnessPanel 与新版 PauseHandler.pauseContainer。</summary>
        internal static bool ShouldShow()
        {
            if (PlayerCamera.main == null) return false;

            if (TryGetPauseMenuVisible(out bool visible)) return visible;
            if (BrightnessPanelField == null) return false;

            try
            {
                var panel = BrightnessPanelField.GetValue(PlayerCamera.main) as GameObject;
                return panel != null && panel.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPauseMenuVisible(out bool visible)
        {
            visible = false;
            if (PauseHandlerType == null) return false;

            try
            {
                bool paused = PauseHandlerPausedProperty?.GetValue(null, null) is bool pausedValue && pausedValue;
                var main = PauseHandlerMainField?.GetValue(null);
                var pauseContainer = main != null ? PauseHandlerContainerField?.GetValue(main) as GameObject : null;
                visible = paused || (pauseContainer != null && pauseContainer.activeSelf);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal void Draw(System.Action onClick)
        {
            if (!ShouldShow()) return;

            float x = Screen.width - ButtonWidth - MarginRight;
            float y = Screen.height - ButtonHeight - MarginBottom;
            var rect = new Rect(x, y, ButtonWidth, ButtonHeight);

            BlackWhiteSkin.Push();
            try
            {
                if (GUI.Button(rect, I18n.T("app.menu_button"))) onClick?.Invoke();
                BlackWhiteSkin.DrawBorder(rect, 5f);
            }
            finally
            {
                BlackWhiteSkin.Pop();
            }
        }
    }
}
