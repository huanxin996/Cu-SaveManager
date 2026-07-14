using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 标记 SaveSystem.TryLoadGame 的执行窗口，供读档期兜底补丁判断时机。
    /// </summary>
    [HarmonyPatch(typeof(SaveSystem), "TryLoadGame")]
    internal static class SaveLoadWindowPatch
    {
        internal static bool Active { get; private set; }

        private static void Prefix()
        {
            Active = true;
            CustomContainerLoadFixPatch.ResetApplied();
        }

        private static void Finalizer()
        {
            Active = false;
            CustomContainerLoadFixPatch.ResetApplied();
        }
    }

    /// <summary>
    /// 读档兜底：CUCoreLib 的自定义容器容量（ContainerProperties，如超级银河皮带的 maxWeight=9999）
    /// 要到 Item.Start 的 postfix 才应用，而 TryLoadGame 在同一帧就用 Container.LoadItem 把内容物塞回容器，
    /// 此时容器还是模板预制体（smallpack）的默认小容量，CanHoldItem 失败导致内容物静默掉落/丢失。
    /// 在读档窗口内、LoadItem 之前先通过 CUCoreLib 把自定义容器属性应用到位。软依赖：CUCoreLib 不在则跳过。
    /// </summary>
    [HarmonyPatch(typeof(Container), "LoadItem")]
    internal static class CustomContainerLoadFixPatch
    {
        private static readonly HashSet<int> _applied = new HashSet<int>();
        private static MethodInfo _applyCustomItemRuntime;
        private static bool _resolveTried;

        internal static void ResetApplied() => _applied.Clear();

        private static void Prefix(Container __instance)
        {
            if (!SaveLoadWindowPatch.Active || __instance == null) return;
            try
            {
                if (!_applied.Add(__instance.GetInstanceID())) return;
                var item = __instance.GetComponent<Item>();
                if (item == null || string.IsNullOrEmpty(item.id)) return;
                MethodInfo apply = ResolveApplyMethod();
                if (apply == null) return;
                apply.Invoke(null, new object[] { item, false });
            }
            catch (Exception ex)
            {
                ModLog.Warning($"读档自定义容器容量兜底失败：{ex.Message}");
            }
        }

        private static MethodInfo ResolveApplyMethod()
        {
            if (_resolveTried) return _applyCustomItemRuntime;
            _resolveTried = true;
            Type type = AccessTools.TypeByName("CUCoreLib.Patches.ItemRegistryPatches");
            if (type != null)
                _applyCustomItemRuntime = AccessTools.Method(type, "ApplyCustomItemRuntime");
            if (_applyCustomItemRuntime == null)
                ModLog.Info("CUCoreLib ApplyCustomItemRuntime 不可用，读档自定义容器容量兜底未启用");
            return _applyCustomItemRuntime;
        }
    }
}
