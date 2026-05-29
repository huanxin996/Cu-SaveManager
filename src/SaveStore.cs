using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 文件级槽位 CRUD。目录结构 slots/&lt;runId&gt;/&lt;YYYY-MM-DD&gt;/&lt;runId&gt;-&lt;biome&gt;-&lt;unix秒&gt;.sv。
    /// runId 取自 save.sv 内的 cId 字段（角色身份四元组之一，1~99999），整局 run 中不变；
    /// 读不到 cId 的旧文件归到 runId=0 的"未识别冒险"分组，由 ListSlots 触发迁移。
    /// 同名 sidecar JSON 记昵称 / pinned / isAuto / runId。pinned 槽位永不参与自动删除。
    /// </summary>
    internal sealed class SaveStore
    {
        private readonly ManualLogSource _log;
        private readonly string _slotsRoot;
        private static ManualLogSource _staticLog;

        internal SaveStore(ManualLogSource log)
        {
            _log = log;
            _staticLog = log;
            _slotsRoot = ResolveSlotsRoot();
            try { Directory.CreateDirectory(_slotsRoot); }
            catch (Exception ex) { _log.LogWarning($"创建 slots 根目录失败：{ex.Message}"); }
        }

        internal string SlotsRoot => _slotsRoot;

        /// <summary>当前游戏 save.sv 的真实路径：与 KrokMP 一致，仅多人会话运行中且 mp 存档存在时用 mp_save，否则用单机路径。</summary>
        internal static string GameSavePath
        {
            get
            {
                string mp = Path.Combine(Application.persistentDataPath, "mp_save", "save.sv");
                if (MultiplayerBridge.IsMultiplayerRunning(_staticLog) && File.Exists(mp)) return mp;
                return Path.Combine(Application.persistentDataPath, "save.sv");
            }
        }

        /// <summary>save.sv 路径决策诊断：当前模式 + mp/单机候选存在性 + 最终选定路径。</summary>
        internal static string DescribeSavePathDecision()
        {
            bool mpRunning = MultiplayerBridge.IsMultiplayerRunning(_staticLog);
            string mp = Path.Combine(Application.persistentDataPath, "mp_save", "save.sv");
            string vanilla = Path.Combine(Application.persistentDataPath, "save.sv");
            string mode = mpRunning ? I18n.T("mode.multiplayer") : I18n.T("mode.singleplayer");
            return I18n.F("fmt.save_path_diag", mode, GameSavePath, File.Exists(mp), File.Exists(vanilla));
        }

        // —— 写 —— //

        /// <summary>手动保存：复制当前 save.sv 到今天目录，写 sidecar；可指定昵称。返回路径或抛异常。</summary>
        internal string SaveManual(string nickname)
        {
            var ctx = SnapshotGameContext();
            string path = WriteSlotFile(ctx, isAuto: false);
            new SlotSidecar
            {
                Nickname = nickname ?? "",
                Pinned = false,
                IsAuto = false,
                Biome = ctx.Biome,
                RunId = ctx.RunId,
                LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                HasPlayerPos = ctx.HasPlayerPos,
                PlayerX = ctx.PlayerX,
                PlayerY = ctx.PlayerY,
                QolSeed = ctx.QolSeed,
                QolSeedInput = ctx.QolSeedInput,
            }.Save(path);
            return path;
        }

        /// <summary>定时备份：复制并按 keep 份数滚动删除最旧的非 pinned 自动槽。</summary>
        internal string AutoBackup(int keep)
        {
            var ctx = SnapshotGameContext();
            string path = WriteSlotFile(ctx, isAuto: true);
            new SlotSidecar
            {
                Nickname = "",
                Pinned = false,
                IsAuto = true,
                Biome = ctx.Biome,
                RunId = ctx.RunId,
                LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                HasPlayerPos = ctx.HasPlayerPos,
                PlayerX = ctx.PlayerX,
                PlayerY = ctx.PlayerY,
                QolSeed = ctx.QolSeed,
                QolSeedInput = ctx.QolSeedInput,
            }.Save(path);
            PruneAutoBackups(keep);
            return path;
        }

        // —— 读 —— //

        /// <summary>列出全部槽位（跨 runId/日期 两层目录），按修改时间倒序。
        /// 扫描时顺手为旧 sidecar.RunId==0 的槽迁移：从 .sv 解出 cId 写回 sidecar，并把文件移动到正确的 runId 目录。</summary>
        internal List<SlotInfo> ListSlots()
        {
            var migrated = new List<(string oldPath, string newPath)>();
            var result = new List<SlotInfo>();
            try
            {
                var root = new DirectoryInfo(_slotsRoot);
                if (!root.Exists) return result;
                foreach (var topDir in root.GetDirectories())
                {
                    foreach (var dateDir in topDir.GetDirectories())
                    {
                        foreach (var f in dateDir.GetFiles("*.sv"))
                        {
                            CollectOrMigrate(f, dateDir.Name, topDir.Name, result, migrated);
                        }
                    }
                    // 兼容老版本：topDir 直接放 .sv 而没有日期子目录
                    foreach (var f in topDir.GetFiles("*.sv"))
                    {
                        CollectOrMigrate(f, "(无日期)", topDir.Name, result, migrated);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"扫描 slots 失败：{ex.Message}");
            }
            // 迁移影响：path 已变，重新拼一次返回最新视图
            if (migrated.Count > 0)
            {
                _log.LogInfo($"为 {migrated.Count} 个旧槽位迁移目录到 runId 分组");
            }
            // 用 sidecar.LastPlayedAtUnixMs（保存时刻）排序，比 File.LastWriteTimeUtc 稳定——
            // 后者会在迁移 / 复制时被改写，导致排序错乱。
            return result
                .OrderByDescending(s => s.Sidecar.LastPlayedAtUnixMs)
                .ThenByDescending(s => s.File.LastWriteTimeUtc)
                .ToList();
        }

        /// <summary>把 fi 收进结果列表；若 sidecar 缺 RunId，则解 .sv 拿 cId 回填，并把文件迁到 slots/&lt;runId&gt;/&lt;dateFolder&gt;/。</summary>
        private void CollectOrMigrate(FileInfo fi, string dateFolder, string topFolder,
            List<SlotInfo> result, List<(string, string)> migrated)
        {
            var sidecar = SlotSidecar.LoadOrEmpty(fi.FullName);
            // 已经有 runId、且当前 topFolder 与 runId 一致：原样收
            if (sidecar.RunId != 0 && topFolder == sidecar.RunId.ToString())
            {
                result.Add(new SlotInfo(fi, sidecar, dateFolder, topFolder));
                return;
            }
            // 否则尝试从 .sv 解出 cId
            int runId = sidecar.RunId != 0 ? sidecar.RunId : ReadRunIdFromSv(fi.FullName);
            if (runId == 0)
            {
                // 解不出（损坏 / 格式不符），原样收，保留旧 topFolder 当 group key
                result.Add(new SlotInfo(fi, sidecar, dateFolder, topFolder));
                return;
            }
            // 准备搬迁
            string targetDateFolder = dateFolder == "(无日期)" ? DateTime.Now.ToString("yyyy-MM-dd") : dateFolder;
            string targetDir = Path.Combine(_slotsRoot, runId.ToString(), targetDateFolder);
            try
            {
                Directory.CreateDirectory(targetDir);
                string targetPath = Path.Combine(targetDir, fi.Name);
                int n = 1;
                while (File.Exists(targetPath) && !PathsEqual(targetPath, fi.FullName))
                {
                    targetPath = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(fi.Name) + "-" + n + ".sv");
                    n++;
                }
                if (!PathsEqual(targetPath, fi.FullName))
                {
                    File.Move(fi.FullName, targetPath);
                    string oldSidecar = SlotSidecar.SidecarPath(fi.FullName);
                    string newSidecar = SlotSidecar.SidecarPath(targetPath);
                    if (File.Exists(oldSidecar)) File.Move(oldSidecar, newSidecar);
                    migrated.Add((fi.FullName, targetPath));
                    fi = new FileInfo(targetPath);
                }
                if (sidecar.RunId == 0)
                {
                    sidecar.RunId = runId;
                    sidecar.Save(fi.FullName);
                }
                result.Add(new SlotInfo(fi, sidecar, targetDateFolder, runId.ToString()));
            }
            catch (Exception ex)
            {
                _log.LogWarning($"迁移槽位失败 {fi.FullName} → runId={runId}: {ex.Message}");
                result.Add(new SlotInfo(fi, sidecar, dateFolder, topFolder));
            }
        }

        private static bool PathsEqual(string a, string b)
            => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // —— 改 —— //

        /// <summary>把指定槽位文件覆盖回 save.sv。可选先备份当前 save.sv 到今天目录。</summary>
        internal void RestoreSlotToSave(string slotFullPath, bool backupBefore)
        {
            if (!File.Exists(slotFullPath))
            {
                throw new FileNotFoundException("槽位文件不存在", slotFullPath);
            }
            string game = GameSavePath;
            _log.LogInfo(DescribeSavePathDecision());
            if (backupBefore && File.Exists(game))
            {
                var ctx = SnapshotGameContext();
                string bakPath = WriteSlotFile(ctx, isAuto: false, prefix: "beforeLoad-");
                new SlotSidecar
                {
                    Nickname = I18n.T("lbl.before_load_alias"),
                    Pinned = false,
                    IsAuto = false,
                    Biome = ctx.Biome,
                    RunId = ctx.RunId,
                    LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    HasPlayerPos = ctx.HasPlayerPos,
                    PlayerX = ctx.PlayerX,
                    PlayerY = ctx.PlayerY,
                    QolSeed = ctx.QolSeed,
                    QolSeedInput = ctx.QolSeedInput,
                }.Save(bakPath);
            }
            File.Copy(slotFullPath, game, overwrite: true);
            // 让 QoL（如果在场）下次读档时把玩家位置 + 种子还原；不影响游戏自身原有行为
            QolBridge.PrepareRollback(SlotSidecar.LoadOrEmpty(slotFullPath), _log);
        }

        internal void DeleteSlot(string slotFullPath)
        {
            try { if (File.Exists(slotFullPath)) File.Delete(slotFullPath); } catch { }
            SlotSidecar.DeleteFor(slotFullPath);
        }

        internal void UpdateSidecar(string slotFullPath, SlotSidecar sidecar) => sidecar.Save(slotFullPath);

        /// <summary>
        /// 自动死亡回档用的槽位选择：限定当前 runId 内，优先最新自动备份，没有则返回最新手动备份；
        /// beforeLoad- 前缀的备份不参与。runId=0 或当前 run 无备份返回 null。
        /// </summary>
        internal SlotInfo FindLatestRollbackTarget()
        {
            int currentRunId = ComputeCurrentRunId();
            if (currentRunId == 0) return null;
            var slots = ListSlots().Where(s => s.Sidecar.RunId == currentRunId).ToList();
            bool NotBeforeLoad(SlotInfo s) => !s.File.Name.StartsWith("beforeLoad-", StringComparison.Ordinal);
            var auto = slots.FirstOrDefault(s => s.Sidecar.IsAuto && NotBeforeLoad(s));
            if (auto != null) return auto;
            return slots.FirstOrDefault(s => !s.Sidecar.IsAuto && NotBeforeLoad(s));
        }

        /// <summary>计算 save.sv 的 MD5。SlotInfo 用其判断槽位内容与当前 save.sv 是否相同。</summary>
        internal static string ComputeFileHash(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath)) return null;
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(fullPath))
                {
                    byte[] buf = md5.ComputeHash(stream);
                    var sb = new System.Text.StringBuilder(32);
                    for (int i = 0; i < buf.Length; i++) sb.Append(buf[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 从指定 .sv 解 runId。先 Unzip 取 body 内 "cId"，0 时回落 mp_rules.json 的 SAVEID
        /// 与 PLRPOS 第一个 STEAM_xxx 的稳定 hash。任何中间失败都打 warning 日志。失败返回 0。
        /// </summary>
        internal static int ReadRunIdFromSv(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                _staticLog?.LogWarning("ReadRunIdFromSv: 路径为空");
                return 0;
            }
            if (!File.Exists(fullPath))
            {
                _staticLog?.LogWarning($"ReadRunIdFromSv: 文件不存在 {fullPath}");
                return 0;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0)
                {
                    _staticLog?.LogWarning($"ReadRunIdFromSv: 文件为空 {fullPath}");
                    return 0;
                }
                string json;
                try
                {
                    json = SaveSystem.Unzip(bytes);
                }
                catch (Exception ex)
                {
                    _staticLog?.LogWarning($"ReadRunIdFromSv: Unzip 失败 {fullPath}: {ex.Message}");
                    json = null;
                }
                if (!string.IsNullOrEmpty(json))
                {
                    int v = ParseIntField(json, "\"cId\":");
                    if (v != 0) return v;
                    _staticLog?.LogInfo($"ReadRunIdFromSv: {Path.GetFileName(fullPath)} body cId=0，尝试 mp_rules.json fallback");
                }
                else
                {
                    _staticLog?.LogWarning($"ReadRunIdFromSv: {fullPath} Unzip 后 json 为空");
                }

                string dir = Path.GetDirectoryName(fullPath);
                string rulesPath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "mp_rules.json");
                if (rulesPath != null && File.Exists(rulesPath))
                {
                    string rj = File.ReadAllText(rulesPath);
                    int sid = ParseIntField(rj, "\"SAVEID\":");
                    if (sid != 0) return sid;
                    int idx = rj.IndexOf("STEAM_", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        int end = idx;
                        while (end < rj.Length && (char.IsLetterOrDigit(rj[end]) || rj[end] == '_')) end++;
                        if (end > idx)
                        {
                            string steamId = rj.Substring(idx, end - idx);
                            return StableHash(steamId);
                        }
                    }
                    _staticLog?.LogWarning($"ReadRunIdFromSv: {rulesPath} 无 SAVEID 与 STEAM_xxx，无法解 runId");
                }
                return 0;
            }
            catch (Exception ex)
            {
                _staticLog?.LogWarning($"ReadRunIdFromSv: 处理 {fullPath} 异常 {ex.Message}");
                return 0;
            }
        }

        /// <summary>FNV-1a 32 位 hash；同字符串永远得同 int，结果非零（0 表示"未识别"）。</summary>
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            int v = (int)h;
            return v == 0 ? 1 : v;
        }

        /// <summary>纯字符串搜 key 后的整数；找不到或解析失败返回 0。</summary>
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

        internal static int ComputeCurrentRunId()
        {
            // 1) 内存：游戏在 TryLoadGame / 新开 run 时维护 WoundView.view.cInfo[2]
            try
            {
                var view = WoundView.view;
                if (view != null && view.cInfo != null && view.cInfo.Length >= 3)
                {
                    int v = view.cInfo[2];
                    if (v != 0) return v;
                }
            }
            catch (Exception ex)
            {
                _staticLog?.LogWarning($"ComputeCurrentRunId: 读 WoundView.cInfo[2] 异常 {ex.Message}");
            }

            // 2) 候选 .sv 文件：先单机/多人 save.sv，再 autosave.sv（save.sv 有时 cId=0 但 autosave.sv 已有真值）
            string p = Application.persistentDataPath;
            string[] candidates = new[]
            {
                Path.Combine(p, "mp_save", "save.sv"),
                Path.Combine(p, "mp_save", "autosave.sv"),
                Path.Combine(p, "save.sv"),
                Path.Combine(p, "autosave.sv"),
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                int v = ReadRunIdFromSv(path);
                if (v != 0)
                {
                    _staticLog?.LogInfo($"ComputeCurrentRunId: 通过 {Path.GetFileName(path)} 解出 runId={v}");
                    return v;
                }
            }
            _staticLog?.LogWarning("ComputeCurrentRunId: 所有候选 .sv 都解不出 runId，返回 0");
            return 0;
        }

        /// <summary>当前游戏 save.sv 的 hash；不存在或读失败返回 null。</summary>
        internal static string ComputeCurrentSaveHash() => ComputeFileHash(GameSavePath);

        // —— 内部 —— //

        /// <summary>每个冒险（runId）独立保留 keep 份自动备份，互不挤占。</summary>
        private void PruneAutoBackups(int keep)
        {
            try
            {
                int currentRunId = ComputeCurrentRunId();
                var autos = ListSlots()
                    .Where(s => s.Sidecar.IsAuto && !s.Sidecar.Pinned && s.Sidecar.RunId == currentRunId)
                    .OrderByDescending(s => s.Sidecar.LastPlayedAtUnixMs)
                    .ThenByDescending(s => s.File.LastWriteTimeUtc)
                    .Skip(Math.Max(1, keep))
                    .ToList();
                foreach (var s in autos)
                {
                    try { DeleteSlot(s.FullSlotPath); } catch { }
                }
            }
            catch { }
        }

        private string WriteSlotFile(GameContext ctx, bool isAuto, string prefix = null)
        {
            string src = GameSavePath;
            if (!File.Exists(src))
            {
                throw new FileNotFoundException("当前没有 save.sv 可复制");
            }

            // 文件夹结构：slots/<runId>/<YYYY-MM-DD>/<runId>-<biome>-<unix秒>.sv
            int runId = ctx.RunId != 0 ? ctx.RunId : ReadRunIdFromSv(src);
            string runFolder = runId == 0 ? "0" : runId.ToString();
            string dateFolder = DateTime.Now.ToString("yyyy-MM-dd");
            string fullDir = Path.Combine(_slotsRoot, runFolder, dateFolder);
            Directory.CreateDirectory(fullDir);

            long unixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string baseName = $"{runFolder}-{ctx.Biome}-{unixSec}";
            if (!string.IsNullOrEmpty(prefix)) baseName = prefix + baseName;

            string dst = Path.Combine(fullDir, baseName + ".sv");
            int n = 1;
            while (File.Exists(dst))
            {
                dst = Path.Combine(fullDir, $"{baseName}-{n}.sv");
                n++;
            }

            File.Copy(src, dst, overwrite: false);
            return dst;
        }

        /// <summary>读当前游戏上下文（biome + runId + 玩家位置 + QoL 种子）。游戏未加载时各项为默认值。</summary>
        private GameContext SnapshotGameContext()
        {
            int biome = 0;
            float px = 0f, py = 0f;
            bool hasPos = false;
            try
            {
                if (WorldGeneration.world != null) biome = WorldGeneration.world.biomeDepth;
            }
            catch { }
            try
            {
                var cam = PlayerCamera.main;
                if (cam != null && cam.body != null)
                {
                    var p = cam.body.transform.position;
                    px = p.x;
                    py = p.y;
                    hasPos = true;
                }
            }
            catch { }
            QolBridge.ReadCurrentSeed(out int qolSeed, out string qolInput, _log);
            return new GameContext
            {
                Biome = biome,
                RunId = ComputeCurrentRunId(),
                PlayerX = px,
                PlayerY = py,
                HasPlayerPos = hasPos,
                QolSeed = qolSeed,
                QolSeedInput = qolInput,
            };
        }

        private struct GameContext
        {
            public int Biome;
            public int RunId;
            public float PlayerX;
            public float PlayerY;
            public bool HasPlayerPos;
            public int QolSeed;
            public string QolSeedInput;
        }

        private static string ResolveSlotsRoot()
        {
            var asmDir = Path.GetDirectoryName(typeof(SaveStore).Assembly.Location)
                          ?? Path.Combine(Paths.PluginPath, "SaveManager");
            return Path.Combine(asmDir, "slots");
        }
    }
}
