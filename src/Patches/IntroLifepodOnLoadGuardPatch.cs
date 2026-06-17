using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>读档/回档时只跳过原版 WorldPlacePlayer 第 1 层重新生成的 Lifepod 入场仓；散布救生仓 (GenerateLifePods) 不拦。</summary>
    [HarmonyPatch]
    internal static class IntroLifepodOnLoadGuardPatch
    {
        internal static bool Enabled = true;

        /// <summary>由 PrepareWorldForSlot / Continue 准备阶段置 true，命中一次后清零；新层切换不再拦。</summary>
        internal static bool PendingSkipIntroLifepod;

        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(WorldGeneration), "GenerateEntityAtPos");

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(GameObject basObj)
        {
            if (!Enabled) return true;
            if (!SaveSystem.loadedRun) return true;
            if (!PendingSkipIntroLifepod) return true;
            if (basObj == null) return true;
            if (basObj.name != "Lifepod") return true;
            var world = WorldGeneration.world;
            if (world == null) return true;
            if (world.totalTraveled > 0) return true;
            PendingSkipIntroLifepod = false;
            return false;
        }
    }
}
