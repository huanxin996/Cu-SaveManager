using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 集中维护持久化配置项。所有按键默认 KeyCode.None，未绑定时不响应。
    /// </summary>
    internal sealed class HotkeyConfig
    {
        private static readonly KeyboardShortcut Unbound = new KeyboardShortcut(KeyCode.None);

        internal ConfigEntry<KeyboardShortcut> ToggleSettingsHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> ToggleSlotsHotkey { get; }
        internal ConfigEntry<KeyboardShortcut> QuickSaveHotkey { get; }

        internal ConfigEntry<bool> AutoBackupEnabled { get; }
        internal ConfigEntry<float> AutoBackupIntervalMinutes { get; }
        internal ConfigEntry<int> AutoBackupKeep { get; }
        internal ConfigEntry<bool> BackupBeforeOverwrite { get; }

        // —— 死亡回档 —— //
        internal ConfigEntry<bool> AutoRollbackOnDeath { get; }
        internal ConfigEntry<float> RollbackDelaySeconds { get; }
        internal ConfigEntry<int> MultiplayerDeathThreshold { get; }
        internal ConfigEntry<int> RecentSlotsLimit { get; }

        // —— 固定世界 —— //
        internal ConfigEntry<string> PreferredEngine { get; }
        internal ConfigEntry<string> SeedInput { get; }
        internal ConfigEntry<string> PositionMode { get; }
        internal ConfigEntry<float> FixedX { get; }
        internal ConfigEntry<float> FixedY { get; }

        // —— 杂项 —— //
        internal ConfigEntry<bool> ShowLogInConsole { get; }
        internal ConfigEntry<bool> AcceptUpdateNotice { get; }
        internal ConfigEntry<string> PreferredLanguage { get; }

        internal HotkeyConfig(ConfigFile config)
        {
            ToggleSettingsHotkey = config.Bind("Hotkeys", "ToggleSettingsHotkey", Unbound,
                "打开主面板并切到设置 tab。默认未绑定。");
            ToggleSlotsHotkey = config.Bind("Hotkeys", "ToggleSlotsHotkey", Unbound,
                "打开 / 关闭主面板（默认在存档 tab）。默认未绑定。");
            QuickSaveHotkey = config.Bind("Hotkeys", "QuickSaveHotkey", Unbound,
                "立即保存当前进度到新槽位。默认未绑定。");

            AutoBackupEnabled = config.Bind("AutoBackup", "Enabled", false,
                "是否启用定时备份。开启后每隔 IntervalMinutes 复制当前 save.sv 到自动槽。");
            AutoBackupIntervalMinutes = config.Bind("AutoBackup", "IntervalMinutes", 5.0f,
                new ConfigDescription("定时备份间隔（分钟）", new AcceptableValueRange<float>(0.5f, 240.0f)));
            AutoBackupKeep = config.Bind("AutoBackup", "Keep", 10,
                new ConfigDescription("自动槽最多保留多少份（不含 pinned 槽）", new AcceptableValueRange<int>(1, 100)));
            BackupBeforeOverwrite = config.Bind("Safety", "BackupBeforeOverwrite", true,
                "在切回某槽位前，先把当前 save.sv 复制到一个新槽（昵称\"切回前自动备份\"）。");

            AutoRollbackOnDeath = config.Bind("Rollback", "AutoOnDeath", false,
                "角色死亡时（黑屏 100% 且 body 已死）自动倒计时回档到最新备份。");
            RollbackDelaySeconds = config.Bind("Rollback", "DelaySeconds", 3.0f,
                new ConfigDescription("倒计时秒数，结束后执行回档。期间可在回档 tab 取消。",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));
            MultiplayerDeathThreshold = config.Bind("Rollback", "MultiplayerDeathThreshold", 1,
                new ConfigDescription("多人模式触发自动回档需要的死亡人数（包含本地玩家）。",
                    new AcceptableValueRange<int>(1, 16)));
            RecentSlotsLimit = config.Bind("Rollback", "RecentSlotsLimit", 20,
                new ConfigDescription("回档 tab 默认显示的最近槽位条数（不含日期筛选）。",
                    new AcceptableValueRange<int>(5, 100)));

            PreferredEngine = config.Bind("World", "PreferredEngine", "qol",
                "固定世界生成引擎偏好：qol=优先用 QoL（不在场则回落 self）；self=用本 mod 自身引擎并暂时禁用 QoL。");
            SeedInput = config.Bind("World", "SeedInput", "",
                "自身引擎种子。留空时从存档字节自动派生（FNV-1a）；填数字或文本则按手动种子。仅 self 引擎生效。");
            PositionMode = config.Bind("World", "PositionMode", "lastPos",
                "读档/回档后的玩家位置模式：lastPos=回保存时坐标；fixedPos=回 FixedX/FixedY。");
            FixedX = config.Bind("World", "FixedX", 0f, "fixedPos 模式下的世界 X 坐标。");
            FixedY = config.Bind("World", "FixedY", 0f, "fixedPos 模式下的世界 Y 坐标。");

            ShowLogInConsole = config.Bind("Misc", "ShowInConsole", false,
                "是否把模组日志同步打印到游戏内控制台（` 键打开）。关闭时仅写入 BepInEx 日志。");
            AcceptUpdateNotice = config.Bind("Misc", "AcceptUpdateNotice", true,
                "是否在启动时检查 GitHub 新版本并在游戏内提示。关闭则不检测不提示。");
            PreferredLanguage = config.Bind("I18n", "PreferredLanguage", "auto",
                "界面语言：auto=跟随游戏；zh=强制中文；en=强制英文。该值会写入配置文件并在重启后保留。");
        }

        internal static bool IsBound(KeyboardShortcut sc) => sc.MainKey != KeyCode.None;

        internal static bool TriggeredThisFrame(ConfigEntry<KeyboardShortcut> entry)
        {
            var sc = entry.Value;
            if (!IsBound(sc)) return false;
            return sc.IsDown();
        }
    }
}
