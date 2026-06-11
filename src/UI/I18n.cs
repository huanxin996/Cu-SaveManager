using System.Collections.Generic;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 极简 i18n：两套字典（zh-CN / EN），按 Locale.currentLangName 自动选择。
    /// 游戏切换语言时会 LoadScene 重启，所以不需要订阅事件，每次 t() 都按当前值判定。
    /// 找不到 key 返回 key 本身作为可见 fallback。
    /// </summary>
    internal static class I18n
    {
        private static readonly string[] ChineseKeywords =
        {
            "中文", "汉化", "简中", "简体", "繁中", "繁体", "繁體", "chinese"
        };

        private static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
        {
            // 通用
            ["app.name"] = "存档管理",
            ["app.menu_button"] = "存档管理",
            // tab
            ["tab.settings"] = "设置",
            ["tab.slots"] = "存档",
            ["tab.rollback"] = "回档",
            // 状态条
            ["status.save_path"] = "save.sv 路径：{0}",
            // 模式 / 诊断
            ["mode.singleplayer"] = "单机",
            ["mode.multiplayer"] = "多人会话",
            ["fmt.save_path_diag"] = "存档路径决策：模式={0}，选定={1}，mp_save 存在={2}，单机 save.sv 存在={3}",
            // 设置：立即保存
            ["sec.save_now"] = "立即保存",
            ["lbl.alias_optional"] = "别名（可选）：",
            ["btn.save_now"] = "立即保存",
            ["btn.save_exit"] = "存档并退出",
            // 设置：自动备份
            ["sec.auto_backup"] = "自动备份",
            ["sw.auto_backup_enabled"] = "启用定时备份",
            ["sw.backup_before_overwrite"] = "切回槽位前先备份当前 save.sv",
            ["fmt.interval_minutes"] = "间隔（分钟）：{0:0.#}",
            ["fmt.auto_keep_count"] = "每个冒险保留自动槽：{0}",
            // 设置：死亡回档
            ["sec.auto_rollback"] = "死亡自动回档",
            ["sw.auto_rollback_enabled"] = "启用死亡自动回档",
            ["fmt.rollback_delay"] = "倒计时秒数：{0:0.#}",
            ["fmt.mp_death_threshold"] = "多人模式触发死亡人数：{0}",
            ["fmt.recent_slots_limit"] = "回档 tab 显示最近条数：{0}",
            // 设置：固定世界
            ["sec.world"] = "固定世界生成",
            ["lbl.world_engine"] = "生成引擎：",
            ["opt.engine_qol"] = "QoL（优先）",
            ["opt.engine_self"] = "本 mod",
            ["hint.qol_absent"] = "未检测到 QoL，仅可用本 mod 引擎",
            ["sec.world_mp"] = "多人固定世界",
            ["lbl.mp_world_engine"] = "多人生成引擎：",
            ["opt.engine_krok"] = "KrokMP（优先）",
            ["hint.mp_krok_engine"] = "KrokMP 模式：存档与位置由联机 mod 立刻保存 + 继续游戏恢复",
            ["lbl.seed_input"] = "种子（留空自动派生）：",
            ["lbl.pos_mode"] = "回档位置：",
            ["opt.pos_last"] = "上次位置",
            ["opt.pos_fixed"] = "固定位置",
            ["lbl.fixed_x"] = "固定 X：",
            ["lbl.fixed_y"] = "固定 Y：",
            ["msg.engine_qol_missing"] = "未检测到 QoL，将使用本 mod 引擎",
            // 多人回档
            ["mp.only_host_can_rollback"] = "仅主机可执行多人回档",
            ["mp.rollback_returning_menu"] = "多人回档：正在让全员返回主菜单…",
            ["mp.rollback_reloading"] = "多人回档：主机正在重新加载存档…",
            ["mp.client_skip_autosave"] = "子端模式：跳过自动备份（仅主机持有权威存档）",
            ["mp.client_cannot_save"] = "子端无法保存存档：多人存档由主机管理",
            ["mp.current_save_hint"] = "  多人模式（mp_save 由主机在保存时生成）",
            ["mp.slot_needs_mp_session"] = "该槽位是多人存档，需在多人会话中回档",
            ["mp.slot_is_singleplayer"] = "该槽位是单人存档，无法在多人会话中回档",
            ["mp.mod_detected"] = "已检测到多人 mod (KrokMP)，多人存档将按 mp_save 目录管理",
            ["mp.mod_not_detected"] = "未检测到多人 mod，按单机存档管理",
            ["qol.mod_detected"] = "已检测到 QoL，固定世界默认交给 QoL（可在面板切到本 mod 引擎）",
            ["qol.mod_not_detected"] = "未检测到 QoL，固定世界使用本 mod 引擎",
            // 设置：快捷键
            ["sec.hotkeys"] = "快捷键",
            ["lbl.hotkey_toggle_panel"] = "打开 / 关闭本面板：",
            ["lbl.hotkey_quick_save"] = "立即保存：",
            ["lbl.hotkey_settings_tab"] = "跳转到设置 tab：",
            ["btn.unbound"] = "未绑定",
            // 设置：杂项
            ["sec.misc"] = "其他",
            ["lbl.language"] = "界面语言：",
            ["opt.language_auto"] = "跟随游戏",
            ["opt.language_zh"] = "中文",
            ["opt.language_en"] = "English",
            ["sw.show_log_in_console"] = "在游戏控制台显示模组日志",
            ["sw.accept_update_notice"] = "接受新版本更新提示",
            ["sw.suppress_starting_supplies_on_load"] = "读档时不重发起始物资",
            ["update.available"] = "SaveManager 有新版本：{0}（点击打开 release 页）",
            ["btn.press_a_key"] = "请按下新按键…",
            ["btn.clear"] = "清除",
            // 开关
            ["sw.on"] = "◀ 开",
            ["sw.off"] = "关 ▶",
            // 存档 tab
            ["fmt.total_slots"] = "当前共有槽位：{0}",
            ["btn.expand_all"] = "全部展开",
            ["btn.collapse_all"] = "全部折叠",
            ["msg.no_slots"] = "  还没有槽位。在游戏内按下立即保存或开启自动备份。",
            ["lbl.current_save"] = "[当前存档]",
            ["msg.no_save_yet"] = "  还没有 save.sv（在游戏内推进进度后才会出现）",
            ["fmt.run_id"] = "  Run ID = #{0}",
            ["msg.run_id_unknown"] = "  Run ID = 未识别（save.sv 解析失败）",
            ["fmt.adventure_named"] = "冒险 #{0}    （{1}）",
            ["fmt.adventure_unknown"] = "未识别冒险    （{0}）",
            ["fmt.date_count"] = "{0}    （{1}）",
            // 卡片
            ["lbl.tag_pinned"] = "[已标记]",
            ["lbl.tag_auto"] = "[自动]",
            ["lbl.tag_manual"] = "[手动]",
            ["fmt.alias"] = "别名：{0}",
            ["lbl.alias_unset"] = "(未设置)",
            ["lbl.is_current_save"] = "【当前存档】",
            ["fmt.last_played"] = "最后游玩 {0}",
            ["lbl.time_unknown"] = "未记录",
            ["btn.load_slot"] = "切换到此存档",
            ["btn.unpin"] = "取消标记",
            ["btn.pin"] = "标记保留",
            ["btn.delete"] = "删除",
            // 回档 tab
            ["fmt.status_idle"] = "状态：空闲。当前没有进行中的回档任务。",
            ["fmt.status_idle_with_error"] = "状态：空闲。上次结果：{0}",
            ["fmt.status_counting"] = "状态：将在 {0} 秒后回档至 {1}",
            ["status.executing"] = "状态：正在加载存档场景...",
            ["btn.cancel_rollback"] = "取消回档",
            ["lbl.date_filter"] = "日期筛选：",
            ["btn.filter_all"] = "全部",
            ["btn.filter_all_active"] = "[全部]",
            ["btn.rollback"] = "回档",
            ["msg.no_rollback_targets"] = "  没有可回档的槽位。请在游戏中保存或开启自动备份。",
            ["msg.no_rollback_targets_filtered"] = "  当前日期筛选下没有槽位。",
            // 业务 / 提示
            ["fmt.saved_to"] = "已保存：{0}（来源 {1}）",
            ["fmt.save_failed"] = "立即保存失败：{0}",
            ["fmt.auto_backup_done"] = "自动备份：{0}",
            ["fmt.auto_backup_failed"] = "自动备份失败：{0}",
            ["fmt.save_exit_failed"] = "存档并退出失败：{0}",
            ["fmt.restored_to"] = "已切回 {0}。请回主菜单点 Load 让游戏读取。",
            ["fmt.restore_failed"] = "切回槽位失败：{0}",
            ["fmt.deleted"] = "已删除 {0}",
            ["fmt.delete_failed"] = "删除槽位失败：{0}",
            ["fmt.rollback_in_seconds"] = "将在 {0} 秒后回档至：{1}",
            ["msg.rollback_canceled"] = "回档已取消",
            ["msg.rollback_loading"] = "回档中...",
            ["msg.save_exit_unavailable"] = "当前不在可存档的游戏场景中",
            ["msg.save_layer_transition"] = "世界正在进入下一层，请稍后再存档",
            ["msg.save_skip_layer_transition"] = "世界进层中，跳过本次自动备份",
            ["msg.save_exit_singleplayer_only"] = "存档并退出目前仅支持单机",
            ["msg.save_exit_done"] = "已保存当前进度并返回主菜单",
            ["msg.save_exit_mp_done"] = "已保存多人存档并返回主菜单（主菜单继续游戏可恢复）",
            ["fmt.rollback_failed"] = "回档失败：{0}",
            ["msg.rollback_no_target"] = "无可用备份，无法自动回档",
            ["msg.rollback_target_missing"] = "目标槽位不存在或已损坏",
            ["msg.user_cancel"] = "用户取消",
            ["lbl.before_load_alias"] = "切回前自动备份",
        };

        private static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            ["app.name"] = "Save Manager",
            ["app.menu_button"] = "Save Manager",
            ["tab.settings"] = "Settings",
            ["tab.slots"] = "Slots",
            ["tab.rollback"] = "Rollback",
            ["status.save_path"] = "save.sv path: {0}",
            ["mode.singleplayer"] = "singleplayer",
            ["mode.multiplayer"] = "multiplayer session",
            ["fmt.save_path_diag"] = "save path decision: mode={0}, chosen={1}, mp_save exists={2}, vanilla save.sv exists={3}",
            ["sec.save_now"] = "Save Now",
            ["lbl.alias_optional"] = "Alias (optional):",
            ["btn.save_now"] = "Save Now",
            ["btn.save_exit"] = "Save && Exit",
            ["sec.auto_backup"] = "Auto Backup",
            ["sw.auto_backup_enabled"] = "Enable timed backup",
            ["sw.backup_before_overwrite"] = "Backup current save.sv before restore",
            ["fmt.interval_minutes"] = "Interval (minutes): {0:0.#}",
            ["fmt.auto_keep_count"] = "Auto slots per run: {0}",
            ["sec.auto_rollback"] = "Auto Rollback on Death",
            ["sw.auto_rollback_enabled"] = "Enable auto rollback on death",
            ["fmt.rollback_delay"] = "Countdown seconds: {0:0.#}",
            ["fmt.mp_death_threshold"] = "Multiplayer death threshold: {0}",
            ["fmt.recent_slots_limit"] = "Recent slots shown in Rollback tab: {0}",
            ["sec.world"] = "Deterministic World",
            ["lbl.world_engine"] = "Engine:",
            ["opt.engine_qol"] = "QoL (preferred)",
            ["opt.engine_self"] = "This mod",
            ["hint.qol_absent"] = "QoL not detected; only this mod's engine is available",
            ["sec.world_mp"] = "Multiplayer Deterministic World",
            ["lbl.mp_world_engine"] = "MP engine:",
            ["opt.engine_krok"] = "KrokMP (preferred)",
            ["hint.mp_krok_engine"] = "KrokMP mode: save/position restored via native save + continue game",
            ["lbl.seed_input"] = "Seed (blank = auto):",
            ["lbl.pos_mode"] = "Respawn position:",
            ["opt.pos_last"] = "Last position",
            ["opt.pos_fixed"] = "Fixed position",
            ["lbl.fixed_x"] = "Fixed X:",
            ["lbl.fixed_y"] = "Fixed Y:",
            ["msg.engine_qol_missing"] = "QoL not detected, using this mod's engine",
            ["mp.only_host_can_rollback"] = "Only the host can perform multiplayer rollback",
            ["mp.rollback_returning_menu"] = "MP rollback: returning everyone to main menu...",
            ["mp.rollback_reloading"] = "MP rollback: host is reloading the save...",
            ["mp.client_skip_autosave"] = "Client mode: skipping auto backup (only host holds authoritative save)",
            ["mp.client_cannot_save"] = "Client cannot save: multiplayer saves are managed by the host",
            ["mp.current_save_hint"] = "  Multiplayer (mp_save is generated by host on save)",
            ["mp.slot_needs_mp_session"] = "This slot is a multiplayer save; roll back inside a multiplayer session",
            ["mp.slot_is_singleplayer"] = "This slot is a singleplayer save; cannot roll back during a multiplayer session",
            ["mp.mod_detected"] = "Multiplayer mod (KrokMP) detected; MP saves managed as mp_save directory",
            ["mp.mod_not_detected"] = "No multiplayer mod detected; managing singleplayer saves",
            ["qol.mod_detected"] = "QoL detected; deterministic world defers to QoL (switch to this mod's engine in panel)",
            ["qol.mod_not_detected"] = "QoL not detected; using this mod's deterministic world engine",
            ["sec.hotkeys"] = "Hotkeys",
            ["lbl.hotkey_toggle_panel"] = "Open / close panel:",
            ["lbl.hotkey_quick_save"] = "Save now:",
            ["lbl.hotkey_settings_tab"] = "Jump to Settings tab:",
            ["btn.unbound"] = "Unbound",
            ["sec.misc"] = "Misc",
            ["lbl.language"] = "UI language:",
            ["opt.language_auto"] = "Follow game",
            ["opt.language_zh"] = "Chinese",
            ["opt.language_en"] = "English",
            ["sw.show_log_in_console"] = "Show mod logs in game console",
            ["sw.accept_update_notice"] = "Accept update notifications",
            ["sw.suppress_starting_supplies_on_load"] = "Skip starting supplies on load",
            ["update.available"] = "SaveManager update available: {0} (click to open release page)",
            ["btn.press_a_key"] = "Press a new key...",
            ["btn.clear"] = "Clear",
            ["sw.on"] = "◀ ON",
            ["sw.off"] = "OFF ▶",
            ["fmt.total_slots"] = "Total slots: {0}",
            ["btn.expand_all"] = "Expand all",
            ["btn.collapse_all"] = "Collapse all",
            ["msg.no_slots"] = "  No slots yet. Save in-game or enable auto backup.",
            ["lbl.current_save"] = "[Current save]",
            ["msg.no_save_yet"] = "  No save.sv yet (will appear after progressing in-game).",
            ["fmt.run_id"] = "  Run ID = #{0}",
            ["msg.run_id_unknown"] = "  Run ID = unknown (save.sv parse failed)",
            ["fmt.adventure_named"] = "Run #{0}    ({1})",
            ["fmt.adventure_unknown"] = "Unknown run    ({0})",
            ["fmt.date_count"] = "{0}    ({1})",
            ["lbl.tag_pinned"] = "[Pinned]",
            ["lbl.tag_auto"] = "[Auto]",
            ["lbl.tag_manual"] = "[Manual]",
            ["fmt.alias"] = "Alias: {0}",
            ["lbl.alias_unset"] = "(unset)",
            ["lbl.is_current_save"] = "[Current save]",
            ["fmt.last_played"] = "Last played {0}",
            ["lbl.time_unknown"] = "unknown",
            ["btn.load_slot"] = "Load this slot",
            ["btn.unpin"] = "Unpin",
            ["btn.pin"] = "Pin",
            ["btn.delete"] = "Delete",
            ["fmt.status_idle"] = "Status: idle. No rollback in progress.",
            ["fmt.status_idle_with_error"] = "Status: idle. Last result: {0}",
            ["fmt.status_counting"] = "Status: rolling back to {1} in {0}s",
            ["status.executing"] = "Status: loading save scene...",
            ["btn.cancel_rollback"] = "Cancel rollback",
            ["lbl.date_filter"] = "Date filter:",
            ["btn.filter_all"] = "All",
            ["btn.filter_all_active"] = "[All]",
            ["btn.rollback"] = "Roll back",
            ["msg.no_rollback_targets"] = "  No rollback targets. Save in-game or enable auto backup.",
            ["msg.no_rollback_targets_filtered"] = "  No slots match the current date filter.",
            ["fmt.saved_to"] = "Saved: {0} (from {1})",
            ["fmt.save_failed"] = "Save failed: {0}",
            ["fmt.auto_backup_done"] = "Auto backup: {0}",
            ["fmt.auto_backup_failed"] = "Auto backup failed: {0}",
            ["fmt.save_exit_failed"] = "Save & exit failed: {0}",
            ["fmt.restored_to"] = "Restored {0}. Return to main menu and click Load.",
            ["fmt.restore_failed"] = "Restore failed: {0}",
            ["fmt.deleted"] = "Deleted {0}",
            ["fmt.delete_failed"] = "Delete failed: {0}",
            ["fmt.rollback_in_seconds"] = "Rolling back in {0}s to: {1}",
            ["msg.rollback_canceled"] = "Rollback canceled",
            ["msg.rollback_loading"] = "Rolling back...",
            ["msg.save_exit_unavailable"] = "Save & exit is unavailable in the current scene",
            ["msg.save_layer_transition"] = "World is descending to the next layer; save again after it finishes",
            ["msg.save_skip_layer_transition"] = "Layer transition in progress; skipped auto-backup",
            ["msg.save_exit_singleplayer_only"] = "Save & exit currently supports singleplayer only",
            ["msg.save_exit_done"] = "Saved current progress and returned to main menu",
            ["msg.save_exit_mp_done"] = "Multiplayer save written; returned to main menu (use Continue to resume)",
            ["fmt.rollback_failed"] = "Rollback failed: {0}",
            ["msg.rollback_no_target"] = "No backup available, cannot auto-rollback",
            ["msg.rollback_target_missing"] = "Target slot missing or corrupted",
            ["msg.user_cancel"] = "User canceled",
            ["lbl.before_load_alias"] = "Auto backup before restore",
        };

        /// <summary>当前语言判定：支持自定义中文包名称关键词，并允许配置强制切换 zh/en。</summary>
        private static Dictionary<string, string> Current
        {
            get
            {
                return UseChinese() ? _zh : _en;
            }
        }

        private static bool UseChinese()
        {
            switch (NormalizeLanguageMode(Plugin.PreferredLanguageMode))
            {
                case "zh":
                    return true;
                case "en":
                    return false;
                default:
                    return IsChineseLocaleName(ReadCurrentLanguageName());
            }
        }

        private static string ReadCurrentLanguageName()
        {
            string name = null;
            try { name = Locale.currentLangName; } catch { }
            if (string.IsNullOrEmpty(name))
            {
                try { name = PlayerPrefs.GetString("locale"); } catch { }
            }
            return name;
        }

        private static string NormalizeLanguageMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "auto";
            mode = mode.Trim().ToLowerInvariant();
            return mode == "zh" || mode == "en" ? mode : "auto";
        }

        private static bool IsChineseLocaleName(string name)
        {
            string normalized = StripRichText(name).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            if (normalized.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("WC", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (int i = 0; i < ChineseKeywords.Length; i++)
            {
                if (normalized.IndexOf(ChineseKeywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string StripRichText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('<') < 0)
            {
                return value ?? string.Empty;
            }

            var sb = new System.Text.StringBuilder(value.Length);
            bool inTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '<')
                {
                    inTag = true;
                    continue;
                }
                if (ch == '>')
                {
                    inTag = false;
                    continue;
                }
                if (!inTag)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        internal static string T(string key)
        {
            if (Current.TryGetValue(key, out var v)) return v;
            // 找不到 key 时回落到 key 本身做可见占位
            return key;
        }

        internal static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }
    }
}
