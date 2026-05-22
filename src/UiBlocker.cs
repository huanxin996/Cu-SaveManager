using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 阻止下层 uGUI 接收点击：遍历当前场景所有 GraphicRaycaster 全部 enabled=false。
    /// 提供 EnforceBlocked 让调用方每帧调用，防止其他 mod 把 enabled 改回 true。
    /// </summary>
    internal static class UiBlocker
    {
        private static readonly List<(GraphicRaycaster gr, bool wasEnabled)> _disabled
            = new List<(GraphicRaycaster, bool)>();

        private static bool _isBlocking;

        /// <summary>面板是否处于阻穿透状态。供 Harmony patch 判断是否应吞掉 onClick。</summary>
        internal static bool IsBlocking => _isBlocking;

        internal static void Block(ManualLogSource log)
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
                log?.LogWarning($"UiBlocker.Block 失败：{ex.Message}");
            }
        }

        /// <summary>每帧调用：把所有列表中 GR 的 enabled 强制刷成 false，防止被其他 mod 改回去。静默执行。</summary>
        internal static void EnforceBlocked(ManualLogSource log)
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

        internal static void Unblock(ManualLogSource log)
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
                log?.LogWarning($"UiBlocker.Unblock 失败：{ex.Message}");
            }
        }
    }
}
