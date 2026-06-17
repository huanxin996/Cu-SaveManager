using System;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>读档/回档时按 sidecar.ActiveLayerModifierIndex 还原本层词条；阻止游戏 ApplyLayerModifiers 自决。</summary>
    [HarmonyPatch(typeof(WorldGeneration), "ApplyLayerModifiers")]
    internal static class ApplyLayerModifiersRestorePatch
    {
        /// <summary>由 PrepareWorldForSlot / Continue 设置：-2 表示无 pending；-1 表示明确"无词条"；>=0 表示对应 modifierIndex。命中后清零回 -2。</summary>
        internal static int PendingIndex = -2;

        [HarmonyPrefix]
        private static bool Prefix(WorldGeneration __instance)
        {
            if (PendingIndex == -2) return true;
            int idx = PendingIndex;
            PendingIndex = -2;
            try
            {
                ResetActiveModifiers(__instance);
                if (idx < 0)
                {
                    __instance.layerPrefix = "";
                    __instance.layerDescription = "";
                    return false;
                }
                var mods = LayerModifier.availableModifiers;
                if (mods == null || idx >= mods.Length)
                {
                    ModLog.Warning($"LayerModifierRestorePatch: 索引越界 idx={idx} len={mods?.Length ?? 0}");
                    return false;
                }
                var m = mods[idx];
                m.Initialize(__instance);
                m.active = true;
                __instance.layerPrefix = Locale.GetOther("layermodifier" + m.modifierIndex);
                __instance.layerDescription = Locale.GetOther("layermodifier" + m.modifierIndex + "dsc");
                ModLog.Info($"LayerModifierRestorePatch 还原词条 index={idx}");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"LayerModifierRestorePatch 异常：{ex.Message}");
            }
            return false;
        }

        private static void ResetActiveModifiers(WorldGeneration world)
        {
            var mods = LayerModifier.availableModifiers;
            if (mods == null) return;
            foreach (var m in mods)
            {
                if (m == null || !m.active) continue;
                try { m.Disable(world); } catch { }
                m.active = false;
            }
        }
    }

    /// <summary>读取当前激活的 LayerModifier 索引；无激活返回 -1。供 SaveStore 在 SnapshotGameContext 时调用。</summary>
    internal static class LayerModifierSnapshot
    {
        internal static int CurrentActiveIndex()
        {
            try
            {
                var mods = LayerModifier.availableModifiers;
                if (mods == null) return -1;
                foreach (var m in mods)
                {
                    if (m != null && m.active) return m.modifierIndex;
                }
            }
            catch { }
            return -1;
        }
    }
}
