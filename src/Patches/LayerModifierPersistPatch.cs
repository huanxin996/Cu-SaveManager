using System;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>读档/回档时按 sidecar.ActiveLayerModifierIndex 还原本层词条；阻止游戏 ApplyLayerModifiers 自决。</summary>
    [HarmonyPatch(typeof(WorldGeneration), "ApplyLayerModifiers")]
    internal static class ApplyLayerModifiersRestorePatch
    {
        /// <summary>由 PrepareWorldForSlot / Continue 设置：-2 表示无 pending（交给游戏自滚）；-1 表示明确"本层无词条"；>=0 表示对应 modifierIndex。命中一次后清回 -2。</summary>
        internal static int PendingIndex = -2;

        private static readonly FieldInfo PrefixField = AccessTools.Field(typeof(WorldGeneration), "layerPrefix");
        private static readonly FieldInfo DescField = AccessTools.Field(typeof(WorldGeneration), "layerDescription");

        [HarmonyPrefix]
        private static bool Prefix(WorldGeneration __instance)
        {
            if (PendingIndex == -2) return true;
            int idx = PendingIndex;
            PendingIndex = -2;
            try
            {
                __instance.ResetLayerModifiers();
                if (idx < 0)
                {
                    ModLog.Info("LayerModifierRestorePatch 还原本层无词条");
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
                PrefixField?.SetValue(__instance, Locale.GetOther("layermodifier" + m.modifierIndex));
                DescField?.SetValue(__instance, Locale.GetOther("layermodifier" + m.modifierIndex + "dsc"));
                ModLog.Info($"LayerModifierRestorePatch 还原词条 index={idx}");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"LayerModifierRestorePatch 异常：{ex.Message}");
            }
            return false;
        }
    }

    /// <summary>读取当前激活的 LayerModifier 索引；availableModifiers 未就绪返回 -2（未知，不持久化决定），无激活返回 -1。供 SaveStore 在 SnapshotGameContext 时调用。</summary>
    internal static class LayerModifierSnapshot
    {
        internal static int CurrentActiveIndex()
        {
            try
            {
                var mods = LayerModifier.availableModifiers;
                if (mods == null) return -2;
                foreach (var m in mods)
                {
                    if (m != null && m.active) return m.modifierIndex;
                }
            }
            catch
            {
                return -2;
            }
            return -1;
        }
    }
}
