using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>读档/回档时跳过原版 WorldPlacePlayer 在第 1 层重新生成的 Lifepod 入场仓。</summary>
    [HarmonyPatch]
    internal static class IntroLifepodOnLoadGuardPatch
    {
        internal static bool Enabled = true;

        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(WorldGeneration), "GenerateEntityAtPos");

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(GameObject basObj)
        {
            if (!Enabled) return true;
            if (!SaveSystem.loadedRun) return true;
            if (basObj == null) return true;
            if (basObj.name != "Lifepod") return true;
            return false;
        }
    }
}
