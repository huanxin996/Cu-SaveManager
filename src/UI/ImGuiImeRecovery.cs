using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// IMGUI TextField + 输入法组合后，<see cref="GUIUtility.keyboardControl"/> 与
    /// <see cref="Input.imeIsSelected"/> 可能残留，导致全局 <see cref="Input.GetKeyDown"/> 失效。
    /// 在面板关闭或不应有文本焦点时，于 Layout 阶段释放焦点。
    /// </summary>
    internal static class ImGuiImeRecovery
    {
        private static bool _pendingClear;

        internal static void RequestClear() => _pendingClear = true;

        /// <summary>Update / LateUpdate：无文本输入需求时标记待清除。</summary>
        internal static void TickUpdate(bool textInputExpected)
        {
            if (textInputExpected) return;
            if (_pendingClear || GUIUtility.keyboardControl != 0 || Input.imeIsSelected)
                _pendingClear = true;
        }

        /// <summary>OnGUI：在 Layout 阶段执行 <see cref="GUI.FocusControl"/>。</summary>
        internal static void TickOnGui(bool textInputExpected)
        {
            if (textInputExpected) return;
            if (!_pendingClear && GUIUtility.keyboardControl == 0 && !Input.imeIsSelected)
                return;

            var ev = Event.current;
            if (ev == null || ev.type != EventType.Layout) return;

            GUI.FocusControl(null);
            _pendingClear = false;
        }
    }
}
