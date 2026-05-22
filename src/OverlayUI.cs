using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 游戏内（按 ESC 暂停时）的 IMGUI 浮动唤起按钮。
    /// 通过 Harmony patch PreRunScript.Start 克隆原版按钮挂到 PreRunScript.transform 下，
    /// </summary>
    internal sealed class OverlayUI
    {
        private const float ButtonWidth = 320f;
        private const float ButtonHeight = 84f;
        private const float MarginRight = 24f;
        private const float MarginBottom = 24f;

        /// <summary>仅在游戏内 ESC 暂停面板 (brightnessPanel) 打开时显示。</summary>
        internal static bool ShouldShow()
        {
            if (PlayerCamera.main == null) return false;
            var panel = PlayerCamera.main.brightnessPanel;
            return panel != null && panel.activeSelf;
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
