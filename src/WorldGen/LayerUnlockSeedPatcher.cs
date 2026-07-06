using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 让 LayerUnlock 解锁层的协程生成内容随种子复现：对其 4 个协程状态机的 MoveNext 挂 prefix/postfix，
    /// 给每个协程实例一条私有确定 RNG 流（首次进入按 种子+层深+协程盐 InitState，其后跨 yield 保存/还原），
    /// 并在每步前后隔离/还原全局 Random.state。LayerUnlock 不在场或世界未确定化时不介入。
    /// </summary>
    internal static class LayerUnlockSeedPatcher
    {
        private static bool _patched;

        // 每个协程实例的私有 RNG 状态。
        private sealed class CoState
        {
            internal bool Active;   // 该实例是否启用确定化（首个 MoveNext 时一次性判定并锁定）
            internal bool Seeded;   // 是否已用层种子 InitState
            internal int Seed;      // 本协程确定种子
            internal UnityEngine.Random.State Saved; // 本协程上次推进后的 RNG 状态
        }

        private static readonly ConditionalWeakTable<object, CoState> _table =
            new ConditionalWeakTable<object, CoState>();

        // 协程类型名 -> 盐（避免不同协程用同一序列；与 SeededWorldPatcher 的阶段偏移区间错开）。
        private static int SaltFor(Type declaringType)
        {
            string n = declaringType != null ? declaringType.Name : "";
            if (n.IndexOf("GenerateStructuresAfterTerrain", StringComparison.Ordinal) >= 0) return 71000;
            if (n.IndexOf("AddStructuresForDepth", StringComparison.Ordinal) >= 0) return 72000;
            if (n.IndexOf("SpawnIonHeaters", StringComparison.Ordinal) >= 0) return 73000;
            if (n.IndexOf("SpawnSpaceHeaters", StringComparison.Ordinal) >= 0) return 74000;
            return 70000;
        }

        // LayerUnlock 协程状态机的嵌套类型全名。
        private static readonly string[] StateMachineTypes =
        {
            "RemiyamuremodLayerUnlock.TerrainPatch+<GenerateStructuresAfterTerrain>d__2",
            "RemiyamuremodLayerUnlock.StructuresPatch+<AddStructuresForDepth>d__1",
            "RemiyamuremodLayerUnlock.EntitiesPatch+<SpawnIonHeaters>d__3",
            "RemiyamuremodLayerUnlock.EntitiesPatch+<SpawnSpaceHeaters>d__4",
        };

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                Type probe = AccessTools.TypeByName("RemiyamuremodLayerUnlock.TerrainPatch");
                if (probe == null) return; // LayerUnlock 不在场，跳过。
                _patched = true;

                var harmony = new Harmony("com.casualtiesUnknown.saveManager.layerUnlockSeed");
                var self = typeof(LayerUnlockSeedPatcher);
                var pre = new HarmonyMethod(self.GetMethod(nameof(MoveNext_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                var post = new HarmonyMethod(self.GetMethod(nameof(MoveNext_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

                int count = 0;
                foreach (string typeName in StateMachineTypes)
                {
                    Type sm = ResolveType(typeName);
                    if (sm == null) { ModLog.Warning($"LayerUnlock 状态机未找到：{typeName}"); continue; }
                    MethodInfo mn = FindMoveNext(sm);
                    if (mn == null) { ModLog.Warning($"LayerUnlock MoveNext 未找到：{typeName}"); continue; }
                    harmony.Patch(mn, pre, post);
                    count++;
                }
                ModLog.Info($"LayerUnlock 确定化已挂载（{count}/{StateMachineTypes.Length} 个协程，仅确定化世界下生效）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"LayerUnlockSeedPatcher.TryPatch 失败：{ex.Message}");
            }
        }

        private static Type ResolveType(string fullNestedName)
        {
            var t = AccessTools.TypeByName(fullNestedName);
            if (t != null) return t;
            // 退路：在 LayerUnlock 程序集里按嵌套结构解析。
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.IndexOf("LayerUnlock", StringComparison.OrdinalIgnoreCase) < 0) continue;
                t = asm.GetType(fullNestedName);
                if (t != null) return t;
            }
            return null;
        }

        private static MethodInfo FindMoveNext(Type smType)
        {
            foreach (var m in smType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.ReturnType != typeof(bool) || m.GetParameters().Length != 0) continue;
                if (m.Name == "MoveNext" || m.Name.EndsWith(".MoveNext", StringComparison.Ordinal)) return m;
            }
            return null;
        }

        private static void MoveNext_Prefix(object __instance, MethodBase __originalMethod,
            out UnityEngine.Random.State __state)
        {
            __state = default;
            CoState co = _table.GetValue(__instance, _ => CreateState(__instance, __originalMethod));
            if (!co.Active) return;
            __state = UnityEngine.Random.state; // 存游戏全局 RNG 流
            if (!co.Seeded)
            {
                co.Seeded = true;
                UnityEngine.Random.InitState(co.Seed);
            }
            else
            {
                UnityEngine.Random.state = co.Saved;
            }
        }

        private static void MoveNext_Postfix(object __instance, UnityEngine.Random.State __state)
        {
            if (!_table.TryGetValue(__instance, out CoState co) || !co.Active) return;
            co.Saved = UnityEngine.Random.state; // 存本协程推进后的 RNG 流
            UnityEngine.Random.state = __state;  // 还原游戏全局 RNG 流，不污染别处
        }

        private static CoState CreateState(object instance, MethodBase originalMethod)
        {
            var co = new CoState { Active = DeterminismActive() };
            if (!co.Active) return co;
            Type declaring = originalMethod != null ? originalMethod.DeclaringType : instance.GetType();
            WorldGeneration wg = ReadWg(instance);
            int depth = ReadDepth(instance, wg);
            co.Seed = ComposeSeed(wg, depth, SaltFor(declaring));
            return co;
        }

        // 是否处于确定化世界：self 引擎激活，或 QoL 在场且已有非 0 种子。否则不介入。
        private static bool DeterminismActive()
        {
            try
            {
                if (SeededWorldEngine.IsActive) return true;
                if (QolBridge.IsQolPresent())
                {
                    QolBridge.ReadCurrentSeed(out int s, out _);
                    return s != 0;
                }
            }
            catch { }
            return false;
        }

        private static int ComposeSeed(WorldGeneration wg, int depth, int salt)
        {
            int baseSeed = ResolveBaseSeed();
            int traveled = 0;
            try { if (wg != null) traveled = wg.totalTraveled; } catch { }
            return baseSeed + traveled * 265443576 + depth * 7919 + salt;
        }

        private static int ResolveBaseSeed()
        {
            if (SeededWorldEngine.IsActive && SeededWorldEngine.CurrentSeed != 0)
                return SeededWorldEngine.CurrentSeed;
            QolBridge.ReadCurrentSeed(out int s, out _);
            if (s != 0) return s;
            return SeededWorldEngine.CurrentSeed;
        }

        private static WorldGeneration ReadWg(object instance)
        {
            try
            {
                FieldInfo f = instance.GetType().GetField("wg",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return f != null ? f.GetValue(instance) as WorldGeneration : null;
            }
            catch { return null; }
        }

        private static int ReadDepth(object instance, WorldGeneration wg)
        {
            try
            {
                FieldInfo f = instance.GetType().GetField("depth",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return (int)f.GetValue(instance);
            }
            catch { }
            try { if (wg != null) return wg.biomeDepth; } catch { }
            return 0;
        }
    }
}
