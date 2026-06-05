using System;
using System.IO;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 多人存档层数处理：KrokMP 按玩家子目录存 save.sv，且 GenerateWorld 在 TryLoadGame 之前捕获 totalTraveled。
    /// 须从磁盘 save.sv 读层信息（对齐单机 NormalizeSaveBiome 思路），并修正 mp_rules 避免进下一层 modifier。
    /// </summary>
    internal static class MpSaveLayerHelper
    {
        internal static int ReadTotalTraveledFromSv(string fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return 0;
                string json = SaveSystem.Unzip(File.ReadAllBytes(fullPath));
                return ParseIntField(json, "\"totalTraveled\":");
            }
            catch { return 0; }
        }

        /// <summary>回档/读档前从本地玩家 save.sv 取 totalTraveled，供 MpWorldSeedInjector 在 TryLoadGame 之前算 layerSeed。</summary>
        internal static int ReadPersistedTotalTraveled()
        {
            if (!SaveSystem.loadedRun) return 0;
            string path = MpSaveLocator.ResolveLocalPlayerSavePath();
            if (path == null) return 0;
            int v = ReadTotalTraveledFromSv(path);
            ModLog.Info($"多人层深：save.sv totalTraveled={v}（{path}）");
            return v;
        }

        /// <summary>SaveGame 后规范化 mp_save 内各玩家 save.sv 的 biome，并锁定 mp_rules 为当前层（非进层过渡）。</summary>
        internal static void NormalizeAfterSnapshot(int biomeDepth)
        {
            if (!MpSaveLocator.HasMpSave()) return;
            try
            {
                if (Directory.Exists(MpSaveLocator.MpSaveDir))
                {
                    foreach (var dir in Directory.GetDirectories(MpSaveLocator.MpSaveDir))
                    {
                        string sv = Path.Combine(dir, "save.sv");
                        if (File.Exists(sv)) NormalizeSaveBiomeField(sv, biomeDepth);
                    }
                }
                string rootSv = Path.Combine(MpSaveLocator.MpSaveDir, "save.sv");
                if (File.Exists(rootSv)) NormalizeSaveBiomeField(rootSv, biomeDepth);
                FixMpRulesForCurrentLayer();
            }
            catch (Exception ex)
            {
                ModLog.Warning($"NormalizeAfterSnapshot 失败：{ex.Message}");
            }
        }

        private static void FixMpRulesForCurrentLayer()
        {
            string rules = MpSaveLocator.MpRulesPath;
            if (!File.Exists(rules)) return;
            try
            {
                string json = File.ReadAllText(rules);
                json = ReplaceJsonBool(json, "LEVEL", true);
                json = ReplaceJsonInt(json, "MODIFIER", -1);
                File.WriteAllText(rules, json);
                ModLog.Info("mp_rules 已锁定为当前层（LEVEL=true MODIFIER=-1）");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"FixMpRulesForCurrentLayer 失败：{ex.Message}");
            }
        }

        internal static void NormalizeSaveBiomeField(string fullPath, int biomeDepth)
        {
            try
            {
                if (!File.Exists(fullPath)) return;
                byte[] bytes = File.ReadAllBytes(fullPath);
                string json = SaveSystem.Unzip(bytes);
                if (string.IsNullOrEmpty(json)) return;
                string rewritten = ReplaceJsonInt(json, "biome", biomeDepth);
                if (string.Equals(rewritten, json, StringComparison.Ordinal)) return;
                File.WriteAllBytes(fullPath, SaveSystem.Zip(rewritten));
            }
            catch (Exception ex)
            {
                ModLog.Warning($"NormalizeSaveBiomeField 失败 {fullPath}: {ex.Message}");
            }
        }

        private static int ParseIntField(string json, string key)
        {
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return 0;
            int p = idx + key.Length;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int start = p;
            if (start < json.Length && json[start] == '-') p++;
            while (p < json.Length && char.IsDigit(json[p])) p++;
            if (p == start) return 0;
            int.TryParse(json.Substring(start, p - start), out int v);
            return v;
        }

        private static string ReplaceJsonInt(string json, string field, int value)
        {
            string key = "\"" + field + "\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return json;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return json;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            if (end < json.Length && json[end] == '-') end++;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == 'e' || json[end] == 'E' || json[end] == '+' || json[end] == '-')) end++;
            if (end <= start) return json;
            return json.Substring(0, start) + value.ToString() + json.Substring(end);
        }

        private static string ReplaceJsonBool(string json, string field, bool value)
        {
            string key = "\"" + field + "\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return json;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return json;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            while (end < json.Length && char.IsLetter(json[end])) end++;
            if (end <= start) return json;
            return json.Substring(0, start) + (value ? "true" : "false") + json.Substring(end);
        }
    }
}
