using System;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 调 SaveSystem.SaveGame() 的保护壳。需要 PlayerCamera.main / body / WorldGeneration.world 三者齐全，
    /// 主菜单或加载场景下直接调会 NRE，故先做存在性检查。
    /// </summary>
    internal static class GameSaveBridge
    {
        /// <returns>true 表示真的调了游戏 SaveGame，false 表示被检查拦下了。</returns>
        internal static bool TrySaveGame()
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
                ModLog.Warning($"调用游戏 SaveGame() 失败：{ex.Message}");
                return false;
            }
        }
    }
}
