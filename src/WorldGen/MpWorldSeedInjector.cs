using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人固定世界注入：KrokMP 主机在 LastBeforeGenerationState 构造时捕获 Random.state 并广播给客户端。
    /// 本注入器在该构造前（仅主机 + self 引擎启用时）用我方种子 Random.InitState，
    /// 使全员用同一确定状态生成世界。软依赖：KrokMP 不在则跳过。
    /// </summary>
    internal static class MpWorldSeedInjector
    {
        private static bool _patched;

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                Type t = AccessTools.TypeByName("KrokoshaCasualtiesMP.LastBeforeGenerationState");
                if (t == null)
                {
                    return;
                }
                ConstructorInfo ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    ModLog.Warning("LastBeforeGenerationState 无参构造未找到，注入跳过");
                    _patched = true;
                    return;
                }
                var harmony = new Harmony("com.casualtiesUnknown.saveManager.mpWorldSeed");
                var prefix = new HarmonyMethod(typeof(MpWorldSeedInjector).GetMethod(
                    nameof(CtorPrefix), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(ctor, prefix);
                _patched = true;
                ModLog.Info("多人固定世界注入已挂载（LastBeforeGenerationState ctor）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpWorldSeedInjector.TryPatch 失败：{ex.Message}");
            }
        }

        private static void CtorPrefix()
        {
            if (WorldEngineArbiter.Current == WorldEngine.Krok)
            {
                ModLog.Info("多人世界种子注入跳过：引擎=KrokMP");
                return;
            }
            bool active = SeededWorldEngine.IsActive;
            bool server = MultiplayerBridge.IsServer();
            if (!active || !server)
            {
                ModLog.Info($"多人世界种子注入跳过：selfActive={active} isServer={server}");
                return;
            }
            // 取「最深进度」的 totalTraveled：
            //   - 初次读档时内存 totalTraveled 尚未填充（GenerateWorld 早于 TryLoadGame），须用磁盘 save.sv 值；
            //   - 进下一层（RegenerateWorld）时内存值已 IncreaseDepthByLayer 递增，而磁盘 save.sv 仍是上一层旧值。
            // 若只读磁盘旧值，进层会用上一层的种子重算 → 重新生成与所在层相同的地图，表现为“卡层”。
            // 两者取最大即可同时覆盖读档与进层：始终对应当前正在生成的这一层。
            int diskTraveled = MpSaveLayerHelper.ReadPersistedTotalTraveled();
            int memTraveled = 0;
            try { if (WorldGeneration.world != null) memTraveled = WorldGeneration.world.totalTraveled; }
            catch { }
            int traveled = Math.Max(diskTraveled, memTraveled);
            int seed = SeededWorldEngine.CurrentSeed + traveled * 265443576;
            UnityEngine.Random.InitState(seed);
            ModLog.Info($"多人世界种子注入：seed={seed} totalTraveled={traveled}（disk={diskTraveled} mem={memTraveled}）");
        }
    }
}
