using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 主面板：黑白线条风格 + 顶部 tab（设置 / 存档）。
    /// 业务回调由 Plugin 注入；本类只管渲染、本地状态（tab / 滚动 / 输入框 / 改键）。
    /// </summary>
    internal sealed class SaveManagerWindow
    {
        private const int WindowId = 0x5A4D5310;
        private const float WindowWidth = 1440f;
        private const float WindowHeight = 900f;
        private const float TitleBarHeight = 64f;
        private const float CloseBtnSize = 52f;
        private const float LabelColW = 220f; // 设置页所有"标签：值"行的左侧标签宽
        private const float RowMinHeight = 52f;

        private readonly HotkeyConfig _cfg;
        private readonly SaveStore _store;
        private readonly Action _onSaveNow;
        private readonly Action<string> _onSaveNowAs;
        private readonly Action _onSaveAndExit;
        private readonly Action<SlotInfo> _onLoadSlot;
        private readonly Action<SlotInfo> _onDeleteSlot;
        private readonly Action _onIntervalChanged;
        private readonly Func<string> _getStatus;
        private readonly Action _onOpened;
        private readonly Action _onClosed;
        private readonly RollbackTabView _rollbackTab;

        private Rect _rect = new Rect(120f, 60f, WindowWidth, WindowHeight);
        private int _tab; // 0 = 设置 / 1 = 存档
        private Vector2 _scroll;

        // 立即保存的昵称输入
        private string _quickNickname = "";

        // 改键采集状态
        private bool _capturingSettingsKey;
        private bool _capturingSlotsKey;
        private bool _capturingQuickKey;

        // 固定世界设置输入缓存（首帧从配置初始化）
        private string _seedInputCache;
        private string _fixedXCache;
        private string _fixedYCache;

        // 卡片昵称编辑状态：path -> 当前编辑文本
        private readonly Dictionary<string, string> _editingNicknames = new Dictionary<string, string>();

        // 日期分组折叠状态：dateFolder -> 是否展开。默认全部展开。
        private readonly Dictionary<string, bool> _dateExpanded = new Dictionary<string, bool>();
        // runId 分组折叠状态：runIdFolder -> 是否展开。默认全部展开。
        private readonly Dictionary<string, bool> _runIdExpanded = new Dictionary<string, bool>();

        // 哪一条卡片处于昵称编辑态（path）。null = 没人在编辑。
        private string _nicknameEditingPath;
        private bool _loggedDrawForCurrentOpen;

        internal bool Open { get; set; }
        internal Rect WindowRect => _rect;

        /// <summary>由外部 (UI 注入按钮 / 快捷键) 打开面板。除了置 Open=true，还触发一次性副作用（如关 ESC 暂停面板防穿透）。</summary>
        internal void OpenPanel()
        {
            if (Open) return;
            Open = true;
            _loggedDrawForCurrentOpen = false;
            ModLog.Info("SaveManagerWindow.OpenPanel -> Open=true");
            _onOpened?.Invoke();
        }

        /// <summary>关闭面板并触发 onClosed 副作用（恢复被暂时禁用的 raycaster 等）。</summary>
        internal void ClosePanel()
        {
            if (!Open) return;
            Open = false;
            _loggedDrawForCurrentOpen = false;
            CancelKeyCapture();
            _onClosed?.Invoke();
        }

        /// <summary>当前光标是否在主面板矩形内（被 UIUtilBlockPatch 用来阻止穿透到游戏）。</summary>
        internal bool IsCursorOver()
        {
            if (!Open) return false;
            // IMGUI 坐标 Y 朝下，Input.mousePosition Y 朝上，需要换算
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return _rect.Contains(mouse);
        }

        internal SaveManagerWindow(HotkeyConfig cfg, SaveStore store,
            Action onSaveNow, Action<string> onSaveNowAs, Action onSaveAndExit,
            Action<SlotInfo> onLoadSlot, Action<SlotInfo> onDeleteSlot,
            Action onIntervalChanged, Func<string> getStatus, Action onOpened, Action onClosed,
            RollbackController rollback)
        {
            _cfg = cfg;
            _store = store;
            _onSaveNow = onSaveNow;
            _onSaveNowAs = onSaveNowAs;
            _onSaveAndExit = onSaveAndExit;
            _onLoadSlot = onLoadSlot;
            _onDeleteSlot = onDeleteSlot;
            _onIntervalChanged = onIntervalChanged;
            _getStatus = getStatus;
            _onOpened = onOpened;
            _onClosed = onClosed;
            _rollbackTab = new RollbackTabView(cfg, store, rollback);
        }

        internal void Draw()
        {
            if (!Open) return;
            if (!_loggedDrawForCurrentOpen)
            {
                _loggedDrawForCurrentOpen = true;
                ModLog.Info($"SaveManagerWindow.Draw active rect=({_rect.x:0},{_rect.y:0},{_rect.width:0},{_rect.height:0})");
            }
            BlackWhiteSkin.Push();
            try
            {
                // 用 ModalWindow 而非 Window：阻止外部点击穿透到下层 Canvas，
                // 并且仅 GUI.DragWindow 指定矩形可拖，标题栏外的内容区不会被拖动。
                _rect = GUI.ModalWindow(WindowId, _rect, DrawContent, "");
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SaveManagerWindow.Draw 失败：{ex}");
                ClosePanel();
            }
            finally
            {
                BlackWhiteSkin.Pop();
            }
        }

        internal void CancelKeyCapture()
        {
            _capturingSettingsKey = false;
            _capturingSlotsKey = false;
            _capturingQuickKey = false;
        }

        // —— 主体 —— //

        private void DrawContent(int id)
        {
            // 整体白色边框（6px 加粗）
            BlackWhiteSkin.DrawBorder(new Rect(0f, 0f, WindowWidth, WindowHeight), 6f);

            // 标题栏：左侧标题 + 右上角 X 关闭按钮
            GUI.Label(new Rect(28f, 14f, WindowWidth - CloseBtnSize - 56f, 40f),
                I18n.T("app.name"), BlackWhiteSkin.HeaderStyle);

            // 右上角 X 关闭按钮
            var closeRect = new Rect(WindowWidth - CloseBtnSize - 12f, 8f, CloseBtnSize, CloseBtnSize);
            if (GUI.Button(closeRect, GUIContent.none))
            {
                ClosePanel();
            }
            BlackWhiteSkin.DrawBorder(closeRect, 4f);
            BlackWhiteSkin.DrawCloseX(new Rect(closeRect.x + 13f, closeRect.y + 13f,
                closeRect.width - 26f, closeRect.height - 26f), 6f);

            // 标题栏下方分隔
            BlackWhiteSkin.DrawHLine(new Rect(0f, TitleBarHeight, WindowWidth, 4f));

            // tab 区
            DrawTabs();
            // tab 下方分隔
            BlackWhiteSkin.DrawHLine(new Rect(0f, TitleBarHeight + 84f, WindowWidth, 4f));

            // 主体（留出底部 60：状态条）
            float bodyTop = TitleBarHeight + 96f;
            float statusH = 60f;
            var bodyRect = new Rect(24f, bodyTop, WindowWidth - 48f, WindowHeight - bodyTop - statusH);
            GUILayout.BeginArea(bodyRect);
            if (_tab == 0) DrawSettingsTab();
            else if (_tab == 1) DrawSlotsTab();
            else if (_tab == 2) _rollbackTab.Draw();
            else DrawExternalTab(_tab - 3);
            GUILayout.EndArea();

            // 状态条 + 上方分隔线
            BlackWhiteSkin.DrawHLine(new Rect(0f, WindowHeight - statusH, WindowWidth, 4f));
            string status = _getStatus?.Invoke() ?? "";
            GUI.Label(new Rect(28f, WindowHeight - statusH + 16f, WindowWidth - 56f, 32f), status);

            GUI.DragWindow(new Rect(0f, 0f, WindowWidth - CloseBtnSize - 24f, TitleBarHeight));
        }

        private void DrawTabs()
        {
            float tabW = 240f, tabH = 64f, top = TitleBarHeight + 14f;
            DrawTabButton(new Rect(28f, top, tabW, tabH), I18n.T("tab.settings"), 0);
            DrawTabButton(new Rect(28f + (tabW + 16f) * 1f, top, tabW, tabH), I18n.T("tab.slots"), 1);
            DrawTabButton(new Rect(28f + (tabW + 16f) * 2f, top, tabW, tabH), I18n.T("tab.rollback"), 2);
            var ext = ExternalTabRegistry.Entries;
            for (int i = 0; i < ext.Count; i++)
            {
                DrawTabButton(new Rect(28f + (tabW + 16f) * (3 + i), top, tabW, tabH), ext[i].Title, 3 + i);
            }
        }

        private void DrawTabButton(Rect rect, string label, int idx)
        {
            var style = idx == _tab ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            if (GUI.Button(rect, label, style))
            {
                _tab = idx;
                CancelKeyCapture();
            }
        }

        private Vector2 _externalScroll;

        /// <summary>绘制第 index 个外部注册分页；委托异常被吞掉以隔离外部 mod 故障。</summary>
        private void DrawExternalTab(int index)
        {
            var ext = ExternalTabRegistry.Entries;
            if (index < 0 || index >= ext.Count)
            {
                _tab = 0;
                return;
            }
            _externalScroll = GUILayout.BeginScrollView(_externalScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            try { ext[index].Draw?.Invoke(); }
            catch (Exception ex) { GUILayout.Label("external tab error: " + ex.Message); }
            GUILayout.EndScrollView();
        }

        // —— 设置页 —— //

        private Vector2 _settingsScroll;

        private void DrawSettingsTab()
        {
            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.Space(8f);
            GUILayout.Label(I18n.T("sec.save_now"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("lbl.alias_optional"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            _quickNickname = GUILayout.TextField(_quickNickname ?? "",
                GUILayout.MinWidth(360f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(12f);
            if (GUILayout.Button(I18n.T("btn.save_now"),
                GUILayout.MinWidth(140f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                if (string.IsNullOrEmpty(_quickNickname)) _onSaveNow?.Invoke();
                else _onSaveNowAs?.Invoke(_quickNickname);
                _quickNickname = "";
            }
            GUILayout.Space(12f);
            if (GUILayout.Button(I18n.T("btn.save_exit"),
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                _onSaveAndExit?.Invoke();
                _quickNickname = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(I18n.T("sec.auto_backup"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            bool newAuto = DrawSwitch(I18n.T("sw.auto_backup_enabled"), _cfg.AutoBackupEnabled.Value);
            if (newAuto != _cfg.AutoBackupEnabled.Value)
            {
                _cfg.AutoBackupEnabled.Value = newAuto;
                if (newAuto) _onIntervalChanged?.Invoke();
            }

            DrawFloatSlider(I18n.F("fmt.interval_minutes", _cfg.AutoBackupIntervalMinutes.Value),
                _cfg.AutoBackupIntervalMinutes, 0.5f, 60f, snap: 0.5f, onChanged: _onIntervalChanged);

            DrawIntSlider(I18n.F("fmt.auto_keep_count", _cfg.AutoBackupKeep.Value),
                _cfg.AutoBackupKeep, 1, 30);

            bool newGuard = DrawSwitch(I18n.T("sw.backup_before_overwrite"), _cfg.BackupBeforeOverwrite.Value);
            if (newGuard != _cfg.BackupBeforeOverwrite.Value) _cfg.BackupBeforeOverwrite.Value = newGuard;

            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(I18n.T("sec.auto_rollback"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            bool newAutoRollback = DrawSwitch(I18n.T("sw.auto_rollback_enabled"), _cfg.AutoRollbackOnDeath.Value);
            if (newAutoRollback != _cfg.AutoRollbackOnDeath.Value) _cfg.AutoRollbackOnDeath.Value = newAutoRollback;

            DrawFloatSlider(I18n.F("fmt.rollback_delay", _cfg.RollbackDelaySeconds.Value),
                _cfg.RollbackDelaySeconds, 1f, 10f, snap: 0.5f, onChanged: null);

            DrawIntSlider(I18n.F("fmt.mp_death_threshold", _cfg.MultiplayerDeathThreshold.Value),
                _cfg.MultiplayerDeathThreshold, 1, 16);

            DrawIntSlider(I18n.F("fmt.recent_slots_limit", _cfg.RecentSlotsLimit.Value),
                _cfg.RecentSlotsLimit, 5, 100);

            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(I18n.T("sec.world"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            DrawWorldGroup();
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(I18n.T("sec.hotkeys"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            DrawHotkeyRow(I18n.T("lbl.hotkey_toggle_panel"), _cfg.ToggleSlotsHotkey, ref _capturingSlotsKey,
                () => { _capturingSettingsKey = false; _capturingQuickKey = false; });
            DrawHotkeyRow(I18n.T("lbl.hotkey_quick_save"), _cfg.QuickSaveHotkey, ref _capturingQuickKey,
                () => { _capturingSettingsKey = false; _capturingSlotsKey = false; });
            DrawHotkeyRow(I18n.T("lbl.hotkey_settings_tab"), _cfg.ToggleSettingsHotkey, ref _capturingSettingsKey,
                () => { _capturingSlotsKey = false; _capturingQuickKey = false; });
            CaptureKeyDownIfNeeded();
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.Label(I18n.T("sec.misc"), BlackWhiteSkin.HeaderStyle);
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            bool newShowLog = DrawSwitch(I18n.T("sw.show_log_in_console"), _cfg.ShowLogInConsole.Value);
            if (newShowLog != _cfg.ShowLogInConsole.Value) _cfg.ShowLogInConsole.Value = newShowLog;
            bool newAcceptUpdate = DrawSwitch(I18n.T("sw.accept_update_notice"), _cfg.AcceptUpdateNotice.Value);
            if (newAcceptUpdate != _cfg.AcceptUpdateNotice.Value)
            {
                _cfg.AcceptUpdateNotice.Value = newAcceptUpdate;
                UpdateChecker.Enabled = newAcceptUpdate;
            }
            DrawLanguageModeRow(I18n.T("lbl.language"), _cfg.PreferredLanguage);
            GUILayout.EndVertical();

            GUILayout.Space(20f);
            GUILayout.EndScrollView();
        }

        // —— 存档页 —— //

        private void DrawSlotsTab()
        {
            // 顶部固定操作条（在 ScrollView 之外）
            var slots = _store.ListSlots();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.F("fmt.total_slots", slots.Count),
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn.expand_all"),
                GUILayout.MinWidth(160f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
            {
                foreach (var s in slots)
                {
                    _runIdExpanded[s.RunIdFolder] = true;
                    _dateExpanded[RunDateKey(s)] = true;
                }
            }
            GUILayout.Space(12f);
            if (GUILayout.Button(I18n.T("btn.collapse_all"),
                GUILayout.MinWidth(160f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
            {
                foreach (var s in slots)
                {
                    _runIdExpanded[s.RunIdFolder] = false;
                    _dateExpanded[RunDateKey(s)] = false;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            // 滚动列表占满剩余空间，多日期可滚
            _scroll = GUILayout.BeginScrollView(_scroll,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // 当前存档卡片（在滚动列表里，与日期分组一起滚）
            string currentHash = SaveStore.ComputeCurrentSaveHash();
            int currentRunId = SaveStore.ComputeCurrentRunId();
            DrawCurrentSaveCard(currentHash);

            // 三层嵌套：runId → 日期 → 卡片。slots 已按修改时间倒序，分组时保持遇到顺序即可。
            string lastRunId = null;
            string lastDate = null;
            foreach (var slot in slots)
            {
                if (slot.RunIdFolder != lastRunId)
                {
                    lastRunId = slot.RunIdFolder;
                    lastDate = null;
                    int runCount = 0;
                    foreach (var s in slots) if (s.RunIdFolder == lastRunId) runCount++;
                    if (!_runIdExpanded.ContainsKey(lastRunId)) _runIdExpanded[lastRunId] = true;
                    DrawRunIdHeader(lastRunId, runCount);
                }
                if (!_runIdExpanded[lastRunId]) continue;

                if (slot.DateFolder != lastDate)
                {
                    lastDate = slot.DateFolder;
                    string dateKey = RunDateKey(slot);
                    int dateCount = 0;
                    foreach (var s in slots) if (s.RunIdFolder == lastRunId && s.DateFolder == lastDate) dateCount++;
                    if (!_dateExpanded.ContainsKey(dateKey)) _dateExpanded[dateKey] = true;
                    DrawDateHeader(lastDate, dateKey, dateCount);
                }
                if (!_dateExpanded[RunDateKey(slot)]) continue;

                // 当前存档判定：runId 必须匹配 + 文件 hash 一致。
                // 单看 hash 会出现 "跨 run 但内容相同" 的存档被误标当前——比如旧 cInfo 缓存下手动保存。
                bool isCurrent = currentHash != null
                    && currentRunId != 0
                    && slot.Sidecar.RunId == currentRunId
                    && slot.Hash == currentHash;
                DrawSlotCard(slot, isCurrent);
            }
            if (slots.Count == 0)
            {
                GUILayout.Space(20f);
                GUILayout.Label(I18n.T("msg.no_slots"));
            }
            GUILayout.EndScrollView();
        }

        /// <summary>日期折叠状态的复合 key：runId 加日期。同一日期在不同 runId 下独立折叠。</summary>
        private static string RunDateKey(SlotInfo s) => s.RunIdFolder + "/" + s.DateFolder;

        private void DrawCurrentSaveCard(string currentHash)
        {
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("lbl.current_save"), BlackWhiteSkin.HeaderStyle,
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f));
            GUILayout.Label(SaveStore.CurrentSaveDisplayPath, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
            GUILayout.EndHorizontal();
            int runId = SaveStore.ComputeCurrentRunId();
            string sub;
            if (SaveStore.IsMultiplayerContextActive())
                sub = runId != 0 ? I18n.F("fmt.run_id", runId) : I18n.T("mp.current_save_hint");
            else
                sub = currentHash == null
                    ? I18n.T("msg.no_save_yet")
                    : (runId == 0 ? I18n.T("msg.run_id_unknown") : I18n.F("fmt.run_id", runId));
            GUILayout.Label(sub);
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private static string FormatSysTime(long unixMs)
        {
            if (unixMs <= 0L) return I18n.T("lbl.time_unknown");
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void DrawRunIdHeader(string runIdFolder, int count)
        {
            GUILayout.Space(10f);
            string prefix = _runIdExpanded[runIdFolder] ? "▼  " : "▶  ";
            string label = runIdFolder == "0"
                ? prefix + I18n.F("fmt.adventure_unknown", count)
                : prefix + I18n.F("fmt.adventure_named", runIdFolder, count);
            if (GUILayout.Button(label,
                BlackWhiteSkin.HeaderButtonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(56f)))
            {
                _runIdExpanded[runIdFolder] = !_runIdExpanded[runIdFolder];
            }
            GUILayout.Space(4f);
        }

        private void DrawDateHeader(string dateFolder, string dateKey, int count)
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(28f); // 二级缩进
            string prefix = _dateExpanded[dateKey] ? "▼  " : "▶  ";
            if (GUILayout.Button(prefix + I18n.F("fmt.date_count", dateFolder, count),
                BlackWhiteSkin.HeaderButtonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(44f)))
            {
                _dateExpanded[dateKey] = !_dateExpanded[dateKey];
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void DrawSlotCard(SlotInfo slot, bool isCurrent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(56f); // 三级缩进，呈现嵌套层级
            GUILayout.BeginVertical(BlackWhiteSkin.CardStyle);

            // 第一行：标签 + 别名（含铅笔编辑） + 操作按钮区
            GUILayout.BeginHorizontal();
            string tag = slot.Sidecar.Pinned ? I18n.T("lbl.tag_pinned")
                : (slot.Sidecar.IsAuto ? I18n.T("lbl.tag_auto") : I18n.T("lbl.tag_manual"));
            GUILayout.Label(tag,
                GUILayout.MinWidth(100f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f));

            string key = slot.FullSlotPath;
            bool editing = _nicknameEditingPath == key;
            if (!_editingNicknames.ContainsKey(key)) _editingNicknames[key] = slot.Sidecar.Nickname;

            if (editing)
            {
                _editingNicknames[key] = GUILayout.TextField(_editingNicknames[key] ?? "",
                    GUILayout.MinWidth(360f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
                if (GUILayout.Button("✓", BlackWhiteSkin.IconBtnStyle,
                    GUILayout.Width(40f), GUILayout.MinHeight(36f)))
                {
                    slot.Sidecar.Nickname = _editingNicknames[key] ?? "";
                    _store.UpdateSidecar(slot.FullSlotPath, slot.Sidecar);
                    _nicknameEditingPath = null;
                }
                if (GUILayout.Button("×", BlackWhiteSkin.IconBtnStyle,
                    GUILayout.Width(40f), GUILayout.MinHeight(36f)))
                {
                    _editingNicknames[key] = slot.Sidecar.Nickname;
                    _nicknameEditingPath = null;
                }
            }
            else
            {
                GUILayout.Label(I18n.F("fmt.alias",
                        string.IsNullOrEmpty(slot.Sidecar.Nickname) ? I18n.T("lbl.alias_unset") : slot.Sidecar.Nickname),
                    BlackWhiteSkin.HeaderStyle,
                    GUILayout.MinWidth(360f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f));
                Rect penBtn = GUILayoutUtility.GetRect(40f, 36f, GUILayout.Width(40f), GUILayout.MinHeight(36f));
                if (GUI.Button(penBtn, GUIContent.none, BlackWhiteSkin.IconBtnStyle))
                {
                    _nicknameEditingPath = key;
                }
                BlackWhiteSkin.DrawPencil(new Rect(penBtn.x + 8f, penBtn.y + 8f,
                    penBtn.width - 16f, penBtn.height - 16f), 2f);
            }

            GUILayout.EndHorizontal();

            // 第二行：文件名 + 时间 / 大小 / 当前标识
            GUILayout.BeginHorizontal();
            GUILayout.Label("  " + slot.File.Name,
                GUILayout.MinWidth(360f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(28f));
            GUILayout.FlexibleSpace();
            if (isCurrent)
            {
                GUILayout.Label(I18n.T("lbl.is_current_save"),
                    GUILayout.MinWidth(140f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(28f));
            }
            string playTime = I18n.F("fmt.last_played", FormatSysTime(slot.Sidecar.LastPlayedAtUnixMs));
            GUILayout.Label(playTime,
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(28f));
            GUILayout.Label(slot.File.LastWriteTime.ToString("HH:mm:ss")
                + "  ·  " + (slot.File.Length / 1024f).ToString("0.0") + " KB",
                GUILayout.MinWidth(220f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(28f));
            GUILayout.EndHorizontal();

            // 第三行：操作按钮区（切换 / 标记 / 删除）。当前存档隐藏切换按钮。
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (!isCurrent)
            {
                if (GUILayout.Button(I18n.T("btn.load_slot"),
                    GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
                {
                    _onLoadSlot?.Invoke(slot);
                }
                GUILayout.Space(8f);
            }
            string pinLabel = slot.Sidecar.Pinned ? I18n.T("btn.unpin") : I18n.T("btn.pin");
            if (GUILayout.Button(pinLabel,
                GUILayout.MinWidth(160f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
            {
                slot.Sidecar.Pinned = !slot.Sidecar.Pinned;
                _store.UpdateSidecar(slot.FullSlotPath, slot.Sidecar);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.T("btn.delete"),
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(36f)))
            {
                _onDeleteSlot?.Invoke(slot);
                _editingNicknames.Remove(key);
                if (_nicknameEditingPath == key) _nicknameEditingPath = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        // —— 控件 helpers —— //

        /// <summary>固定世界设置组：引擎二选一 + 种子输入 + 位置模式二选一 + 固定坐标。改值即写配置并同步仲裁器。</summary>
        private void DrawWorldGroup()
        {
            bool engineSelf = string.Equals(_cfg.PreferredEngine.Value, "self", StringComparison.OrdinalIgnoreCase);
            bool qolPresent = QolBridge.IsQolPresent();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("lbl.world_engine"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && qolPresent;
            if (GUILayout.Button(I18n.T("opt.engine_qol"), engineSelf ? BlackWhiteSkin.TabStyle : BlackWhiteSkin.TabActiveStyle,
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                _cfg.PreferredEngine.Value = "qol";
            }
            GUI.enabled = prevEnabled;
            GUILayout.Space(8f);
            if (GUILayout.Button(I18n.T("opt.engine_self"), engineSelf ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                _cfg.PreferredEngine.Value = "self";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!qolPresent)
            {
                GUILayout.Label(I18n.T("hint.qol_absent"));
            }
            GUILayout.Space(6f);

            _seedInputCache = _seedInputCache ?? _cfg.SeedInput.Value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("lbl.seed_input"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            string newSeed = GUILayout.TextField(_seedInputCache ?? "",
                GUILayout.MinWidth(280f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            if (newSeed != _seedInputCache)
            {
                _seedInputCache = newSeed;
                _cfg.SeedInput.Value = newSeed;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            bool posFixed = string.Equals(_cfg.PositionMode.Value, "fixedPos", StringComparison.OrdinalIgnoreCase);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("lbl.pos_mode"),
                GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            if (GUILayout.Button(I18n.T("opt.pos_last"), posFixed ? BlackWhiteSkin.TabStyle : BlackWhiteSkin.TabActiveStyle,
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                _cfg.PositionMode.Value = "lastPos";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(I18n.T("opt.pos_fixed"), posFixed ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(180f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                _cfg.PositionMode.Value = "fixedPos";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (posFixed)
            {
                _fixedXCache = _fixedXCache ?? _cfg.FixedX.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _fixedYCache = _fixedYCache ?? _cfg.FixedY.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DrawFloatField(I18n.T("lbl.fixed_x"), ref _fixedXCache, _cfg.FixedX);
                DrawFloatField(I18n.T("lbl.fixed_y"), ref _fixedYCache, _cfg.FixedY);
            }

            WorldEngineArbiter.SyncPreference(_cfg.PreferredEngine.Value, _cfg.SeedInput.Value,
                _cfg.PositionMode.Value, _cfg.FixedX.Value, _cfg.FixedY.Value);
        }

        private static void DrawFloatField(string label, ref string cache, ConfigEntry<float> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight));
            string next = GUILayout.TextField(cache ?? "",
                GUILayout.MinWidth(200f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowMinHeight));
            if (next != cache)
            {
                cache = next;
                if (float.TryParse(next, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    entry.Value = v;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        /// <summary>左右开关：标签 + [开] / [关] 两按钮，当前态加白色边框高亮。</summary>
        private static bool DrawSwitch(string label, bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);

            bool result = value;

            var onStyle = value ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle;
            var offStyle = value ? BlackWhiteSkin.TabStyle : BlackWhiteSkin.TabActiveStyle;

            if (GUILayout.Button(I18n.T("sw.on"), onStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                result = true;
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(I18n.T("sw.off"), offStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                result = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            return result;
        }

        private static void DrawLanguageModeRow(string label, ConfigEntry<string> entry)
        {
            string mode = NormalizeLanguageMode(entry.Value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(RowMinHeight));
            GUILayout.Space(20f);
            if (GUILayout.Button(I18n.T("opt.language_auto"),
                mode == "auto" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(140f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "auto";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(I18n.T("opt.language_zh"),
                mode == "zh" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "zh";
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(I18n.T("opt.language_en"),
                mode == "en" ? BlackWhiteSkin.TabActiveStyle : BlackWhiteSkin.TabStyle,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = "en";
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private static string NormalizeLanguageMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "auto";
            mode = mode.Trim().ToLowerInvariant();
            return mode == "zh" || mode == "en" ? mode : "auto";
        }

        private static void DrawFloatSlider(string label, ConfigEntry<float> entry, float min, float max,
            float snap, Action onChanged)
        {
            GUILayout.Label(label, GUILayout.MinHeight(36f));
            float next = GUILayout.HorizontalSlider(entry.Value, min, max,
                GUILayout.Height(44f), GUILayout.ExpandWidth(true));
            if (snap > 0f) next = Mathf.Round(next / snap) * snap;
            if (Mathf.Abs(next - entry.Value) > 0.001f)
            {
                entry.Value = Mathf.Clamp(next, min, max);
                onChanged?.Invoke();
            }
            GUILayout.Space(12f);
        }

        private static void DrawIntSlider(string label, ConfigEntry<int> entry, int min, int max)
        {
            GUILayout.Label(label, GUILayout.MinHeight(36f));
            int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max,
                GUILayout.Height(44f), GUILayout.ExpandWidth(true)));
            if (next != entry.Value) entry.Value = Mathf.Clamp(next, min, max);
            GUILayout.Space(12f);
        }

        private void DrawHotkeyRow(string label, ConfigEntry<KeyboardShortcut> entry,
            ref bool capturing, Action onStart)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.MinWidth(LabelColW), GUILayout.ExpandWidth(false),
                GUILayout.MinHeight(RowMinHeight));
            string text = capturing
                ? I18n.T("btn.press_a_key")
                : (HotkeyConfig.IsBound(entry.Value) ? entry.Value.ToString() : I18n.T("btn.unbound"));
            if (GUILayout.Button(text,
                GUILayout.MinWidth(280f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                capturing = true;
                onStart();
            }
            GUILayout.Space(12f);
            if (GUILayout.Button(I18n.T("btn.clear"),
                GUILayout.MinWidth(96f), GUILayout.ExpandWidth(false), GUILayout.MinHeight(RowMinHeight)))
            {
                entry.Value = new KeyboardShortcut(KeyCode.None);
                capturing = false;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void CaptureKeyDownIfNeeded()
        {
            if (!(_capturingSettingsKey || _capturingSlotsKey || _capturingQuickKey)) return;
            var ev = Event.current;
            if (ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None) return;
            if (ev.keyCode == KeyCode.Escape)
            {
                CancelKeyCapture();
                ev.Use();
                return;
            }
            var sc = new KeyboardShortcut(ev.keyCode);
            if (_capturingSettingsKey) _cfg.ToggleSettingsHotkey.Value = sc;
            else if (_capturingSlotsKey) _cfg.ToggleSlotsHotkey.Value = sc;
            else if (_capturingQuickKey) _cfg.QuickSaveHotkey.Value = sc;
            CancelKeyCapture();
            ev.Use();
        }
    }
}
