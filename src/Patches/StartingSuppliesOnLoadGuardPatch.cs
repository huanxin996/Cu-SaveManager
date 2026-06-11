using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>读档/回档时把 runSettings["startingsupplies"] 视作 0，避免游戏 WorldPlacePlayer 在第 1 层重发起始物资。</summary>
    [HarmonyPatch]
    internal static class StartingSuppliesOnLoadGuardPatch
    {
        internal static bool Enabled = true;

        private static MethodBase TargetMethod()
            => AccessTools.Method(typeof(WorldGeneration), "GetRunSettingInt");

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix(string name, ref int __result)
        {
            if (!Enabled) return true;
            if (!SaveSystem.loadedRun) return true;
            if (name != "startingsupplies") return true;
            __result = 0;
            return false;
        }
    }
}
