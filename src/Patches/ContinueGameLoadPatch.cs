using System;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    [HarmonyPatch(typeof(PreRunScript), "LoadRun")]
    internal static class ContinueGameLoadPatch
    {
        private static void Prefix()
        {
            try
            {
                if (SaveStore.IsMultiplayerContextActive() || MultiplayerBridge.IsMultiplayerRunning()) return;
                if (!SaveStore.CurrentSaveExists()) return;
                new SaveStore().PrepareCurrentSaveForContinue();
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ContinueGameLoadPatch 失败：{ex.Message}");
            }
        }
    }
}