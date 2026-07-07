using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    [HarmonyPatch(typeof(LifepodPump), "Start")]
    internal static class LifepodPumpBoundsPatch
    {
        private static void Postfix(LifepodPump __instance)
        {
            var world = WorldGeneration.world;
            if (world == null || __instance == null) return;
            int maxX = (int)world.width - 1;
            int maxY = (int)world.height - 1;
            __instance.pumpMin = Clamp(__instance.pumpMin, maxX, maxY);
            __instance.pumpMax = Clamp(__instance.pumpMax, maxX, maxY);
            __instance.pumpOut = Clamp(__instance.pumpOut, maxX, maxY);
        }

        private static Vector2Int Clamp(Vector2Int v, int maxX, int maxY)
            => new Vector2Int(Mathf.Clamp(v.x, 0, maxX), Mathf.Clamp(v.y, 0, maxY));
    }
}
