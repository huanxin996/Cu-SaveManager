using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人回档位置写回：对齐单机 SelfSpawnPatcher（FinishWorldGeneration Postfix 最后写回）。
    /// 不用 Krok OnWorldgenFinish 事件（会拖垮 Krok 协程）；改 Harmony Postfix + 抑制 PlaceBody 广播。
    /// </summary>
    internal static class MpPositionRestorer
    {
        private static bool _harmonyPatched;
        private static bool _activeSession;
        private static Dictionary<string, Vector2> _pending;

        internal static bool ActiveSession => _activeSession;

        internal static void TryPatchHarmony()
        {
            if (_harmonyPatched) return;
            try
            {
                var harmony = new Harmony("com.casualtiesUnknown.saveManager.mpPosition");
                var finish = AccessTools.Method(typeof(WorldGeneration), "FinishWorldGeneration");
                if (finish != null)
                {
                    harmony.Patch(finish, postfix: new HarmonyMethod(typeof(MpPositionFinishPatch), nameof(MpPositionFinishPatch.Postfix)));
                }
                MpLateSpawnSuppressor.TryPatch();
                MpPlaceBodySilencer.TryPatch(harmony);
                _harmonyPatched = true;
                ModLog.Info("多人回档位置写回已挂载（FinishWorldGeneration Postfix）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpPositionRestorer.TryPatchHarmony 失败：{ex.Message}");
            }
        }

        internal static void PrepareForRollback(SlotSidecar sidecar)
        {
            TryPatchHarmony();
            var positions = ReadPlrPosFromMpRules();
            if (positions.Count == 0)
                ModLog.Warning("多人回档：mp_rules 无 PLRPOS，仅尝试 sidecar 主机坐标");

            if (sidecar != null && !MultiplayerBridge.IsDedicatedServer())
            {
                var hostPos = ResolveHostPosition(sidecar);
                if (hostPos.HasValue)
                {
                    string hostId = MultiplayerBridge.GetLocalPersistentId();
                    if (string.IsNullOrEmpty(hostId))
                    {
                        foreach (var k in positions.Keys) { hostId = k; break; }
                    }
                    if (!string.IsNullOrEmpty(hostId))
                    {
                        positions[hostId] = hostPos.Value;
                        ModLog.Info($"多人回档：sidecar 覆盖主机 {hostId} ({hostPos.Value.x:0.0},{hostPos.Value.y:0.0})");
                    }
                }
            }
            else if (MultiplayerBridge.IsDedicatedServer())
            {
                ModLog.Info("多人回档：Dedicated Server跳过sidecar主机坐标");
            }

            if (positions.Count == 0)
            {
                ModLog.Warning("多人回档：无可恢复的玩家坐标");
                return;
            }

            _pending = positions;
            _activeSession = true;
            ModLog.Info($"多人回档：已缓存 {positions.Count} 名玩家位置");
            foreach (var kv in positions)
                ModLog.Info($"  PLRPOS {kv.Key} = ({kv.Value.x:0.0},{kv.Value.y:0.0})");
        }

        internal static void ApplyAfterFinishWorldGen()
        {
            if (!_activeSession || _pending == null || _pending.Count == 0) return;
            TryApplyOnce();
            void EndSession()
            {
                _activeSession = false;
                _pending = null;
            }
            var runner = MpRollbackRunner.Instance;
            if (runner != null)
                runner.RunApplyRetries(TryApplyOnce, EndSession);
            else
                EndSession();
        }

        private static Vector2? ResolveHostPosition(SlotSidecar sidecar)
        {
            if (sidecar == null) return null;
            if (string.Equals(sidecar.PosMode, "fixedPos", StringComparison.OrdinalIgnoreCase))
                return new Vector2(sidecar.FixedX, sidecar.FixedY);
            if (sidecar.HasPlayerPos) return new Vector2(sidecar.PlayerX, sidecar.PlayerY);
            return null;
        }

        private static void TryApplyOnce()
        {
            if (_pending == null || _pending.Count == 0) return;
            if (!MultiplayerBridge.IsMultiplayerRunning()) return;

            ApplyLocalHostPosition(_pending);

            if (!MultiplayerBridge.IsServer()) return;
            int n = MultiplayerBridge.RestorePlayerPositions(_pending);
            ModLog.Info($"多人回档位置写回（Server_TeleportCharacter）：{n} 名玩家");
        }

        /// <summary>仿 SelfSpawnPatcher.ApplyPendingPosition：先写本地 body+camera。</summary>
        private static void ApplyLocalHostPosition(Dictionary<string, Vector2> positions)
        {
            try
            {
                string hostId = MultiplayerBridge.GetLocalPersistentId();
                if (string.IsNullOrEmpty(hostId) || !positions.TryGetValue(hostId, out Vector2 pos)) return;
                var cam = PlayerCamera.main;
                if (cam == null || cam.body == null) return;
                cam.body.transform.position = new Vector3(pos.x, pos.y, 0f);
                cam.transform.position = new Vector3(pos.x, pos.y, -10f);
                if (cam.body.rb != null) cam.body.rb.velocity = Vector2.zero;
                ModLog.Info($"多人回档本地写回：({pos.x:0.0},{pos.y:0.0})");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ApplyLocalHostPosition 失败：{ex.Message}");
            }
        }

        internal static Dictionary<string, Vector2> ReadPlrPosFromMpRules(string rulesPath = null)
        {
            var result = new Dictionary<string, Vector2>();
            rulesPath = rulesPath ?? MpSaveLocator.MpRulesPath;
            try
            {
                if (!File.Exists(rulesPath)) return result;
                string json = File.ReadAllText(rulesPath);
                int idx = json.IndexOf("\"PLRPOS\"", StringComparison.Ordinal);
                if (idx < 0) return result;
                int brace = json.IndexOf('{', idx);
                if (brace < 0) return result;
                int depth = 0, end = brace;
                for (int i = brace; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}')
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                string block = json.Substring(brace + 1, end - brace - 1);
                int p = 0;
                while (p < block.Length)
                {
                    int q1 = block.IndexOf('"', p);
                    if (q1 < 0) break;
                    int q2 = block.IndexOf('"', q1 + 1);
                    if (q2 < 0) break;
                    string id = block.Substring(q1 + 1, q2 - q1 - 1);
                    int objStart = block.IndexOf('{', q2);
                    if (objStart < 0) break;
                    int objEnd = block.IndexOf('}', objStart);
                    if (objEnd < 0) break;
                    string inner = block.Substring(objStart, objEnd - objStart + 1);
                    float x = ParseCoord(inner, "Item1");
                    if (Math.Abs(x) < 0.0001f) x = ParseCoord(inner, "x");
                    float y = ParseCoord(inner, "Item2");
                    if (Math.Abs(y) < 0.0001f) y = ParseCoord(inner, "y");
                    if (id.Length > 0) result[id] = new Vector2(x, y);
                    p = objEnd + 1;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ReadPlrPosFromMpRules 失败：{ex.Message}");
            }
            return result;
        }

        private static float ParseCoord(string inner, string field)
        {
            int k = inner.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (k < 0) return 0f;
            int colon = inner.IndexOf(':', k);
            if (colon < 0) return 0f;
            int s = colon + 1;
            while (s < inner.Length && (inner[s] == ' ' || inner[s] == '\t')) s++;
            int e = s;
            while (e < inner.Length && (char.IsDigit(inner[e]) || inner[e] == '-' || inner[e] == '.' || inner[e] == 'e' || inner[e] == 'E' || inner[e] == '+')) e++;
            if (e <= s) return 0f;
            float.TryParse(inner.Substring(s, e - s),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            return v;
        }
    }

    [HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
    [HarmonyPriority(Priority.Last)]
    internal static class MpPositionFinishPatch
    {
        internal static void Postfix()
        {
            try { MpPositionRestorer.ApplyAfterFinishWorldGen(); }
            catch (Exception ex) { ModLog.Warning($"MpPositionFinishPatch 异常：{ex.Message}"); }
        }
    }

    /// <summary>回档读档时静音 Krok PlaceBody Postfix，避免把全员拉到层顶出生点。</summary>
    internal static class MpPlaceBodySilencer
    {
        private static bool _patched;

        internal static void TryPatch(Harmony harmony)
        {
            if (_patched) return;
            var asm = MultiplayerBridge.GetKrokAssembly();
            if (asm == null) return;
            var t = asm.GetType("KrokoshaCasualtiesMP.Body_PlaceBody_MultiplayerPatch");
            if (t == null) return;
            var postfix = AccessTools.Method(t, "Postfix");
            if (postfix == null) return;
            harmony.Patch(postfix, prefix: new HarmonyMethod(typeof(MpPlaceBodySilencer), nameof(BlockDuringRollback)));
            _patched = true;
            ModLog.Info("多人 PlaceBody 广播抑制已挂载");
        }

        private static bool BlockDuringRollback()
            => !(MpPositionRestorer.ActiveSession && SaveSystem.loadedRun);
    }
}
