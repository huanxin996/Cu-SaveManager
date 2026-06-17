using System;
using System.IO;
using System.Text;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 槽位伴生元数据，落到 &lt;slot&gt;.sv.json：
    /// { "nickname":"…", "pinned":false, "isAuto":true, "biome":3, "runId":12345, "lastPlayedAtUnixMs":… }
    /// 缺失字段走默认值；序列化与解析手写完成。
    /// runId 取自 save.sv 内的 cId 字段；旧版字段名 "seed" 仍可读取。
    /// </summary>
    internal sealed class SlotSidecar
    {
        internal string Nickname { get; set; } = "";
        internal bool Pinned { get; set; }
        internal bool IsAuto { get; set; }
        internal int Biome { get; set; }
        internal int RunId { get; set; }
        /// <summary>系统时间戳（Unix 毫秒），代表"最后游玩时间"= 这个槽位被生成的时刻。</summary>
        internal long LastPlayedAtUnixMs { get; set; }

        // —— QoL mod 联动字段：保存时抓 body.transform.position 与 QoL 的 SeedManager 静态字段；
        //    回档时通过 QolBridge 反射写回 QoL.SaveSystemPatcher.PendingLoadPosition / SeedManager.IsSeeded / CurrentSeed。
        //    QoL 不在场时这些字段也照写，下次装上 QoL 即可生效。
        internal float PlayerX { get; set; }
        internal float PlayerY { get; set; }
        internal int QolSeed { get; set; }
        internal string QolSeedInput { get; set; } = "";
        internal bool HasPlayerPos { get; set; }

        // —— 固定世界 / 位置模式 ——
        //    WorldEngine: "qol" | "self"，标识该存档世界由哪套引擎生成，两套不可互转。
        //    MpWorldEngine: "krok" | "self"，多人存档的世界/位置由 KrokMP 或本 mod 种子注入。
        //    PosMode: "lastPos"（回保存时坐标）| "fixedPos"（回 FixedX/FixedY）。
        internal string WorldEngine { get; set; } = "";
        internal string MpWorldEngine { get; set; } = "";
        internal string PosMode { get; set; } = "";
        internal float FixedX { get; set; }
        internal float FixedY { get; set; }
        internal bool IsMultiplayer { get; set; }

        /// <summary>本层激活的 LayerModifier 索引；-1 表示无词条。读档时由 LayerModifierRestorePatch 还原。</summary>
        internal int ActiveLayerModifierIndex { get; set; } = -1;

        internal static SlotSidecar LoadOrEmpty(string slotPath)
        {
            try
            {
                var p = SidecarPath(slotPath);
                if (!File.Exists(p)) return new SlotSidecar();
                return Parse(File.ReadAllText(p, Encoding.UTF8));
            }
            catch
            {
                return new SlotSidecar();
            }
        }

        internal void Save(string slotPath)
        {
            try
            {
                File.WriteAllText(SidecarPath(slotPath), Serialize(), Encoding.UTF8);
            }
            catch
            {
                // 保存元数据失败不致命，主存档已经在磁盘上。
            }
        }

        internal static void DeleteFor(string slotPath)
        {
            try
            {
                var p = SidecarPath(slotPath);
                if (File.Exists(p)) File.Delete(p);
            }
            catch { }
        }

        internal static string SidecarPath(string slotPath) => slotPath + ".json";

        // —— 极简 JSON 序列化 —— //

        private string Serialize()
        {
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"nickname\":").Append(EscapeJsonString(Nickname)).Append(',');
            sb.Append("\"pinned\":").Append(Pinned ? "true" : "false").Append(',');
            sb.Append("\"isAuto\":").Append(IsAuto ? "true" : "false").Append(',');
            sb.Append("\"biome\":").Append(Biome.ToString()).Append(',');
            sb.Append("\"runId\":").Append(RunId.ToString()).Append(',');
            sb.Append("\"lastPlayedAtUnixMs\":").Append(LastPlayedAtUnixMs.ToString()).Append(',');
            sb.Append("\"hasPlayerPos\":").Append(HasPlayerPos ? "true" : "false").Append(',');
            sb.Append("\"playerX\":").Append(PlayerX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"playerY\":").Append(PlayerY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"qolSeed\":").Append(QolSeed.ToString()).Append(',');
            sb.Append("\"qolSeedInput\":").Append(EscapeJsonString(QolSeedInput)).Append(',');
            sb.Append("\"worldEngine\":").Append(EscapeJsonString(WorldEngine)).Append(',');
            sb.Append("\"mpWorldEngine\":").Append(EscapeJsonString(MpWorldEngine)).Append(',');
            sb.Append("\"posMode\":").Append(EscapeJsonString(PosMode)).Append(',');
            sb.Append("\"fixedX\":").Append(FixedX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"fixedY\":").Append(FixedY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"isMultiplayer\":").Append(IsMultiplayer ? "true" : "false").Append(',');
            sb.Append("\"activeLayerModifierIndex\":").Append(ActiveLayerModifierIndex.ToString());
            sb.Append('}');
            return sb.ToString();
        }

        private static SlotSidecar Parse(string text)
        {
            var s = new SlotSidecar();
            if (string.IsNullOrEmpty(text)) return s;
            s.Nickname = ReadString(text, "nickname") ?? "";
            s.Pinned = ReadBool(text, "pinned");
            s.IsAuto = ReadBool(text, "isAuto");
            s.Biome = ReadInt(text, "biome");
            // runId 是新字段；老 sidecar 用的是 seed，向后兼容读取
            int rid = ReadInt(text, "runId");
            if (rid == 0) rid = ReadInt(text, "seed");
            s.RunId = rid;
            s.LastPlayedAtUnixMs = ReadLong(text, "lastPlayedAtUnixMs");
            s.HasPlayerPos = ReadBool(text, "hasPlayerPos");
            s.PlayerX = ReadFloat(text, "playerX");
            s.PlayerY = ReadFloat(text, "playerY");
            s.QolSeed = ReadInt(text, "qolSeed");
            s.QolSeedInput = ReadString(text, "qolSeedInput") ?? "";
            s.WorldEngine = ReadString(text, "worldEngine") ?? "";
            s.MpWorldEngine = ReadString(text, "mpWorldEngine") ?? "";
            s.PosMode = ReadString(text, "posMode") ?? "";
            s.FixedX = ReadFloat(text, "fixedX");
            s.FixedY = ReadFloat(text, "fixedY");
            s.IsMultiplayer = ReadBool(text, "isMultiplayer");
            int idx = ReadInt(text, "activeLayerModifierIndex");
            // 老 sidecar 没这字段：ReadInt 返 0；用 FindKey 区分"无字段"vs"显式 0"
            s.ActiveLayerModifierIndex = FindKey(text, "activeLayerModifierIndex") < 0 ? -1 : idx;
            return s;
        }

        private static string EscapeJsonString(string raw)
        {
            if (raw == null) return "\"\"";
            var sb = new StringBuilder(raw.Length + 2);
            sb.Append('"');
            foreach (var c in raw)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static int FindKey(string text, string key)
        {
            string needle = "\"" + key + "\"";
            int idx = text.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return -1;
            int colon = text.IndexOf(':', idx + needle.Length);
            if (colon < 0) return -1;
            // 跳过空白
            int p = colon + 1;
            while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
            return p;
        }

        private static string ReadString(string text, string key)
        {
            int p = FindKey(text, key);
            if (p < 0 || p >= text.Length || text[p] != '"') return null;
            var sb = new StringBuilder();
            p++;
            while (p < text.Length)
            {
                char c = text[p++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && p < text.Length)
                {
                    char n = text[p++];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(n); break;
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool ReadBool(string text, string key)
        {
            int p = FindKey(text, key);
            if (p < 0) return false;
            return p + 4 <= text.Length && text.Substring(p, 4) == "true";
        }

        private static int ReadInt(string text, string key)
        {
            int p = FindKey(text, key);
            if (p < 0) return 0;
            int start = p;
            if (start < text.Length && text[start] == '-') start++;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start) return 0;
            int.TryParse(text.Substring(p, end - p), out int v);
            return v;
        }

        private static long ReadLong(string text, string key)
        {
            int p = FindKey(text, key);
            if (p < 0) return 0L;
            int start = p;
            if (start < text.Length && text[start] == '-') start++;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end == start) return 0L;
            long.TryParse(text.Substring(p, end - p), out long v);
            return v;
        }

        private static float ReadFloat(string text, string key)
        {
            int p = FindKey(text, key);
            if (p < 0) return 0f;
            int start = p;
            if (start < text.Length && text[start] == '-') start++;
            int end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == 'e' || text[end] == 'E' || text[end] == '+' || text[end] == '-'))
                end++;
            if (end == start) return 0f;
            float.TryParse(text.Substring(p, end - p),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v);
            return v;
        }
    }
}
