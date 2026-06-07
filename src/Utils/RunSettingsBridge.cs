using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 游戏难度（RunSettings）反射桥接。难度存于 save.sv 的 runSettings 字段：
    /// 单机由游戏 TryLoadGame 自动恢复；多人由 KrokMP 接管加载且不恢复，故回档后从本地 save.sv 解析并写回 WorldGeneration.runSettings。
    /// 类型转换复用游戏 SaveSystem.TupleListToDic（按 RunSetting 类型分别 float/bool/int 解析）。
    /// </summary>
    internal static class RunSettingsBridge
    {
        private static MethodInfo _tupleListToDic;
        private static FieldInfo _runSettingsField;
        private static bool _resolved;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var wg = AccessTools.TypeByName("WorldGeneration");
                var ss = AccessTools.TypeByName("SaveSystem");
                if (wg != null) _runSettingsField = AccessTools.Field(wg, "runSettings");
                if (ss != null) _tupleListToDic = AccessTools.Method(ss, "TupleListToDic");
            }
            catch (Exception ex) { ModLog.Warning($"RunSettingsBridge.Resolve 失败：{ex.Message}"); }
        }

        /// <summary>从 save.sv 解析 runSettings 并写回 WorldGeneration.runSettings；游戏无此系统（旧版）时静默跳过。</summary>
        internal static void RestoreFromSaveFile(string savePath)
        {
            Resolve();
            if (_runSettingsField == null || _tupleListToDic == null) return;
            try
            {
                if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath)) return;
                string json = SaveSystem.Unzip(File.ReadAllBytes(savePath));
                var list = Parse(json);
                if (list.Count == 0) return;
                object dict = _tupleListToDic.Invoke(null, new object[] { list });
                _runSettingsField.SetValue(null, dict);
                ModLog.Info($"多人回档：已恢复 {list.Count} 项难度 runSettings");
            }
            catch (Exception ex) { ModLog.Warning($"RunSettingsBridge.RestoreFromSaveFile 失败：{ex.Message}"); }
        }

        private static List<(string, string)> Parse(string json)
        {
            var list = new List<(string, string)>();
            if (string.IsNullOrEmpty(json)) return list;
            var arr = Regex.Match(json, "\"runSettings\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return list;
            foreach (Match m in Regex.Matches(arr.Groups[1].Value,
                "\\{\\s*\"Item1\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"Item2\"\\s*:\\s*([^}]+?)\\s*\\}"))
            {
                list.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
            }
            return list;
        }
    }
}
