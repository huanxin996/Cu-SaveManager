using System;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// self 引擎启用时暂时禁用 QoL 的世界生成与出生防卡：令 SeededRunPatcher.ShouldUseQoLSeededWorldgen
    /// 返回 false（QoL 世界生成各阶段退出，地形交本模组种子），并跳过其 FinishWorldGeneration_Postfix 防卡。
    /// 软依赖：QoL 不在则跳过。
    /// </summary>
    internal static class QolSpawnSuppressor
    {
        private static bool _patched;

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                Type t = AccessTools.TypeByName("QoL_Unknown.SeededRunPatcher");
                if (t == null)
                {
                    return;
                }
                var harmony = new Harmony("com.casualtiesUnknown.saveManager.qolSpawnSuppressor");
                bool any = false;

                MethodInfo spawnGuard = t.GetMethod("FinishWorldGeneration_Postfix",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (spawnGuard != null)
                {
                    harmony.Patch(spawnGuard, new HarmonyMethod(typeof(QolSpawnSuppressor).GetMethod(
                        nameof(SkipWhenSelfEngine), BindingFlags.Static | BindingFlags.NonPublic)));
                    any = true;
                }
                else ModLog.Warning("QoL FinishWorldGeneration_Postfix 未找到，出生防卡由本模组处理");

                MethodInfo worldgenGate = t.GetMethod("ShouldUseQoLSeededWorldgen",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (worldgenGate != null)
                {
                    harmony.Patch(worldgenGate, new HarmonyMethod(typeof(QolSpawnSuppressor).GetMethod(
                        nameof(OverrideWorldgenGate), BindingFlags.Static | BindingFlags.NonPublic)));
                    any = true;
                }
                else ModLog.Warning("QoL ShouldUseQoLSeededWorldgen 未找到，世界生成仍由 QoL 处理");

                _patched = true;
                if (any) ModLog.Info("QoL 世界生成/出生防卡接管已挂载（仅 self 引擎启用时生效）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolSpawnSuppressor.TryPatch 失败：{ex.Message}");
            }
        }

        private static bool SkipWhenSelfEngine()
        {
            return !SeededWorldEngine.IsActive;
        }

        private static bool OverrideWorldgenGate(ref bool __result)
        {
            if (SeededWorldEngine.IsActive)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
