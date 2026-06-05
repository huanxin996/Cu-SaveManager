using System;
using HarmonyLib;

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

        /// <summary>世界正在进下一层（RegenerateWorld）：此时 biomeDepth 已递增，不能作为当前层快照依据。</summary>
        internal static bool IsWorldRegenerating()
        {
            try
            {
                var world = WorldGeneration.world;
                if (world == null) return false;
                if (world.generatingWorld) return true;
                var f = AccessTools.Field(typeof(WorldGeneration), "doingRegen");
                if (f != null && f.GetValue(world) is bool doing && doing) return true;
            }
            catch { }
            return false;
        }

        internal static bool CanSnapshotCurrentLayer()
            => !IsWorldRegenerating();
    }
}
