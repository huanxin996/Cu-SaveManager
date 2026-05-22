using System.Collections.Generic;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 游戏切换语言时会 LoadScene 重启，所以不需要订阅事件，每次 t() 都按当前值判定。
    /// 找不到 key 返回 key 本身作为可见 fallback。
    /// </summary>
    internal static class I18n
    {
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
            // 设置：立即保存
            ["sec.save_now"] = "立即保存",
            ["lbl.alias_optional"] = "别名（可选）：",
            ["btn.save_now"] = "立即保存",
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
            // 设置：快捷键
            ["sec.hotkeys"] = "快捷键",
            ["lbl.hotkey_toggle_panel"] = "打开 / 关闭本面板：",
            ["lbl.hotkey_quick_save"] = "立即保存：",
            ["lbl.hotkey_settings_tab"] = "跳转到设置 tab：",
            ["btn.unbound"] = "未绑定",
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
            ["fmt.restored_to"] = "已切回 {0}。请回主菜单点 Load 让游戏读取。",
            ["fmt.restore_failed"] = "切回槽位失败：{0}",
            ["fmt.deleted"] = "已删除 {0}",
            ["fmt.delete_failed"] = "删除槽位失败：{0}",
            ["fmt.rollback_in_seconds"] = "将在 {0} 秒后回档至：{1}",
            ["msg.rollback_canceled"] = "回档已取消",
            ["msg.rollback_loading"] = "回档中...",
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
            ["sec.save_now"] = "Save Now",
            ["lbl.alias_optional"] = "Alias (optional):",
            ["btn.save_now"] = "Save Now",
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
            ["sec.hotkeys"] = "Hotkeys",
            ["lbl.hotkey_toggle_panel"] = "Open / close panel:",
            ["lbl.hotkey_quick_save"] = "Save now:",
            ["lbl.hotkey_settings_tab"] = "Jump to Settings tab:",
            ["btn.unbound"] = "Unbound",
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
            ["fmt.restored_to"] = "Restored {0}. Return to main menu and click Load.",
            ["fmt.restore_failed"] = "Restore failed: {0}",
            ["fmt.deleted"] = "Deleted {0}",
            ["fmt.delete_failed"] = "Delete failed: {0}",
            ["fmt.rollback_in_seconds"] = "Rolling back in {0}s to: {1}",
            ["msg.rollback_canceled"] = "Rollback canceled",
            ["msg.rollback_loading"] = "Rolling back...",
            ["fmt.rollback_failed"] = "Rollback failed: {0}",
            ["msg.rollback_no_target"] = "No backup available, cannot auto-rollback",
            ["msg.rollback_target_missing"] = "Target slot missing or corrupted",
            ["msg.user_cancel"] = "User canceled",
            ["lbl.before_load_alias"] = "Auto backup before restore",
        };

        /// <summary>判定当前语言：游戏 Locale.currentLangName 为 zh-CN 时走中文，否则英文。</summary>
        private static Dictionary<string, string> Current
        {
            get
            {
                string name = null;
                try { name = Locale.currentLangName; } catch { }
                if (string.IsNullOrEmpty(name))
                {
                    try { name = PlayerPrefs.GetString("locale"); } catch { }
                }
                if (!string.IsNullOrEmpty(name)
                    && (name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("WC", System.StringComparison.OrdinalIgnoreCase)))
                {
                    return _zh;
                }
                return _en;
            }
        }

        internal static string T(string key)
        {
            if (Current.TryGetValue(key, out var v)) return v;
            // fallback：找不到 key 返回 key 本身，避免 UI 上空白
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
