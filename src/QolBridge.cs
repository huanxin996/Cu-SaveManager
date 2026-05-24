using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// QoL.Unknown mod 的反射桥接。让 SaveManager 在保存槽位时抓取 QoL 的 SeedManager 状态、
    /// 在回档时把存档里的玩家位置 + 种子推回 QoL，让它在下一次 TryLoadGame postfix 还原。
    /// QoL 不在场时所有方法静默返回 false / 默认值，绝不影响主流程。
    /// </summary>
    internal static class QolBridge
    {
        private static bool _resolved;

        // QoL_Unknown.SaveSystemPatcher.PendingLoadPosition (Vector3?)
        private static FieldInfo _fPendingLoadPosition;
        // QoL_Unknown.SeedManager
        private static FieldInfo _fSeedIsSeeded;     // bool
        private static FieldInfo _fSeedCurrentSeed;  // int
        private static FieldInfo _fSeedInputString;  // string

        private static void TryResolve(ManualLogSource log)
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type tPatcher = null, tSeed = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (!name.Contains("QoL")) continue;
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "SaveSystemPatcher") tPatcher = t;
                        else if (t.Name == "SeedManager") tSeed = t;
                    }
                    if (tPatcher != null && tSeed != null) break;
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
                bool present = tPatcher != null || tSeed != null;
                log?.LogInfo($"[SaveManager] QolBridge resolved: SaveSystemPatcher={(tPatcher != null ? "✓" : "✗")} SeedManager={(tSeed != null ? "✓" : "✗")}");
                if (!present) log?.LogInfo("[SaveManager] QoL.Unknown 未检测到，玩家位置 / 世界确定化将不生效");
            }
            catch (Exception ex)
            {
                log?.LogWarning($"QolBridge 反射失败：{ex.Message}");
            }
        }

        /// <summary>读 QoL.SeedManager 当前状态写到 sidecar；CurrentSeed 为 0（QoL 不在或新 run 还没初始化 seed）
        /// 时 fallback 用当前 save.sv 的 FNV-1a hash，与 QoL.SeedManager.GetDeterministicSeedFromSaveFile 等价。
        /// 这样 sidecar 永远有非零 seed 可在回档时写回，QoL 不会再走 EnsureNonZeroSeedForLoadedRun 的 fallback 计算
        /// 路径，确保回档用的就是保存时刻那一份 seed。</summary>
        internal static void ReadCurrentSeed(out int seed, out string input, ManualLogSource log)
        {
            TryResolve(log);
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
                seed = ComputeFnv1a(SaveStore.GameSavePath);
            }
        }

        /// <summary>FNV-1a 32 位 hash 当前 save.sv 字节流，与 QoL SeedManager.GetDeterministicSeedFromSaveFile 完全一致。</summary>
        private static int ComputeFnv1a(string fullPath)
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

        /// <summary>把回档目标的玩家位置 + 种子推给 QoL。回档完成后 QoL 的 TryLoadGame postfix 自动塞回 body.transform.position。</summary>
        internal static void PrepareRollback(SlotSidecar sidecar, ManualLogSource log)
        {
            if (sidecar == null) return;
            TryResolve(log);
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
                log?.LogWarning($"QolBridge.PrepareRollback 失败：{ex.Message}");
            }
        }
    }
}
