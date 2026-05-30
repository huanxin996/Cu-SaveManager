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
            bool active = SeededWorldEngine.IsActive;
            bool server = MultiplayerBridge.IsServer();
            if (!active || !server)
            {
                ModLog.Info($"多人世界种子注入跳过：selfActive={active} isServer={server}");
                return;
            }
            int seed = SeededWorldEngine.LayerSeed();
            UnityEngine.Random.InitState(seed);
            ModLog.Info($"多人世界种子注入：seed={seed}");
        }
    }
}
