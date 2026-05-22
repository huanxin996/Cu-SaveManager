using System;
using BepInEx.Logging;

namespace CasualtiesUnknown.SaveManager
{
    internal static class GameSaveBridge
    {
        internal static bool TrySaveGame(ManualLogSource log)
        {
            try
            {
                if (PlayerCamera.main == null) return false;
                if (PlayerCamera.main.body == null) return false;
                if (WorldGeneration.world == null) return false;
                SaveSystem.SaveGame();
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"调用游戏 SaveGame() 失败：{ex.Message}");
                return false;
            }
        }
    }
}
