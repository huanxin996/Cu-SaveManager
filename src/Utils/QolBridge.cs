using System;
using System.Reflection;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// QoL.Unknown mod 的反射桥接。保存槽位时抓取 QoL 的 SeedManager 状态，
    /// 回档时把存档里的玩家位置 + 种子推回 QoL，让它在下一次 TryLoadGame postfix 还原。
    /// QoL 不在场时所有方法静默返回默认值，绝不影响主流程。
    /// </summary>
    internal static class QolBridge
    {
        private static bool _resolved;

        private static FieldInfo _fPendingLoadPosition; // QoL_Unknown.SaveSystemPatcher.PendingLoadPosition (Vector3?)
        private static FieldInfo _fSeedIsSeeded;         // QoL_Unknown.SeedManager.IsSeeded (bool)
        private static FieldInfo _fSeedCurrentSeed;      // QoL_Unknown.SeedManager.CurrentSeed (int)
        private static FieldInfo _fSeedInputString;      // QoL_Unknown.SeedManager.InputString (string)
        private static FieldInfo _fAutosaveEnabled;      // QoL_Unknown.SaveManager.AutosaveEnabled (bool)
        private static bool _qolPresent;

        /// <summary>是否检测到 QoL.Unknown（其世界确定化 / 自动存档能力可用）。</summary>
        internal static bool IsQolPresent()
        {
            TryResolve();
            return _qolPresent;
        }

        /// <summary>暂时禁用 QoL 对世界生成与存档的介入：置 SeedManager.IsSeeded=false + SaveManager.AutosaveEnabled=false。</summary>
        internal static void DisableQolIntervention()
        {
            TryResolve();
            try
            {
                _fSeedIsSeeded?.SetValue(null, false);
                _fSeedCurrentSeed?.SetValue(null, 0);
                _fAutosaveEnabled?.SetValue(null, false);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolBridge.DisableQolIntervention 失败：{ex.Message}");
            }
        }

        /// <summary>QoL 模式新开局确保 QoL 有确定种子：未 seeded 时用自生成种子让 QoL 固定世界，并恢复其自动存档。
        /// 已 seeded（玩家在 QoL 输过种子）则不覆盖。让回档可凭 sidecar 种子复现世界。</summary>
        internal static void EnsureQolSeeded()
        {
            TryResolve();
            if (!_qolPresent)
            {
                ModLog.Warning("EnsureQolSeeded：QoL 未解析到，跳过设种子");
                return;
            }
            try
            {
                _fAutosaveEnabled?.SetValue(null, true);
                bool seeded = _fSeedIsSeeded != null && (bool)_fSeedIsSeeded.GetValue(null);
                int current = _fSeedCurrentSeed != null ? (int)_fSeedCurrentSeed.GetValue(null) : 0;
                if (seeded && current != 0)
                {
                    ModLog.Info($"QoL 模式新开局：QoL 已有种子 seed={current}，不覆盖");
                    return;
                }
                int seed = Guid.NewGuid().GetHashCode();
                if (seed == 0) seed = 1;
                _fSeedCurrentSeed?.SetValue(null, seed);
                _fSeedInputString?.SetValue(null, seed.ToString());
                _fSeedIsSeeded?.SetValue(null, true);
                ModLog.Info($"QoL 模式新开局已设确定种子 seed={seed}（原 seeded={seeded} current={current}）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolBridge.EnsureQolSeeded 失败：{ex.Message}");
            }
        }

        private static void TryResolve()
        {
            if (_resolved) return;
            try
            {
                Type tPatcher = null, tSeed = null, tSaveMgr = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name.IndexOf("qol", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    tPatcher = asm.GetType("QoL_Unknown.SaveSystemPatcher");
                    tSeed = asm.GetType("QoL_Unknown.SeedManager");
                    tSaveMgr = asm.GetType("QoL_Unknown.SaveManager");
                    if (tSeed != null) break;
                }
                if (tPatcher != null)
                {
                    _fPendingLoadPosition = tPatcher.GetField("PendingLoadPosition",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                }
                if (tSeed != null)
                {
                    _fSeedIsSeeded = tSeed.GetField("IsSeeded",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    _fSeedCurrentSeed = tSeed.GetField("CurrentSeed",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    _fSeedInputString = tSeed.GetField("InputString",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                }
                if (tSaveMgr != null)
                {
                    _fAutosaveEnabled = tSaveMgr.GetField("AutosaveEnabled",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                }
                _qolPresent = tPatcher != null || tSeed != null;
                if (_qolPresent) _resolved = true;
                ModLog.Info($"QolBridge resolved: SaveSystemPatcher={(tPatcher != null ? "✓" : "✗")} SeedManager={(tSeed != null ? "✓" : "✗")} SaveManager={(tSaveMgr != null ? "✓" : "✗")}");
                if (!_qolPresent) ModLog.Info("QoL.Unknown 未检测到，将使用 saveManager 自身固定世界引擎");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolBridge 反射失败：{ex.Message}");
            }
        }

        /// <summary>读 QoL.SeedManager 当前实时种子；为 0 时回落到本 mod sidecar 已记录的稳定种子。</summary>
        internal static void ReadCurrentSeed(out int seed, out string input)
        {
            TryResolve();
            seed = 0;
            input = "";
            try
            {
                if (_fSeedCurrentSeed != null) seed = (int)_fSeedCurrentSeed.GetValue(null);
                if (_fSeedInputString != null) input = (string)_fSeedInputString.GetValue(null) ?? "";
            }
            catch { }
            if (seed == 0)
            {
                var sc = SlotSidecar.LoadOrEmpty(SaveStore.GameSavePath);
                if (sc != null && sc.QolSeed != 0)
                {
                    seed = sc.QolSeed;
                    input = string.IsNullOrEmpty(input) ? (sc.QolSeedInput ?? "") : input;
                }
            }
        }

        /// <summary>FNV-1a 32 位 hash 当前 save.sv 字节流，与 QoL SeedManager.GetDeterministicSeedFromSaveFile 一致。</summary>
        internal static int ComputeDeterministicSeedFromSaveFile(string fullPath)
        {
            try
            {
                if (!System.IO.File.Exists(fullPath)) return 0;
                byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0) return 0;
                uint h = 2166136261u;
                for (int i = 0; i < bytes.Length; i++)
                {
                    h ^= bytes[i];
                    h *= 16777619u;
                }
                int v = (int)h;
                return v == 0 ? 1 : v;
            }
            catch { return 0; }
        }

        /// <summary>把种子钉进 QoL：CurrentSeed/InputString + IsSeeded=true。QoL 不在场或 seed=0 时静默跳过。</summary>
        internal static void PinSeed(int seed, string input)
        {
            TryResolve();
            if (!_qolPresent || seed == 0) return;
            try
            {
                _fSeedCurrentSeed?.SetValue(null, seed);
                _fSeedInputString?.SetValue(null, input ?? "");
                _fSeedIsSeeded?.SetValue(null, true);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolBridge.PinSeed 失败：{ex.Message}");
            }
        }

        /// <summary>把回档目标的玩家位置 + 种子推给 QoL。回档完成后 QoL 的 TryLoadGame postfix 自动塞回 body.transform.position。</summary>
        internal static void PrepareRollback(SlotSidecar sidecar)
        {
            if (sidecar == null) return;
            TryResolve();
            try
            {
                if (sidecar.HasPlayerPos && _fPendingLoadPosition != null)
                {
                    Vector3? pos = new Vector3(sidecar.PlayerX, sidecar.PlayerY, 0f);
                    _fPendingLoadPosition.SetValue(null, pos);
                }
                if (_fSeedIsSeeded != null && _fSeedCurrentSeed != null && _fSeedInputString != null)
                {
                    if (sidecar.QolSeed != 0 || !string.IsNullOrEmpty(sidecar.QolSeedInput))
                    {
                        _fSeedIsSeeded.SetValue(null, true);
                        _fSeedCurrentSeed.SetValue(null, sidecar.QolSeed);
                        _fSeedInputString.SetValue(null, sidecar.QolSeedInput ?? "");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolBridge.PrepareRollback 失败：{ex.Message}");
            }
        }
    }
}
