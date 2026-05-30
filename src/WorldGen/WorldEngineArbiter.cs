namespace CasualtiesUnknown.SaveManager
{
    /// <summary>固定世界生成引擎：交给 QoL，或用 saveManager 自身实现。</summary>
    internal enum WorldEngine
    {
        Qol,
        Self,
    }

    /// <summary>
    /// 决定当前 run 用哪套固定世界引擎并落实。
    /// 选 Self 时暂时禁用 QoL 的世界 / 存档介入；选 Qol 时停掉自身引擎把控制权交给 QoL。
    /// QoL 不在场时强制回落 Self。
    /// </summary>
    internal static class WorldEngineArbiter
    {
        internal static WorldEngine Current { get; private set; } = WorldEngine.Self;

        internal static bool PreferQol { get; private set; } = true;
        internal static string ManualSeedInput { get; private set; } = "";
        internal static string PositionMode { get; private set; } = "lastPos";
        internal static float FixedX { get; private set; }
        internal static float FixedY { get; private set; }

        /// <summary>同步面板/配置里的引擎偏好、手动种子与位置模式，供新开局 patch 与 sidecar 写入读取。</summary>
        internal static void SyncPreference(string preferredEngine, string manualSeed,
            string positionMode = null, float fixedX = 0f, float fixedY = 0f)
        {
            PreferQol = !string.Equals(preferredEngine, "self", System.StringComparison.OrdinalIgnoreCase);
            ManualSeedInput = manualSeed ?? "";
            if (positionMode != null)
            {
                PositionMode = positionMode;
                FixedX = fixedX;
                FixedY = fixedY;
            }
        }

        /// <summary>新开局（非读档）按偏好启用引擎：self 引擎无条件确定化（手动种子优先，否则自生成）。</summary>
        internal static void ApplyForFreshRun()
        {
            var engine = Resolve(PreferQol);
            if (engine == WorldEngine.Self)
            {
                QolBridge.DisableQolIntervention();
                if (!string.IsNullOrWhiteSpace(ManualSeedInput)) SeededWorldEngine.SetManualSeed(ManualSeedInput);
                else SeededWorldEngine.EnsureFreshSeed();
                Current = WorldEngine.Self;
            }
            else
            {
                SeededWorldEngine.Deactivate();
                QolBridge.EnsureQolSeeded();
                Current = engine;
            }
        }

        /// <summary>按用户偏好仲裁出实际引擎：preferQol 且 QoL 在场才用 Qol，否则用 Self。</summary>
        internal static WorldEngine Resolve(bool preferQol)
        {
            bool qol = QolBridge.IsQolPresent();
            return (preferQol && qol) ? WorldEngine.Qol : WorldEngine.Self;
        }

        /// <summary>启用指定引擎：Self 用给定种子接手并暂时禁用 QoL；Qol 停自身引擎交给 QoL。</summary>
        internal static void Apply(WorldEngine engine, int seed, string seedInput)
        {
            Current = engine;
            if (engine == WorldEngine.Self)
            {
                QolBridge.DisableQolIntervention();
                SeededWorldEngine.Activate(seed, seedInput);
            }
            else
            {
                SeededWorldEngine.Deactivate();
            }
        }
    }
}
