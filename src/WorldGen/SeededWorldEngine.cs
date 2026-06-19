using System;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// saveManager 自带的确定化世界生成种子源。IsActive 为真时 SeededWorldPatcher 会在
    /// WorldGeneration 各阶段 Prefix 用 LayerSeed 重置 UnityEngine.Random，使地形与实体可复现。
    /// 种子优先级：手动输入 > 存档字节 FNV-1a 派生。
    /// </summary>
    internal static class SeededWorldEngine
    {
        internal static bool IsActive { get; private set; }
        internal static int CurrentSeed { get; private set; }
        internal static string InputString { get; private set; } = "";

        /// <summary>多人下是否启用逐阶段种子重置：主机/客户端确认拿到固定世界种子后置真，
        /// 使多人世界与同种子单机一致。仅多人路径用；单机由 IsActive 决定。</summary>
        internal static bool MpReseedEnabled { get; set; }

        /// <summary>用手动输入设种子。空串关闭确定化；纯数字直接当种子，否则取字符串稳定 hash。</summary>
        internal static void SetManualSeed(string input)
        {
            InputString = input ?? "";
            if (string.IsNullOrWhiteSpace(input))
            {
                IsActive = false;
                CurrentSeed = 0;
                return;
            }
            IsActive = true;
            CurrentSeed = int.TryParse(input, out int v) ? v : StableHash(input);
        }

        /// <summary>用指定种子与输入串直接激活（回档/读档时按 sidecar 还原用）。</summary>
        internal static void Activate(int seed, string input)
        {
            CurrentSeed = seed != 0 ? seed : 1;
            InputString = input ?? "";
            IsActive = true;
        }

        /// <summary>新开局时生成一个一次性随机种子并激活。</summary>
        internal static void EnsureFreshSeed()
        {
            int seed = Guid.NewGuid().GetHashCode();
            if (seed == 0) seed = 1;
            CurrentSeed = seed;
            InputString = seed.ToString();
            IsActive = true;
        }

        internal static void Deactivate()
        {
            IsActive = false;
            CurrentSeed = 0;
            InputString = "";
            MpReseedEnabled = false;
        }

        /// <summary>当前层的种子：基础种子叠加 totalTraveled 偏移，保证逐层不同但可复现。
        /// totalTraveled 取「当前层」值：进层时内存已递增用内存值；初次读档内存尚未填充（GenerateWorld 早于
        /// TryLoadGame）则回落磁盘 save.sv 值。与 MpWorldSeedInjector 取值口径一致，避免广播种子与逐阶段重置错配。</summary>
        internal static int LayerSeed()
        {
            int traveled = 0;
            try
            {
                if (WorldGeneration.world != null) traveled = WorldGeneration.world.totalTraveled;
            }
            catch { }
            if (traveled <= 0)
            {
                try { traveled = MpSaveLayerHelper.ReadPersistedTotalTraveled(); }
                catch { }
            }
            return CurrentSeed + traveled * 265443576;
        }

        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h == 0 ? 1 : h;
        }
    }
}
