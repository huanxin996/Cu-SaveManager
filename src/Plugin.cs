using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 协调者：装配 HotkeyConfig / SaveStore / OverlayUI / SaveManagerWindow。
    /// 处理快捷键分发、定时备份调度。具体能力放在各专职文件里。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.casualtiesUnknown.saveManager";
        private const string PluginName = "SaveManager";
        private const string PluginVersion = "1.0.0";

        private static ManualLogSource _log;

        private HotkeyConfig _cfg;
        private SaveStore _store;
        private OverlayUI _overlay;
        private SaveManagerWindow _window;
        private RollbackController _rollback;

        private float _nextAutoBackupAt = float.PositiveInfinity;
        private string _lastMessage;
        private float _lastMessageAt;

        private static bool _quitting;

        private void Awake()
        {
            _log = Logger;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _cfg = new HotkeyConfig(Config);
            _store = new SaveStore(_log);
            _rollback = new RollbackController(_cfg, _store, _log);

            _overlay = new OverlayUI();
            _window = new SaveManagerWindow(_cfg, _store,
                onSaveNow: () => SaveManual(""),
                onSaveNowAs: nick => SaveManual(nick),
                onLoadSlot: LoadFromSlot,
                onDeleteSlot: DeleteSlot,
                onIntervalChanged: ResetAutoBackupTimer,
                getStatus: () => BuildStatusLine(),
                onOpened: OnPanelOpened,
                onClosed: OnPanelClosed,
                rollback: _rollback);

            MenuButtonUiInjector.Setup(_log, () => _window.OpenPanel());
            PreRunGuardLog.Log = _log;

            ResetAutoBackupTimer();
            // 用 Application.quitting 而非 OnApplicationQuit：在 Plugin 实例销毁前触发，可保证
            // Harmony unpatch 在游戏方法被最后一次调用前完成。
            Application.quitting += OnApplicationQuitting;
            _log.LogInfo($"{PluginName} ready · slots→{_store.SlotsRoot} · save.sv={SaveStore.GameSavePath}");
        }

        private void OnApplicationQuitting()
        {
            if (_quitting) return;
            _quitting = true;
            try { _rollback?.Dispose(); } catch { }
            try { MenuButtonUiInjector.Dispose(); } catch { }
            try { OnPanelClosed(); } catch { }
            try { Application.quitting -= OnApplicationQuitting; } catch { }
        }

        private void Update()
        {
            if (_quitting) return;
            UiBlocker.EnforceBlocked(_log);
            _rollback.Tick();

            if (HotkeyConfig.TriggeredThisFrame(_cfg.ToggleSlotsHotkey))
            {
                if (_window.Open) _window.ClosePanel();
                else _window.OpenPanel();
            }
            // ESC 关闭主面板。PlayerCameraHandleInputGuard 已 Prefix 吞掉游戏 ESC，
            // 必须在这里独立处理才能让面板关闭。
            if (_window.Open && Input.GetKeyDown(KeyCode.Escape))
            {
                _window.ClosePanel();
            }
            if (HotkeyConfig.TriggeredThisFrame(_cfg.ToggleSettingsHotkey))
            {
                _window.OpenPanel();
                _window.CancelKeyCapture();
            }
            if (HotkeyConfig.TriggeredThisFrame(_cfg.QuickSaveHotkey))
            {
                SaveManual("");
            }

            if (_cfg.AutoBackupEnabled.Value && Time.unscaledTime >= _nextAutoBackupAt)
            {
                DoAutoBackup();
                ResetAutoBackupTimer();
            }
        }

        private void OnGUI()
        {
            if (_quitting) return;
            _overlay.Draw(() => _window.OpenPanel());
            _window.Draw();
        }

        private void OnDisable()
        {
            // mod 卸载时确保游戏 UI 状态恢复
            OnPanelClosed();
        }

        // —— 业务回调 —— //

        private void SaveManual(string nickname)
        {
            try
            {
                GameSaveBridge.TrySaveGame(_log);
                string path = _store.SaveManual(nickname);
                SetMessage(I18n.F("fmt.saved_to", System.IO.Path.GetFileName(path), SaveStore.GameSavePath));
            }
            catch (Exception ex)
            {
                SetMessage(I18n.F("fmt.save_failed", ex.Message));
                _log.LogWarning(ex.ToString());
            }
        }

        private void DoAutoBackup()
        {
            try
            {
                if (PlayerCamera.main == null || WorldGeneration.world == null) return;
                // 先调 SaveSystem.SaveGame 让游戏把内存里的物品 / 装备 / 状态写到 save.sv，
                // 否则槽位拷的是上一次进层的旧快照，回档会丢身上的物品。
                GameSaveBridge.TrySaveGame(_log);
                string path = _store.AutoBackup(_cfg.AutoBackupKeep.Value);
                _log.LogInfo(I18n.F("fmt.auto_backup_done", System.IO.Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                _log.LogWarning(I18n.F("fmt.auto_backup_failed", ex.Message));
            }
        }

        private void LoadFromSlot(SlotInfo slot)
        {
            try
            {
                _store.RestoreSlotToSave(slot.FullSlotPath, _cfg.BackupBeforeOverwrite.Value);
                SetMessage(I18n.F("fmt.restored_to", slot.File.Name));
            }
            catch (Exception ex)
            {
                SetMessage(I18n.F("fmt.restore_failed", ex.Message));
                _log.LogWarning(ex.ToString());
            }
        }

        private void DeleteSlot(SlotInfo slot)
        {
            try
            {
                _store.DeleteSlot(slot.FullSlotPath);
                SetMessage(I18n.F("fmt.deleted", slot.File.Name));
            }
            catch (Exception ex)
            {
                SetMessage(I18n.F("fmt.delete_failed", ex.Message));
                _log.LogWarning(ex.ToString());
            }
        }

        private void SetMessage(string msg)
        {
            _lastMessage = msg;
            _lastMessageAt = Time.unscaledTime;
            _log.LogInfo(msg);
        }

        private string BuildStatusLine()
        {
            if (_lastMessage != null && Time.unscaledTime - _lastMessageAt < 8f)
            {
                return _lastMessage;
            }
            return I18n.F("status.save_path", SaveStore.GameSavePath);
        }

        private void ResetAutoBackupTimer()
        {
            float interval = Mathf.Max(0.5f, _cfg.AutoBackupIntervalMinutes.Value) * 60f;
            _nextAutoBackupAt = Time.unscaledTime + interval;
        }

        // —— 阻穿透 —— //

        private void OnPanelOpened()
        {
            UiBlocker.Block(_log);
        }

        private void OnPanelClosed()
        {
            UiBlocker.Unblock(_log);
        }
    }
}
