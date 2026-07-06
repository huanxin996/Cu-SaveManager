namespace CasualtiesUnknown.SaveManager
{
    /// <summary>固定世界生成引擎：QoL / KrokMP / 本 mod。</summary>
    internal enum WorldEngine
    {
        Qol,
        Krok,
        Self,
    }

    /// <summary>
    /// 决定当前 run 用哪套固定世界引擎并落实。
    /// PreferredEngine 配置值：qol | krok | self；按 mod 是否在场回落。
    /// </summary>
    internal static class WorldEngineArbiter
    {
        internal static WorldEngine Current { get; private set; } = WorldEngine.Self;

        internal static string PreferredEngine { get; private set; } = "qol";
        internal static string ManualSeedInput { get; private set; } = "";
        internal static string PositionMode { get; private set; } = "lastPos";
        internal static float FixedX { get; private set; }
        internal static float FixedY { get; private set; }

        internal static void SyncPreference(string preferredEngine, string manualSeed,
            string positionMode = null, float fixedX = 0f, float fixedY = 0f)
        {
            PreferredEngine = NormalizeEngineName(preferredEngine);
            ManualSeedInput = manualSeed ?? "";
            if (positionMode != null)
            {
                PositionMode = positionMode;
                FixedX = fixedX;
                FixedY = fixedY;
            }
        }

        internal static string NormalizeEngineName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "self";
            name = name.Trim().ToLowerInvariant();
            return name == "qol" || name == "krok" ? name : "self";
        }

        /// <summary>按配置 + mod 在场情况解析实际引擎名。</summary>
        internal static string ResolveEffectiveEngineName(string preferred = null)
        {
            string pref = NormalizeEngineName(preferred ?? PreferredEngine);
            bool mpEnabled = MultiplayerBridge.IsMultiplayerEnabled();
            // 「本 mod 固定世界」是显式 opt-in：只有用户主动选 self 才启用确定化引擎。
            if (pref == "self") return "self";
            if (pref == "krok" && mpEnabled) return "krok";
            if (pref == "qol" && QolBridge.IsQolPresent()) return "qol";
            if (mpEnabled) return "krok";
            return "self";
        }

        internal static WorldEngine ResolveEffectiveEngine(string preferred = null)
        {
            string name = ResolveEffectiveEngineName(preferred);
            if (name == "krok") return WorldEngine.Krok;
            if (name == "qol") return WorldEngine.Qol;
            return WorldEngine.Self;
        }

        /// <summary>新开局（非读档）按偏好启用引擎。</summary>
        internal static void ApplyForFreshRun()
        {
            var engine = ResolveEffectiveEngine();
            if (engine == WorldEngine.Krok)
            {
                SeededWorldEngine.Deactivate();
                QolBridge.DisableQolIntervention();
                Current = WorldEngine.Krok;
                ModLog.Info("固定世界引擎=KrokMP（原生世界生成）");
                return;
            }
            if (engine == WorldEngine.Self)
            {
                QolBridge.DisableQolIntervention();
                if (!string.IsNullOrWhiteSpace(ManualSeedInput)) SeededWorldEngine.SetManualSeed(ManualSeedInput);
                else SeededWorldEngine.EnsureFreshSeed();
                Current = WorldEngine.Self;
                return;
            }
            SeededWorldEngine.Deactivate();
            QolBridge.EnsureQolSeeded();
            Current = WorldEngine.Qol;
        }

        /// <summary>多人回档前按 sidecar 选定引擎准备：self/qol 引擎把 sidecar 记录的种子钉进 QoL，
        /// 使世界按记录种子复现；krok 引擎联机时由主机广播状态，无需钉入。</summary>
        internal static void PrepareMpRollback(SlotSidecar sidecar)
        {
            string engine = ResolveSidecarEngine(sidecar);
            int seed = sidecar?.QolSeed ?? 0;
            string input = sidecar?.QolSeedInput ?? "";
            ModLog.Info($"多人回档引擎={engine} seed={seed} (sidecar.worldEngine='{sidecar?.WorldEngine}' mpWorldEngine='{sidecar?.MpWorldEngine}')");
            if (engine == "krok")
            {
                SeededWorldEngine.Deactivate();
                QolBridge.DisableQolIntervention();
                Current = WorldEngine.Krok;
                return;
            }
            if (engine == "qol")
            {
                // QoL 引擎：不激活 self，直接把 sidecar 种子钉回 QoL 让它用记录种子复现世界。
                SeededWorldEngine.Deactivate();
                Current = WorldEngine.Qol;
                QolBridge.PrepareRollback(sidecar);
                return;
            }
            // self 引擎：Apply 内会先清零 QoL 种子，须在其后再 PrepareRollback 钉入，顺序不可调换。
            Apply(WorldEngine.Self, seed, input);
            QolBridge.PrepareRollback(sidecar);
        }

        private static string ResolveSidecarEngine(SlotSidecar sidecar)
        {
            if (!string.IsNullOrEmpty(sidecar?.WorldEngine))
                return ResolveEffectiveEngineName(sidecar.WorldEngine);
            if (!string.IsNullOrEmpty(sidecar?.MpWorldEngine))
                return ResolveEffectiveEngineName(sidecar.MpWorldEngine);
            return ResolveEffectiveEngineName();
        }

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
