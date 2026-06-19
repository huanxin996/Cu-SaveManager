using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
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
        private readonly string _slotsRoot;

        internal SaveStore()
        {
            _slotsRoot = ResolveSlotsRoot();
            try { Directory.CreateDirectory(_slotsRoot); }
            catch (Exception ex) { ModLog.Warning($"创建 slots 根目录失败：{ex.Message}"); }
        }

        internal string SlotsRoot => _slotsRoot;

        /// <summary>单机 save.sv 路径。多人存档是目录（见 MpSaveLocator），不走此属性。</summary>
        internal static string GameSavePath => MpSaveLocator.VanillaSavePath;

        /// <summary>当前游戏存档用于显示的路径：多人会话返回 mp_save 目录，否则单机 save.sv。</summary>
        internal static string CurrentSaveDisplayPath
            => MpSaveLocator.IsMultiplayerSaveActive() ? MpSaveLocator.MpSaveDir : MpSaveLocator.VanillaSavePath;

        /// <summary>当前是否处于多人上下文（供 UI 文案判定）。</summary>
        internal static bool IsMultiplayerContextActive() => MpSaveLocator.IsMultiplayerSaveActive();

        /// <summary>当前是否存在有效游戏存档：多人看 mp_save，单机看 save.sv。</summary>
        internal static bool CurrentSaveExists()
            => MpSaveLocator.IsMultiplayerSaveActive() || File.Exists(MpSaveLocator.VanillaSavePath);

        /// <summary>存档路径决策诊断：当前模式 + 多人判定分项（running/context/mp_save）+ 选定路径。</summary>
        internal static string DescribeSavePathDecision()
        {
            bool mpRunning = MultiplayerBridge.IsMultiplayerRunning();
            bool mpEnabled = MultiplayerBridge.IsMultiplayerEnabled();
            bool mpModPresent = MultiplayerBridge.IsModPresent();
            bool mpSave = MpSaveLocator.HasMpSave();
            string vanilla = MpSaveLocator.VanillaSavePath;
            string mode = mpEnabled ? I18n.T("mode.multiplayer") : I18n.T("mode.singleplayer");
            string chosen = MpSaveLocator.IsMultiplayerSaveActive() ? MpSaveLocator.MpSaveDir : vanilla;
            return I18n.F("fmt.save_path_diag", mode, chosen, mpSave, File.Exists(vanilla))
                + $" [modPresent={mpModPresent} enabled={mpEnabled} running={mpRunning}]";
        }

        // —— 写 —— //

        /// <summary>手动保存：复制当前 save.sv 到今天目录，写 sidecar；可指定昵称。返回路径或抛异常。
        /// 调用前须已执行 SaveSystem.SaveGame()。</summary>
        internal string SaveManual(string nickname)
        {
            var ctx = SnapshotGameContextFromSavedGame();
            PersistCurrentSaveForContinue(ctx);
            string path = WriteSlotFile(ctx, isAuto: false);
            BuildSidecar(ctx, nickname ?? "", isAuto: false).Save(path);
            return path;
        }

        /// <summary>定时备份：复制并按 keep 份数滚动删除最旧的非 pinned 自动槽。
        /// 调用前须已执行 SaveSystem.SaveGame()。</summary>
        internal string AutoBackup(int keep)
        {
            var ctx = SnapshotGameContextFromSavedGame();
            PersistCurrentSaveForContinue(ctx);
            string path = WriteSlotFile(ctx, isAuto: true);
            BuildSidecar(ctx, "", isAuto: true).Save(path);
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
                ModLog.Warning($"扫描 slots 失败：{ex.Message}");
            }
            // 迁移影响：path 已变，重新拼一次返回最新视图
            if (migrated.Count > 0)
            {
                ModLog.Info($"为 {migrated.Count} 个旧槽位迁移目录到 runId 分组");
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
                ModLog.Warning($"迁移槽位失败 {fi.FullName} → runId={runId}: {ex.Message}");
                result.Add(new SlotInfo(fi, sidecar, dateFolder, topFolder));
            }
        }

        private static bool PathsEqual(string a, string b)
            => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // —— 改 —— //

        /// <summary>把指定槽位还原到游戏存档。单机覆盖 save.sv；多人解包回 mp_save 并由主机重新发起加载。</summary>
        internal void RestoreSlotToSave(string slotFullPath, bool backupBefore)
        {
            if (!File.Exists(slotFullPath))
            {
                throw new FileNotFoundException("槽位文件不存在", slotFullPath);
            }
            ModLog.Info(DescribeSavePathDecision());
            var sidecar = SlotSidecar.LoadOrEmpty(slotFullPath);
            bool mpRunning = MultiplayerBridge.IsMultiplayerRunning();

            if (sidecar.IsMultiplayer)
            {
                if (!mpRunning)
                {
                    ModLog.Warning(I18n.T("mp.slot_needs_mp_session"));
                    throw new InvalidOperationException(I18n.T("mp.slot_needs_mp_session"));
                }
                RestoreMultiplayerSlot(slotFullPath, backupBefore);
                return;
            }
            if (mpRunning)
            {
                ModLog.Warning(I18n.T("mp.slot_is_singleplayer"));
                throw new InvalidOperationException(I18n.T("mp.slot_is_singleplayer"));
            }

            string game = GameSavePath;
            if (backupBefore && File.Exists(game))
            {
                var ctx = SnapshotGameContext();
                string bakPath = WriteSlotFile(ctx, isAuto: false, prefix: "beforeLoad-");
                var bak = BuildSidecar(ctx, I18n.T("lbl.before_load_alias"), isAuto: false);
                bak.Save(bakPath);
            }
            File.Copy(slotFullPath, game, overwrite: true);
            NormalizeSaveBiome(game, sidecar.Biome);
            PersistCurrentSaveSidecar(sidecar);
            PrepareWorldForSlot(sidecar);
        }

        /// <summary>多人槽位还原：仅主机可执行——可选备份当前 mp_save，解包目标 zip 回 mp_save，再触发主机重新加载。</summary>
        private void RestoreMultiplayerSlot(string slotFullPath, bool backupBefore)
        {
            if (MultiplayerBridge.IsMultiplayerRunning() && !MultiplayerBridge.IsServer())
            {
                ModLog.Warning(I18n.T("mp.only_host_can_rollback"));
                throw new InvalidOperationException(I18n.T("mp.only_host_can_rollback"));
            }
            if (backupBefore)
            {
                MultiplayerBridge.TrySaveMpGame();
                if (MpSaveLocator.HasMpSave())
                {
                    var ctx = SnapshotGameContext();
                    string bakPath = WriteSlotFile(ctx, isAuto: false, prefix: "beforeLoad-");
                    BuildSidecar(ctx, I18n.T("lbl.before_load_alias"), isAuto: false).Save(bakPath);
                }
            }
            MpSaveLocator.RestoreMpSaveFrom(slotFullPath);
            var sidecar = SlotSidecar.LoadOrEmpty(slotFullPath);
            PrepareWorldForSlot(sidecar);
            WorldEngineArbiter.PrepareMpRollback(sidecar);
            MultiplayerBridge.PrepareMpRollback(sidecar);
            MpPositionRestorer.PrepareForRollback(sidecar);
            ModLog.Info("多人回档：mp_save 已还原，继续游戏由 KrokMP 原生流程加载");
            MpRollbackController.TriggerHostReload();
        }

        private static float ParseTupleField(string inner, string field)
        {
            int k = inner.IndexOf("\"" + field + "\"", StringComparison.Ordinal);
            if (k < 0) return 0f;
            int colon = inner.IndexOf(':', k);
            if (colon < 0) return 0f;
            int s = colon + 1;
            while (s < inner.Length && (inner[s] == ' ' || inner[s] == '\t')) s++;
            int e = s;
            while (e < inner.Length && (char.IsDigit(inner[e]) || inner[e] == '-' || inner[e] == '.' || inner[e] == 'e' || inner[e] == 'E' || inner[e] == '+')) e++;
            if (e == s) return 0f;
            float.TryParse(inner.Substring(s, e - s),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            return v;
        }

        /// <summary>是否按多人存档处理本次备份/还原：真在多人会话且 mp_save 已生成。决定打包 zip 与 sidecar.IsMultiplayer 标记。</summary>
        private static bool ShouldUseMpSave()
            => MultiplayerBridge.IsMultiplayerRunning() && MpSaveLocator.HasMpSave();

        /// <summary>统一构造 sidecar：填入上下文 + 默认位置模式（lastPos，FixedXY=当前坐标）。</summary>
        private static SlotSidecar BuildSidecar(GameContext ctx, string nickname, bool isAuto)
        {
            return new SlotSidecar
            {
                Nickname = nickname ?? "",
                Pinned = false,
                IsAuto = isAuto,
                Biome = ctx.Biome,
                RunId = ctx.RunId,
                LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                HasPlayerPos = ctx.HasPlayerPos,
                PlayerX = ctx.PlayerX,
                PlayerY = ctx.PlayerY,
                QolSeed = ctx.QolSeed,
                QolSeedInput = ctx.QolSeedInput,
                WorldEngine = ctx.WorldEngine,
                PosMode = WorldEngineArbiter.PositionMode,
                FixedX = WorldEngineArbiter.FixedX,
                FixedY = WorldEngineArbiter.FixedY,
                IsMultiplayer = ShouldUseMpSave(),
                ActiveLayerModifierIndex = ctx.ActiveLayerModifierIndex,
            };
        }

        /// <summary>读档/回档前按 sidecar 选定的世界引擎准备世界还原：self 由本模组写回位置，qol 交给 QoL。</summary>
        internal void PrepareWorldForSlot(SlotSidecar sidecar)
        {
            if (sidecar == null) return;
            // bug2：本层词条按存档还原，阻止游戏 ApplyLayerModifiers 重滚；两套引擎都生效。
            ApplyLayerModifiersRestorePatch.PendingIndex = sidecar.ActiveLayerModifierIndex;
            RunSettingsBridge.RestoreFromSaveFile(sidecar.IsMultiplayer
                ? MpSaveLocator.ResolveLocalPlayerSavePath()
                : GameSavePath);
            string eff = WorldEngineArbiter.ResolveEffectiveEngineName(sidecar.WorldEngine);
            if (eff == "krok")
            {
                ModLog.Warning("单机回档忽略 krok 引擎，回落 self");
                eff = "self";
            }
            bool wantSelf = eff == "self";
            ModLog.Info($"回档引擎={eff} (sidecar.WorldEngine='{sidecar.WorldEngine}')");

            if (wantSelf)
            {
                WorldEngineArbiter.Apply(WorldEngine.Self, sidecar.QolSeed, sidecar.QolSeedInput);
                IntroLifepodOnLoadGuardPatch.PendingSkipIntroLifepod = true;
                var pos = ResolveSpawnPosition(sidecar);
                if (pos.HasValue)
                {
                    SelfSpawnPatcher.PendingPosition = pos;
                    ModLog.Info($"回档准备：位置模式={sidecar.PosMode} 目标=({pos.Value.x:0.0},{pos.Value.y:0.0}) hasPos={sidecar.HasPlayerPos}");
                }
                else
                {
                    ModLog.Warning($"回档准备：无可还原位置（hasPos={sidecar.HasPlayerPos} posMode={sidecar.PosMode}）");
                }
            }
            else
            {
                if (!QolBridge.IsQolPresent())
                {
                    ModLog.Warning("该存档由 QoL 引擎生成，但当前未检测到 QoL，世界可能不一致");
                }
                WorldEngineArbiter.Apply(WorldEngine.Qol, sidecar.QolSeed, sidecar.QolSeedInput);
                QolBridge.PrepareRollback(sidecar);
            }
        }

        /// <summary>主菜单 Continue 前准备当前 save.sv：优先读 companion 元数据，缺失时按当前配置补齐。</summary>
        internal void PrepareCurrentSaveForContinue()
        {
            if (ShouldUseMpSave()) return;
            if (!File.Exists(GameSavePath)) return;

            var sidecar = SlotSidecar.LoadOrEmpty(GameSavePath);
            if (IsEmptySidecar(sidecar)) sidecar = BuildFallbackCurrentSaveSidecar();
            else FillMissingContinueFields(sidecar);

            sidecar.WorldEngine = ResolveCurrentPreferredEngineName();
            sidecar.PosMode = WorldEngineArbiter.PositionMode;
            sidecar.FixedX = WorldEngineArbiter.FixedX;
            sidecar.FixedY = WorldEngineArbiter.FixedY;

            // 不在此处改写 biome：save.sv 的 biome 由存档时刻按是否在层底写定（层底=进下一层，保留 biomeDepth+1；
            // 层中=原位续玩，已规范回当前层）。主菜单 Continue 时拿不到场内玩家位置，重复规范会把进层用的 biomeDepth+1 改回当前层=卡层。
            PersistCurrentSaveSidecar(sidecar);
            PrepareWorldForSlot(sidecar);
        }

        private static Vector3? ResolveSpawnPosition(SlotSidecar sidecar)
        {
            bool fixedMode = string.Equals(sidecar.PosMode, "fixedPos", StringComparison.OrdinalIgnoreCase);
            if (fixedMode) return new Vector3(sidecar.FixedX, sidecar.FixedY, 0f);
            if (sidecar.HasPlayerPos) return new Vector3(sidecar.PlayerX, sidecar.PlayerY, 0f);
            return null;
        }

        internal void PersistCurrentSaveForContinue()
        {
            PersistCurrentSaveForContinue(SnapshotGameContext());
        }

        private void PersistCurrentSaveForContinue(GameContext ctx)
        {
            if (ShouldUseMpSave()) return;
            if (!File.Exists(GameSavePath)) return;
            // 游戏以「存档→读档」推进层：SaveGame 写 biome=biomeDepth+1，TryLoadGame 读回。
            // 层底保存即「进下一层」，必须保留游戏写入的 biomeDepth+1，否则读档回原层＝卡层；
            // 仅层中保存才把 biome 规范回当前层以便原位续玩。
            if (!IsPlayerAtLayerBoundary())
                NormalizeSaveBiome(GameSavePath, ctx.Biome);
            PersistCurrentSaveSidecar(BuildSidecar(ctx, "", isAuto: false));
        }

        private static void PersistCurrentSaveSidecar(SlotSidecar sidecar)
        {
            if (sidecar == null || ShouldUseMpSave()) return;
            sidecar.Save(GameSavePath);
        }

        private static SlotSidecar BuildFallbackCurrentSaveSidecar()
        {
            int seed = QolBridge.ComputeDeterministicSeedFromSaveFile(GameSavePath);
            return new SlotSidecar
            {
                Biome = ReadBiomeFromSv(GameSavePath),
                RunId = ReadRunIdFromSv(GameSavePath),
                LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                QolSeed = seed,
                QolSeedInput = "",
                WorldEngine = ResolveCurrentPreferredEngineName(),
                PosMode = WorldEngineArbiter.PositionMode,
                FixedX = WorldEngineArbiter.FixedX,
                FixedY = WorldEngineArbiter.FixedY,
            };
        }

        private static void FillMissingContinueFields(SlotSidecar sidecar)
        {
            if (sidecar == null) return;
            if (string.IsNullOrWhiteSpace(sidecar.PosMode))
            {
                sidecar.PosMode = WorldEngineArbiter.PositionMode;
                sidecar.FixedX = WorldEngineArbiter.FixedX;
                sidecar.FixedY = WorldEngineArbiter.FixedY;
            }
            if (sidecar.QolSeed == 0) sidecar.QolSeed = QolBridge.ComputeDeterministicSeedFromSaveFile(GameSavePath);
            if (sidecar.RunId == 0) sidecar.RunId = ReadRunIdFromSv(GameSavePath);
            sidecar.LastPlayedAtUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private static bool IsEmptySidecar(SlotSidecar sidecar)
        {
            if (sidecar == null) return true;
            return !sidecar.Pinned
                && !sidecar.IsAuto
                && sidecar.Biome == 0
                && sidecar.RunId == 0
                && sidecar.LastPlayedAtUnixMs == 0
                && !sidecar.HasPlayerPos
                && sidecar.PlayerX == 0f
                && sidecar.PlayerY == 0f
                && sidecar.QolSeed == 0
                && string.IsNullOrEmpty(sidecar.QolSeedInput)
                && string.IsNullOrEmpty(sidecar.WorldEngine)
                && string.IsNullOrEmpty(sidecar.PosMode)
                && sidecar.FixedX == 0f
                && sidecar.FixedY == 0f
                && !sidecar.IsMultiplayer
                && string.IsNullOrEmpty(sidecar.Nickname);
        }

        private static string ResolveCurrentPreferredEngineName()
        {
            return WorldEngineArbiter.ResolveEffectiveEngineName();
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
                ModLog.Warning("ReadRunIdFromSv: 路径为空");
                return 0;
            }
            if (!File.Exists(fullPath))
            {
                ModLog.Warning($"ReadRunIdFromSv: 文件不存在 {fullPath}");
                return 0;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0)
                {
                    ModLog.Warning($"ReadRunIdFromSv: 文件为空 {fullPath}");
                    return 0;
                }
                string json;
                try
                {
                    json = SaveSystem.Unzip(bytes);
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"ReadRunIdFromSv: Unzip 失败 {fullPath}: {ex.Message}");
                    json = null;
                }
                if (!string.IsNullOrEmpty(json))
                {
                    int v = ParseIntField(json, "\"cId\":");
                    if (v != 0) return v;
                    ModLog.Info($"ReadRunIdFromSv: {Path.GetFileName(fullPath)} body cId=0，尝试 mp_rules.json fallback");
                }
                else
                {
                    ModLog.Warning($"ReadRunIdFromSv: {fullPath} Unzip 后 json 为空");
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
                    ModLog.Warning($"ReadRunIdFromSv: {rulesPath} 无 SAVEID 与 STEAM_xxx，无法解 runId");
                }
                return 0;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ReadRunIdFromSv: 处理 {fullPath} 异常 {ex.Message}");
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

        private static int ReadBiomeFromSv(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath)) return 0;
                string json = SaveSystem.Unzip(File.ReadAllBytes(fullPath));
                if (string.IsNullOrEmpty(json)) return 0;
                return ParseIntField(json, "\"biome\":");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ReadBiomeFromSv: 处理 {fullPath} 异常 {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// SaveGame 写入 biome=biomeDepth+1，TryLoadGame 直接赋给 biomeDepth。
        /// 槽位/sidecar 统一存 0-based biomeDepth；从刚写完的 save.sv 解读，避免层间过渡时内存 depth 已递增。
        /// </summary>
        private static int BiomeDepthFromSaveFile(string fullPath)
        {
            int raw = ReadBiomeFromSv(fullPath);
            if (raw <= 0)
            {
                try { return WorldGeneration.world?.biomeDepth ?? 0; }
                catch { return 0; }
            }
            try
            {
                int live = WorldGeneration.world?.biomeDepth ?? -1;
                if (live >= 0)
                {
                    if (raw == live) return live;
                    if (raw == live + 1) return live;
                }
            }
            catch { }
            return Math.Max(0, raw - 1);
        }

        /// <summary>SaveGame 之后构造上下文：层数取自磁盘 save.sv，不用内存里可能已进下一层的 biomeDepth。</summary>
        private GameContext SnapshotGameContextFromSavedGame()
        {
            var ctx = SnapshotGameContext();
            string sv = ShouldUseMpSave() ? MpSaveLocator.ResolveLocalPlayerSavePath() : GameSavePath;
            if (!string.IsNullOrEmpty(sv) && File.Exists(sv))
            {
                int depth = BiomeDepthFromSaveFile(sv);
                ctx.Biome = depth;
                int traveled = MpSaveLayerHelper.ReadTotalTraveledFromSv(sv);
                ModLog.Info($"存档层数：path={sv} biomeDepth={depth} totalTraveled={traveled} rawBiome={ReadBiomeFromSv(sv)} liveDepth={WorldGeneration.world?.biomeDepth}");
            }
            if (ShouldUseMpSave())
                MpSaveLayerHelper.NormalizeAfterSnapshot(ctx.Biome, IsPlayerAtLayerBoundary());
            return ctx;
        }

        /// <summary>玩家是否站在层底（进层点）。镜像游戏判定 body.y &lt; -halfHeight+3.1f。
        /// 游戏用「存档→读档」推进层，层底保存即进下一层，此时不可把 biome 改回当前层。</summary>
        internal static bool IsPlayerAtLayerBoundary()
        {
            try
            {
                var w = WorldGeneration.world;
                var cam = PlayerCamera.main;
                if (w == null || cam == null || cam.body == null) return false;
                float y = cam.body.transform.position.y;
                return y < (float)(0L - (long)w.halfHeight) + 3.1f;
            }
            catch { return false; }
        }

        private static void NormalizeSaveBiome(string fullPath, int biome)
        {
            try
            {
                if (!File.Exists(fullPath)) return;
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes == null || bytes.Length == 0) return;
                string json = SaveSystem.Unzip(bytes);
                if (string.IsNullOrEmpty(json)) return;
                string rewritten = ReplaceIntField(json, "biome", biome);
                if (string.Equals(rewritten, json, StringComparison.Ordinal)) return;
                File.WriteAllBytes(fullPath, SaveSystem.Zip(rewritten));
            }
            catch (Exception ex)
            {
                ModLog.Warning($"NormalizeSaveBiome 失败 {fullPath}: {ex.Message}");
            }
        }

        private static string ReplaceIntField(string json, string field, int value)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field)) return json;
            string key = "\"" + field + "\"";
            int idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return json;
            int colon = json.IndexOf(':', idx + key.Length);
            if (colon < 0) return json;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            if (end < json.Length && json[end] == '-') end++;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            if (end <= start) return json;
            return json.Substring(0, start) + value.ToString() + json.Substring(end);
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
                ModLog.Warning($"ComputeCurrentRunId: 读 WoundView.cInfo[2] 异常 {ex.Message}");
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
                    ModLog.Info($"ComputeCurrentRunId: 通过 {Path.GetFileName(path)} 解出 runId={v}");
                    return v;
                }
            }
            ModLog.Warning("ComputeCurrentRunId: 所有候选 .sv 都解不出 runId，返回 0");
            return 0;
        }

        /// <summary>当前游戏存档的 hash：多人取 mp_rules.json，单机取 save.sv；不存在返回 null。</summary>
        internal static string ComputeCurrentSaveHash()
            => MpSaveLocator.IsMultiplayerSaveActive()
                ? ComputeFileHash(MpSaveLocator.MpRulesPath)
                : ComputeFileHash(GameSavePath);

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
            bool mp = ShouldUseMpSave();
            if (!mp && !File.Exists(GameSavePath))
            {
                throw new FileNotFoundException("当前没有 save.sv 可复制");
            }

            int runId = ctx.RunId != 0 ? ctx.RunId
                : (mp ? ReadRunIdFromMpRules() : ReadRunIdFromSv(GameSavePath));
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

            if (mp) MpSaveLocator.PackMpSaveTo(dst);
            else File.Copy(GameSavePath, dst, overwrite: false);
            return dst;
        }

        /// <summary>从 mp_rules.json 的 SAVEID 解出多人存档 runId；失败返回 0。</summary>
        private static int ReadRunIdFromMpRules()
        {
            try
            {
                if (!File.Exists(MpSaveLocator.MpRulesPath)) return 0;
                string rj = File.ReadAllText(MpSaveLocator.MpRulesPath);
                return ParseIntField(rj, "\"SAVEID\":");
            }
            catch { return 0; }
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
            QolBridge.ReadCurrentSeed(out int qolSeed, out string qolInput);
            if (SeededWorldEngine.IsActive)
            {
                qolSeed = SeededWorldEngine.CurrentSeed;
                qolInput = SeededWorldEngine.InputString;
            }
            return new GameContext
            {
                Biome = biome,
                RunId = ComputeCurrentRunId(),
                PlayerX = px,
                PlayerY = py,
                HasPlayerPos = hasPos,
                QolSeed = qolSeed,
                QolSeedInput = qolInput,
                WorldEngine = ResolveCurrentPreferredEngineName(),
                ActiveLayerModifierIndex = LayerModifierSnapshot.CurrentActiveIndex(),
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
            public string WorldEngine;
            public int ActiveLayerModifierIndex;
        }

        private static string ResolveSlotsRoot()
        {
            var asmDir = Path.GetDirectoryName(typeof(SaveStore).Assembly.Location)
                          ?? Path.Combine(Paths.PluginPath, "SaveManager");
            return Path.Combine(asmDir, "slots");
        }
    }
}
