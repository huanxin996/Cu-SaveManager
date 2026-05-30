using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人回档后把各玩家传送回存档位置（KrokMP 存了 mp_rules.json 的 PLRPOS 但加载时不用）。
    /// 回档还原 mp_save 时缓存 PLRPOS，世界生成完成后由主机按 persistentId 写回。
    /// </summary>
    [HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
    internal static class MpPositionRestorer
    {
        private static Dictionary<string, Vector2> _pending;

        /// <summary>记录待恢复的各玩家位置（persistentId -> 坐标）；下次多人世界生成完成后写回一次。</summary>
        internal static void SetPending(Dictionary<string, Vector2> byPersistentId)
        {
            _pending = byPersistentId;
        }

        private static void Postfix()
        {
            if (_pending == null || _pending.Count == 0) return;
            if (!MultiplayerBridge.IsMultiplayerRunning() || !MultiplayerBridge.IsServer()) return;
            var runner = PlayerCamera.main;
            if (runner == null) return;
            runner.StartCoroutine(ApplyAfterPlacement());
        }

        private static IEnumerator ApplyAfterPlacement()
        {
            yield return null;
            yield return null;
            var pending = _pending;
            _pending = null;
            if (pending == null) yield break;
            int n = MultiplayerBridge.RestorePlayerPositions(pending);
            ModLog.Info($"多人回档位置写回：{n} 名玩家");
        }
    }
}
