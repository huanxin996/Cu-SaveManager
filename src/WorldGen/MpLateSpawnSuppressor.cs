using System;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人回档窗口期内抑制 KrokMP LateSpawnLocation，避免异步协程覆盖 MpPositionRestorer 写回的位置。
    /// </summary>
    internal static class MpLateSpawnSuppressor
    {
        private static bool _patched;

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                var asm = MultiplayerBridge.GetKrokAssembly();
                if (asm == null) return;
                var t = asm.GetType("KrokoshaCasualtiesMP.ServerMain");
                if (t == null) return;
                var m = AccessTools.Method(t, "LateSpawnLocation");
                if (m == null) return;
                var harmony = new Harmony("com.casualtiesUnknown.saveManager.mpLateSpawnSuppressor");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(MpLateSpawnSuppressor), nameof(SkipWhenRestoring)));
                _patched = true;
                ModLog.Info("多人 LateSpawnLocation 抑制已挂载");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"MpLateSpawnSuppressor.TryPatch 失败：{ex.Message}");
            }
        }

        private static bool SkipWhenRestoring()
            => !MpPositionRestorer.ActiveSession;
    }
}
