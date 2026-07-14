using System;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// QoL 的出生清陷阱（SeededRunPatcher.RemoveNearbyTrapsLikeVanilla）按名字匹配陷阱表清场，
    /// 不区分世界陷阱与玩家随身物品，导致背包/容器里的抗辐射药等每次进层被删。
    /// 用本模组"仅清世界物体"的版本整体替换其实现。软依赖：QoL 不在则跳过。
    /// </summary>
    internal static class QolTrapClearGuard
    {
        private static bool _patched;

        internal static void TryPatch()
        {
            if (_patched) return;
            try
            {
                Type t = AccessTools.TypeByName("QoL_Unknown.SeededRunPatcher");
                if (t == null) return;
                MethodInfo m = t.GetMethod("RemoveNearbyTrapsLikeVanilla",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (m == null)
                {
                    ModLog.Warning("QoL RemoveNearbyTrapsLikeVanilla 未找到，随身陷阱类物品保护未挂载");
                    return;
                }
                var harmony = new Harmony("com.casualtiesUnknown.saveManager.qolTrapClearGuard");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(QolTrapClearGuard).GetMethod(
                    nameof(SafeClearInstead), BindingFlags.Static | BindingFlags.NonPublic)));
                _patched = true;
                ModLog.Info("QoL 出生清陷阱已替换为仅清世界物体版本（保护随身抗辐射药等物品）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"QolTrapClearGuard.TryPatch 失败：{ex.Message}");
            }
        }

        private static bool SafeClearInstead()
        {
            SelfSpawnPatcher.RemoveNearbyTrapsLikeVanilla();
            return false;
        }
    }
}
