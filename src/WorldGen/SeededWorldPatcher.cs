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
    /// PlaceLiquids、各 Pod、ApplyLayerModifiers 等）逐一即时重置，保证同种子下世界可复现。
    /// </summary>
    internal static class SeededWorldPatcher
    {
        private static bool _patched;

        // 噪声步进 / DistributeEntities 调用序号计数器。
        private static int _noiseGenStep;
        private static int _distributeEntitiesCallIndex;

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

        // 单机：self 引擎激活即介入。多人：仅在拿到主机广播的固定世界种子后介入（MpReseedEnabled），
        // 使全员逐阶段重置、世界与同种子单机一致；KrokMP 原生世界则不介入。
        private static bool On => SeededWorldEngine.IsActive
            && (!MultiplayerBridge.IsMultiplayerRunning() || SeededWorldEngine.MpReseedEnabled);

        /// <summary>显式挂载世界生成各阶段 Prefix；幂等。</summary>
        internal static void TryPatch(Harmony harmony)
        {
            if (_patched) return;
            _patched = true;
            try
            {
                var t = typeof(SeededWorldPatcher);

                Patch(harmony, "Start", Pre(t, nameof(Start_Prefix)));

                // 协程阶段：作为基线重置（首个 yield 之前的同步抽随机有效）。
                var phasePrefix = Pre(t, nameof(Phase_Prefix));
                foreach (var stage in PhaseStages) Patch(harmony, stage.method, phasePrefix);

                // 协程内同步子方法：yield 之后才执行，必须各自即时重置（这才是真正决定确定性的部分）。
                Patch(harmony, "GenerateTree", Pre(t, nameof(GenerateTree_Prefix)));
                Patch(harmony, "GenerateBigMushroom", Pre(t, nameof(GenerateBigMushroom_Prefix)));
                Patch(harmony, "DistributeEntities", Pre(t, nameof(DistributeEntities_Prefix)));
                Patch(harmony, "PlaceLiquids", Pre(t, nameof(PlaceLiquids_Prefix)));
                Patch(harmony, "GenerateLifePods", Pre(t, nameof(GenerateLifePods_Prefix)));
                Patch(harmony, "GenerateDropCapsules", Pre(t, nameof(GenerateDropCapsules_Prefix)));
                Patch(harmony, "GenerateCollapsedPods", Pre(t, nameof(GenerateCollapsedPods_Prefix)));
                // 与读档还原补丁 ApplyLayerModifiersRestorePatch 共存：读/回档时原方法被 return false 跳过，
                // 此处重置无害；新开局时为随机选取提供确定性。
                Patch(harmony, "ApplyLayerModifiers", Pre(t, nameof(ApplyLayerModifiers_Prefix)));

                // 进层 / 重生成 / 清理：重置计数器与种子。
                Patch(harmony, "ContinueRun", Pre(t, nameof(ContinueRun_Prefix)));
                Patch(harmony, "RegenerateWorld", Pre(t, nameof(ResetCounters_Prefix)));
                Patch(harmony, "Clear", null, Post(t, nameof(Clear_Postfix)));

                // 地形/洞穴噪声：FastNoiseLite 构造函数是同步执行点，直接决定地图形状能否复现。
                PatchFastNoiseCtor(harmony, t);

                // 战利品确定化：商人库存 / 可开启容器（运行时触发，前后保存还原 Random.state 避免污染全局流）。
                PatchExternal(harmony, typeof(TraderScript), "GenerateInventory",
                    Pre(t, nameof(Trader_GenerateInv_Prefix)), Post(t, nameof(Trader_GenerateInv_Postfix)));
                PatchExternal(harmony, typeof(Openable), "OnUse",
                    Pre(t, nameof(Openable_OnUse_Prefix)), Post(t, nameof(Openable_OnUse_Postfix)));

                ModLog.Info("世界引擎 patch 已挂载（含同步子方法 + FastNoiseLite + 战利品确定化）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SeededWorldPatcher.TryPatch 失败：{ex.Message}");
            }
        }

        private static HarmonyMethod Pre(Type t, string name)
            => new HarmonyMethod(t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        private static HarmonyMethod Post(Type t, string name)
            => new HarmonyMethod(t.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        private static void Patch(Harmony harmony, string method, HarmonyMethod prefix, HarmonyMethod postfix = null)
        {
            var mi = AccessTools.Method(typeof(WorldGeneration), method);
            if (mi == null) { ModLog.Warning($"世界引擎 patch 跳过：WorldGeneration.{method} 未找到"); return; }
            harmony.Patch(mi, prefix, postfix);
        }

        private static void PatchExternal(Harmony harmony, Type type, string method, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            var mi = AccessTools.Method(type, method);
            if (mi == null) { ModLog.Warning($"战利品确定化 patch 跳过：{type?.Name}.{method} 未找到"); return; }
            harmony.Patch(mi, prefix, postfix);
        }

        private static void PatchFastNoiseCtor(Harmony harmony, Type t)
        {
            var fn = AccessTools.TypeByName("FastNoiseLite");
            if (fn == null) { ModLog.Warning("FastNoiseLite 类型未找到，噪声确定化跳过"); return; }
            var ctor = AccessTools.Constructor(fn, new[] { typeof(int) });
            if (ctor == null) { ModLog.Warning("FastNoiseLite(int) 构造未找到，噪声确定化跳过"); return; }
            harmony.Patch(ctor, Pre(t, nameof(FastNoiseCtor_Prefix)));
        }

        private static int StageOffset(MethodBase __originalMethod)
        {
            string name = __originalMethod.Name;
            foreach (var s in PhaseStages) if (s.method == name) return s.offset;
            return 0;
        }

        /// <summary>稳定字符串散列（用于 DistributeEntities 种子组合）。</summary>
        private static int StableHashRaw(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            int num = 23;
            foreach (char c in str) num = num * 31 + c;
            return num;
        }

        private static int ComposeDistributeEntitySeed(GameObject basObj, float minPerChunk, float maxPerChunk, int callIndex)
        {
            int layerSeed = SeededWorldEngine.LayerSeed();
            int stableHash = StableHashRaw(basObj != null ? basObj.name : string.Empty);
            int a = (int)(minPerChunk * 1000f);
            int b = (int)(maxPerChunk * 100f);
            return layerSeed + stableHash + a + b + callIndex * 486187739;
        }

        private static void Start_Prefix()
        {
            QolSpawnSuppressor.TryPatch();
            MpWorldSeedInjector.TryPatch();
            MpSeedBroadcast.TryPatch();
            if (!SaveSystem.loadedRun) WorldEngineArbiter.ApplyForFreshRun();
            ModLog.Info($"世界生成开始：loadedRun={SaveSystem.loadedRun} engine={WorldEngineArbiter.Current} effective={WorldEngineArbiter.ResolveEffectiveEngineName()} selfActive={SeededWorldEngine.IsActive} seed={SeededWorldEngine.CurrentSeed} layerSeed={SeededWorldEngine.LayerSeed()}");
        }

        private static void Phase_Prefix(MethodBase __originalMethod)
        {
            if (!On) return;
            string name = __originalMethod.Name;
            // 在世界生成 / 地形阶段开头复位噪声与实体计数器。
            if (name == "GenerateWorld") { _noiseGenStep = 0; _distributeEntitiesCallIndex = 0; }
            else if (name == "WorldGenerateTerrain") { _noiseGenStep = 0; }
            UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + StageOffset(__originalMethod));
        }

        private static void FastNoiseCtor_Prefix(ref int seed)
        {
            if (On && WorldGeneration.world != null && WorldGeneration.world.generatingWorld)
            {
                seed = SeededWorldEngine.LayerSeed() + _noiseGenStep * 8121;
                _noiseGenStep++;
            }
        }

        private static void GenerateTree_Prefix(Vector2Int pos)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5500 + pos.x * 31 + pos.y * 17);
        }

        private static void GenerateBigMushroom_Prefix(Vector2Int pos)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5600 + pos.x * 31 + pos.y * 17);
        }

        private static void DistributeEntities_Prefix(GameObject basObj, float minPerChunk, float maxPerChunk)
        {
            if (On && basObj != null)
                UnityEngine.Random.InitState(ComposeDistributeEntitySeed(basObj, minPerChunk, maxPerChunk, _distributeEntitiesCallIndex++));
        }

        private static void PlaceLiquids_Prefix(byte type, int maxFill)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 9000 + type * 100 + maxFill);
        }

        private static void GenerateLifePods_Prefix(float amt)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5100 + (int)(amt * 1000f));
        }

        private static void GenerateDropCapsules_Prefix(float amt)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5200 + (int)(amt * 1000f));
        }

        private static void GenerateCollapsedPods_Prefix(float amt)
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 5300 + (int)(amt * 1000f));
        }

        private static void ApplyLayerModifiers_Prefix()
        {
            if (On) UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + 10000);
        }

        private static void ContinueRun_Prefix()
        {
            if (On)
            {
                _distributeEntitiesCallIndex = 0;
                UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed());
            }
        }

        private static void ResetCounters_Prefix()
        {
            if (On) { _noiseGenStep = 0; _distributeEntitiesCallIndex = 0; }
        }

        private static void Clear_Postfix()
        {
            if (On) { _noiseGenStep = 0; _distributeEntitiesCallIndex = 0; }
        }

        private static void Trader_GenerateInv_Prefix(TraderScript __instance, ref UnityEngine.Random.State __state)
        {
            if (On)
            {
                __state = UnityEngine.Random.state;
                Vector3 p = __instance.transform.position;
                UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() + __instance.character * 1000 + (int)(p.x * 10f) + (int)(p.y * 10f));
            }
        }

        private static void Trader_GenerateInv_Postfix(UnityEngine.Random.State __state)
        {
            if (On) UnityEngine.Random.state = __state;
        }

        private static void Openable_OnUse_Prefix(Openable __instance, ref UnityEngine.Random.State __state)
        {
            if (On)
            {
                __state = UnityEngine.Random.state;
                Vector3 p = __instance.transform.position;
                int num = (int)(p.x * 100f) ^ (int)(p.y * 100f);
                UnityEngine.Random.InitState(SeededWorldEngine.LayerSeed() ^ num);
            }
        }

        private static void Openable_OnUse_Postfix(UnityEngine.Random.State __state)
        {
            if (On) UnityEngine.Random.state = __state;
        }
    }
}
