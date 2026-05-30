using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// self 引擎启用时由本模组处理出生防卡与回档位置写回：仿 QoL 防卡（远离 elder、踩实地、清陷阱），
    /// 回档时把玩家写回 PendingPosition 作为最后一步，覆盖防卡的 PlaceBody。
    /// </summary>
    [HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
    internal static class SelfSpawnPatcher
    {
        private const float ElderSpawnSafeRadius = 45f;

        private static readonly HashSet<string> TrapEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "antirad", "barbedwirefence", "beartrap", "caveticks", "coil", "drillpod", "grabberplant", "grabbershroom", "gunmine", "jumppad",
            "landmine", "overgrowntick", "radbarrel", "shadecrawler", "sidestabber", "sidestabberflip", "snowstrider", "soundcannon", "spentfuel", "spikestabber",
            "thornbackyoung", "wallbiter"
        };

        /// <summary>待写回的玩家世界坐标；读档/回档前由外部置入，Postfix 消费后清空。</summary>
        internal static Vector3? PendingPosition;

        private static void Postfix()
        {
            if (MultiplayerBridge.IsMultiplayerRunning()) return;
            if (!SeededWorldEngine.IsActive) return;
            if (WorldGeneration.world == null || PlayerCamera.main == null || PlayerCamera.main.body == null) return;
            try
            {
                if (!ShouldSkipSpawnSafetyForFreshRunStart())
                {
                    Vector2 spawnPos = PlayerCamera.main.body.transform.position;
                    KeepEldersAwayFromSpawn(spawnPos, ElderSpawnSafeRadius);
                    EnsurePlayerOnSolidGroundLikeVanilla();
                    RemoveNearbyTrapsLikeVanilla();
                }
                ApplyPendingPosition();
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SelfSpawnPatcher 异常：{ex.Message}");
            }
        }

        private static bool ShouldSkipSpawnSafetyForFreshRunStart()
        {
            if (SaveSystem.loadedRun) return false;
            if (WorldGeneration.world == null) return false;
            return WorldGeneration.world.totalTraveled < 50;
        }

        private static void ApplyPendingPosition()
        {
            if (!PendingPosition.HasValue) return;
            Vector3 pos = PendingPosition.Value;
            if (PlayerCamera.main != null && PlayerCamera.main.body != null)
            {
                PlayerCamera.main.body.transform.position = pos;
                PlayerCamera.main.transform.position = new Vector3(pos.x, pos.y, -10f);
                if (PlayerCamera.main.body.rb != null) PlayerCamera.main.body.rb.velocity = Vector2.zero;
                ModLog.Info($"回档位置写回：目标({pos.x:0.0},{pos.y:0.0}) 实际({PlayerCamera.main.body.transform.position.x:0.0},{PlayerCamera.main.body.transform.position.y:0.0})");
            }
            PendingPosition = null;
        }

        private static void KeepEldersAwayFromSpawn(Vector2 spawnPos, float minRadius)
        {
            var all = UnityEngine.Object.FindObjectsOfType<Transform>();
            foreach (var t in all)
            {
                if (t == null || t.gameObject == null) continue;
                string name = t.gameObject.name;
                if (!name.Contains("thornbackelder")) continue;
                Vector2 d = (Vector2)t.position - spawnPos;
                if (d.sqrMagnitude >= minRadius * minRadius) continue;
                if (TryFindValidElderPositionAwayFromSpawn(spawnPos, minRadius, out var pos))
                {
                    t.position = new Vector3(pos.x, pos.y, t.position.z);
                }
            }
        }

        private static bool TryFindValidElderPositionAwayFromSpawn(Vector2 spawnPos, float minRadius, out Vector2 pos)
        {
            pos = spawnPos;
            var world = WorldGeneration.world;
            if (world == null) return false;
            int hw = (int)world.width / 2;
            int hh = (int)world.height / 2;
            for (int i = 0; i < 96; i++)
            {
                float ang = UnityEngine.Random.Range(0f, 360f) * (Mathf.PI / 180f);
                float dist = UnityEngine.Random.Range(minRadius + 10f, Mathf.Min(world.width, world.height) * 0.45f);
                int x = Mathf.RoundToInt(spawnPos.x + Mathf.Cos(ang) * dist);
                int y = Mathf.RoundToInt(spawnPos.y + Mathf.Sin(ang) * dist);
                if (x >= -hw + 2 && x <= hw - 2 && y >= -hh + 2 && y <= hh - 2
                    && IsAirAtWorld(x, y) && !IsAirAtWorld(x, y - 1))
                {
                    pos = new Vector2(x, y);
                    return true;
                }
            }
            return false;
        }

        private static void EnsurePlayerOnSolidGroundLikeVanilla()
        {
            if (PlayerCamera.main == null || PlayerCamera.main.body == null || WorldGeneration.world == null) return;
            Vector3 p = PlayerCamera.main.body.transform.position;
            int x = Mathf.RoundToInt(p.x);
            int y = Mathf.RoundToInt(p.y);
            if (IsPlayerStandLocationValid(x, y)) return;
            PlayerCamera.main.body.PlaceBody();
            p = PlayerCamera.main.body.transform.position;
            x = Mathf.RoundToInt(p.x);
            y = Mathf.RoundToInt(p.y);
            if (!IsPlayerStandLocationValid(x, y)) MovePlayerToNearestOpenLandIfEmbedded();
        }

        private static void RemoveNearbyTrapsLikeVanilla()
        {
            if (PlayerCamera.main == null || PlayerCamera.main.body == null) return;
            Vector2 origin = PlayerCamera.main.body.transform.position;
            const float sqrRadius = 144f;
            var all = UnityEngine.Object.FindObjectsOfType<Transform>();
            foreach (var t in all)
            {
                if (t == null || t.gameObject == null || !IsTrapObject(t.gameObject)) continue;
                Vector2 d = (Vector2)t.position - origin;
                bool near = d.sqrMagnitude <= sqrRadius;
                bool aboveFloor = t.position.y > origin.y - 5f;
                if (near || aboveFloor) UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        private static bool IsTrapObject(GameObject obj)
        {
            if (obj == null) return false;
            if (TrapEntityIds.Contains(NormalizeEntityId(obj.name))) return true;
            var be = obj.GetComponent<BuildingEntity>();
            if (be == null || string.IsNullOrEmpty(be.id)) return false;
            return TrapEntityIds.Contains(NormalizeEntityId(be.id));
        }

        private static string NormalizeEntityId(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string s = value.Replace("(Clone)", "").Trim();
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            return s;
        }

        private static void MovePlayerToNearestOpenLandIfEmbedded()
        {
            if (PlayerCamera.main == null || PlayerCamera.main.body == null || WorldGeneration.world == null) return;
            Vector3 p = PlayerCamera.main.body.transform.position;
            var start = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
            if (IsPlayerStandLocationValid(start.x, start.y)) return;
            if (!TryFindNearestOpenLand(start, 80, out var found)) return;
            var dst = new Vector3(found.x, found.y, p.z);
            PlayerCamera.main.body.transform.position = dst;
            PlayerCamera.main.transform.position = new Vector3(dst.x, dst.y, -10f);
            if (PlayerCamera.main.body.rb != null) PlayerCamera.main.body.rb.velocity = Vector2.zero;
        }

        private static bool TryFindNearestOpenLand(Vector2Int start, int maxRadius, out Vector2Int found)
        {
            found = start;
            var visited = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var dirs = new[] { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (Mathf.Abs(cur.x - start.x) + Mathf.Abs(cur.y - start.y) > maxRadius) continue;
                if (IsPlayerStandLocationValid(cur.x, cur.y)) { found = cur; return true; }
                foreach (var dir in dirs)
                {
                    var next = cur + dir;
                    if (!visited.Contains(next) && IsInsideWorld(next.x, next.y))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return false;
        }

        private static bool IsPlayerStandLocationValid(int x, int y)
        {
            if (!IsInsideWorld(x, y) || !IsInsideWorld(x, y + 1) || !IsInsideWorld(x, y - 1)) return false;
            return IsAirAtWorld(x, y) && IsAirAtWorld(x, y + 1) && !IsAirAtWorld(x, y - 1);
        }

        private static bool IsAirAtWorld(int x, int y) => GetWorldBlock(x, y) == 0;

        private static ushort GetWorldBlock(int x, int y)
        {
            var world = WorldGeneration.world;
            if (world == null || !IsInsideWorld(x, y)) return 0;
            int hw = (int)world.width / 2;
            int hh = (int)world.height / 2;
            int bx = x + hw;
            int by = y + hh;
            if (bx < 0 || by < 0 || bx >= (int)world.width || by >= (int)world.height) return 0;
            var blocks = Traverse.Create(world).Field("worldBlocks").GetValue<ushort[,]>();
            if (blocks == null) return 0;
            return blocks[bx, by];
        }

        private static bool IsInsideWorld(int x, int y)
        {
            var world = WorldGeneration.world;
            if (world == null) return false;
            int hw = (int)world.width / 2;
            int hh = (int)world.height / 2;
            return x >= -hw && x < hw && y >= -hh && y < hh;
        }
    }
}
