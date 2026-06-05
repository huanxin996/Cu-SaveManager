using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 在 WorldGeneration 各生成阶段 Prefix 用 LayerSeed+阶段偏移重置 UnityEngine.Random，
    /// 使地形与实体分布可复现。仅 SeededWorldEngine.IsActive 且单机时介入。
    /// 显式逐方法挂载，避免 HarmonyX 对多目标特性类自动发现失效。
    /// </summary>
    internal static class SeededWorldPatcher
    {
        private static bool _patched;

        private static readonly (string method, int offset)[] PhaseStages =
        {
            ("GenerateWorld", 1000),
            ("WorldPreprocess", 2000),
            ("WorldCreateBackground", 3000),
            ("WorldGenerateTerrain", 4000),
            ("WorldGenerateWorldBorders", 4500),
            ("WorldGenerateStructures", 5000),
            ("WorldPlacePlayer", 6000),
            ("WorldPlaceEntities", 7000),
            ("FinishWorldGeneration", 8000),
            ("InstantiateWorld", 500),
            ("GenerateOres", 5400),
            ("PlaceCrystals", 9100),
            ("DistributeMiniBarrels", 9200),
        };

        private static bool On => SeededWorldEngine.IsActive && !MultiplayerBridge.IsMultiplayerRunning();

        /// <summary>显式挂载世界生成各阶段 Prefix；幂等。</summary>
        internal static void TryPatch(Harmony harmony)
        {
            if (_patched) return;
            _patched = true;
            try
            {
                var t = typeof(SeededWorldPatcher);
                Patch(harmony, "Start", new HarmonyMethod(t.GetMethod(nameof(Start_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                var phasePrefix = new HarmonyMethod(t.GetMethod(nameof(Phase_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                foreach (var stage in PhaseStages) Patch(harmony, stage.method, phasePrefix);
                Patch(harmony, "GenerateTree", new HarmonyMethod(t.GetMethod(nameof(GenerateTree_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                Patch(harmony, "GenerateBigMushroom", new HarmonyMethod(t.GetMethod(nameof(GenerateBigMushroom_Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                ModLog.Info("世界引擎 patch 已挂载");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SeededWorldPatcher.TryPatch 失败：{ex.Message}");
            }
        }

        private static void Patch(Harmony harmony, string method, HarmonyMethod prefix)
        {
            var mi = AccessTools.Method(typeof(WorldGeneration), method);
            if (mi == null) { ModLog.Warning($"世界引擎 patch 跳过：WorldGeneration.{method} 未找到"); return; }
            harmony.Patch(mi, prefix);
        }

        private static int StageOffset(MethodBase __originalMethod)
        {
            string name = __originalMethod.Name;
            foreach (var s in PhaseStages) if (s.method == name) return s.offset;
            return 0;
        }

        private static void Start_Prefix()
        {
            QolSpawnSuppressor.TryPatch();
            MpWorldSeedInjector.TryPatch();
            if (!SaveSystem.loadedRun) WorldEngineArbiter.ApplyForFreshRun();
            ModLog.Info($"世界生成开始：loadedRun={SaveSystem.loadedRun} engine={WorldEngineArbiter.Current} effective={WorldEngineArbiter.ResolveEffectiveEngineName()} selfActive={SeededWorldEngine.IsActive} seed={SeededWorldEngine.CurrentSeed} layerSeed={SeededWorldEngine.LayerSeed()}");
        }

        private static void Phase_Prefix(MethodBase __originalMethod)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + StageOffset(__originalMethod));
        }

        private static void GenerateTree_Prefix(Vector2Int pos)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5500 + pos.x * 31 + pos.y * 17);
        }

        private static void GenerateBigMushroom_Prefix(Vector2Int pos)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5600 + pos.x * 31 + pos.y * 17);
        }
    }
}
