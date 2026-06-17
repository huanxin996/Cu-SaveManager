using System;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>vanilla SaveGame 后立即把当前玩家位置 + biome 写到 save.sv 旁的 sidecar，让 Continue 能还原位置。</summary>
    [HarmonyPatch(typeof(SaveSystem), "SaveGame")]
    internal static class SaveGamePersistPositionPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { new SaveStore().PersistCurrentSaveForContinue(); }
            catch (Exception ex) { ModLog.Warning($"SaveGamePersistPositionPatch 失败：{ex.Message}"); }
        }
    }

    /// <summary>玩家退到主菜单前抓最后位置写到 sidecar，覆盖此次会话最新位置。</summary>
    [HarmonyPatch(typeof(PlayerCamera), "ToMainMenu")]
    internal static class ToMainMenuPersistPositionPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { new SaveStore().PersistCurrentSaveForContinue(); }
            catch (Exception ex) { ModLog.Warning($"ToMainMenuPersistPositionPatch 失败：{ex.Message}"); }
        }
    }
}
