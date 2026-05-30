using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 把当前场景所有 GraphicRaycaster.enabled 暂存并设为 false，
    /// 阻止下层 uGUI 接收点击；提供 EnforceBlocked 每帧重设以抵消其他脚本的复位。
    /// </summary>
    internal static class UiBlocker
    {
        private static readonly List<(GraphicRaycaster gr, bool wasEnabled)> _disabled
            = new List<(GraphicRaycaster, bool)>();

        private static bool _isBlocking;

        /// <summary>面板是否处于阻穿透状态。供 Harmony patch 判断是否应吞掉 onClick。</summary>
        internal static bool IsBlocking => _isBlocking;

        internal static void Block()
        {
            try
            {
                _disabled.Clear();
                var all = UnityEngine.Object.FindObjectsOfType<GraphicRaycaster>(includeInactive: true);
                foreach (var gr in all)
                {
                    if (gr == null) continue;
                    _disabled.Add((gr, gr.enabled));
                    gr.enabled = false;
                }
                _isBlocking = true;
            }
            catch (System.Exception ex)
            {
                ModLog.Warning($"UiBlocker.Block 失败：{ex.Message}");
            }
        }

        /// <summary>把列表中所有 GraphicRaycaster.enabled 重设为 false。</summary>
        internal static void EnforceBlocked()
        {
            if (!_isBlocking) return;
            try
            {
                foreach (var (gr, _) in _disabled)
                {
                    if (gr == null) continue;
                    if (gr.enabled) gr.enabled = false;
                }
            }
            catch { }
        }

        internal static void Unblock()
        {
            try
            {
                foreach (var (gr, was) in _disabled)
                {
                    if (gr != null) gr.enabled = was;
                }
                _disabled.Clear();
                _isBlocking = false;
            }
            catch (System.Exception ex)
            {
                ModLog.Warning($"UiBlocker.Unblock 失败：{ex.Message}");
            }
        }
    }
}
