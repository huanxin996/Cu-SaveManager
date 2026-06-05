using System;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 在 KrokMP 的 Harmony Prefix 方法上再挂一层 Prefix 并 return false，
    /// 从而跳过 Krok 原始 Prefix 体。比 Unpatch(owner) 更可靠（Harmony 同方法多 Prefix 仍会全部执行）。
    /// </summary>
    internal static class KrokMpHarmonySilencer
    {
        private static readonly string[] RunPatchTypes =
        {
            "PreRunScript_LoadRun_MultiplayerPatch",
            "PreRunScript_StartRun_MultiplayerPatch",
        };

        internal static void TrySilence(Harmony harmony)
        {
            var asm = MultiplayerBridge.GetKrokAssembly();
            if (asm == null) return;
            foreach (var typeName in RunPatchTypes)
            {
                try { SilenceOne(harmony, asm, typeName); }
                catch (Exception ex) { ModLog.Warning($"静音 {typeName} 失败：{ex.Message}"); }
            }
        }

        private static void SilenceOne(Harmony harmony, Assembly asm, string typeName)
        {
            var t = asm.GetType($"KrokoshaCasualtiesMP.{typeName}");
            if (t == null) return;
            var prefix = AccessTools.Method(t, "Prefix");
            if (prefix == null) return;

            var info = Harmony.GetPatchInfo(prefix);
            bool already = false;
            if (info?.Prefixes != null)
            {
                foreach (var p in info.Prefixes)
                {
                    if (p.owner == harmony.Id && p.PatchMethod?.Name == nameof(BlockKrokPrefix))
                        already = true;
                }
            }
            if (already) return;

            harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(KrokMpHarmonySilencer), nameof(BlockKrokPrefix)));
            ModLog.Info($"KrokMpHarmonySilencer：已静音 {typeName}.Prefix");
        }

        private static bool BlockKrokPrefix() => false;
    }
}
