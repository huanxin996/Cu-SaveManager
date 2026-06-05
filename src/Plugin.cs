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
    [BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.casualtiesUnknown.saveManager";
        private const string PluginName = "SaveManager";
        private const string PluginVersion = "1.0.9";

        private static ManualLogSource _log;
        private static Plugin _instance;

        private HotkeyConfig _cfg;
        private SaveStore _store;
        private OverlayUI _overlay;
        private SaveManagerWindow _window;
        private RollbackController _rollback;

        private float _nextAutoBackupAt = float.PositiveInfinity;
        private string _lastMessage;
        private float _lastMessageAt;

        private static bool _quitting;

        internal static string PreferredLanguageMode => _instance?._cfg?.PreferredLanguage?.Value ?? "auto";

        private void Awake()
        {
            _instance = this;
            _log = Logger;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _cfg = new HotkeyConfig(Config);
            ModLog.Init(_log);
            ModLog.ShowInConsole = _cfg.ShowLogInConsole.Value;
            _cfg.ShowLogInConsole.SettingChanged += (_, __) => ModLog.ShowInConsole = _cfg.ShowLogInConsole.Value;
            UpdateChecker.Enabled = _cfg.AcceptUpdateNotice.Value;
            WorldEngineArbiter.SyncPreference(_cfg.PreferredEngine.Value, _cfg.SeedInput.Value,
                _cfg.PositionMode.Value, _cfg.FixedX.Value, _cfg.FixedY.Value);
            _store = new SaveStore();
            _rollback = new RollbackController(_cfg, _store);

            _overlay = new OverlayUI();
            _window = new SaveManagerWindow(_cfg, _store,
                onSaveNow: () => SaveManual(""),
                onSaveNowAs: nick => SaveManual(nick),
                onSaveAndExit: SaveAndExitCurrentRun,
                onLoadSlot: LoadFromSlot,
                onDeleteSlot: DeleteSlot,
                onIntervalChanged: ResetAutoBackupTimer,
                getStatus: () => BuildStatusLine(),
                onOpened: OnPanelOpened,
                onClosed: OnPanelClosed,
                rollback: _rollback);

            MenuButtonUiInjector.Setup(() => _window.OpenPanel());

            ResetAutoBackupTimer();
            // 用 Application.quitting 而非 OnApplicationQuit：在 Plugin 实例销毁前触发，可保证
            // Harmony unpatch 在游戏方法被最后一次调用前完成。
            Application.quitting += OnApplicationQuitting;
            ModLog.Info($"{PluginName} ready · slots→{_store.SlotsRoot} · save.sv={SaveStore.GameSavePath}");
            ModLog.Info(MultiplayerBridge.IsModPresent() ? I18n.T("mp.mod_detected") : I18n.T("mp.mod_not_detected"));
            ModLog.Info(QolBridge.IsQolPresent() ? I18n.T("qol.mod_detected") : I18n.T("qol.mod_not_detected"));
            MpWorldSeedInjector.TryPatch();
            MpRollbackRunner.Ensure(this);
            MpPositionRestorer.TryPatchHarmony();
            gameObject.AddComponent<UpdateChecker>();
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
            UiBlocker.EnforceBlocked();
            _rollback.Tick();

            if (_cfg.AutoBackupEnabled.Value && Time.unscaledTime >= _nextAutoBackupAt)
            {
                DoAutoBackup();
                ResetAutoBackupTimer();
            }
        }

        private void LateUpdate()
        {
            if (_quitting) return;
            ImGuiImeRecovery.TickUpdate(_window.ExpectsTextInput);

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
        }

        private void OnGUI()
        {
            if (_quitting) return;
            ImGuiImeRecovery.TickOnGui(_window.ExpectsTextInput);
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
                if (MultiplayerBridge.IsMultiplayerRunning() && !MultiplayerBridge.IsServer())
                {
                    SetMessage(I18n.T("mp.client_cannot_save"));
                    return;
                }
                if (!GameSaveBridge.CanSnapshotCurrentLayer())
                {
                    SetMessage(I18n.T("msg.save_layer_transition"));
                    return;
                }
                if (MultiplayerBridge.IsMultiplayerRunning())
                    MultiplayerBridge.TrySaveMpGame();
                else
                    GameSaveBridge.TrySaveGame();
                string path = _store.SaveManual(nickname);
                SetMessage(I18n.F("fmt.saved_to", System.IO.Path.GetFileName(path), SaveStore.CurrentSaveDisplayPath));
            }
            catch (Exception ex)
            {
                SetMessage(I18n.F("fmt.save_failed", ex.Message));
                ModLog.Warning(ex.ToString());
            }
        }

        private void DoAutoBackup()
        {
            try
            {
                if (PlayerCamera.main == null || WorldGeneration.world == null) return;
                if (MultiplayerBridge.IsMultiplayerRunning() && !MultiplayerBridge.IsServer())
                {
                    ModLog.Info(I18n.T("mp.client_skip_autosave"));
                    return;
                }
                if (!GameSaveBridge.CanSnapshotCurrentLayer())
                {
                    ModLog.Info(I18n.T("msg.save_skip_layer_transition"));
                    return;
                }
                // 先调 SaveSystem.SaveGame 让游戏把内存里的物品 / 装备 / 状态写到 save.sv，
                // 否则槽位拷的是上一次进层的旧快照，回档会丢身上的物品。
                GameSaveBridge.TrySaveGame();
                string path = _store.AutoBackup(_cfg.AutoBackupKeep.Value);
                ModLog.Info(I18n.F("fmt.auto_backup_done", System.IO.Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                ModLog.Warning(I18n.F("fmt.auto_backup_failed", ex.Message));
            }
        }

        private void SaveAndExitCurrentRun()
        {
            try
            {
                if (PlayerCamera.main == null || PlayerCamera.main.body == null || WorldGeneration.world == null)
                {
                    SetMessage(I18n.T("msg.save_exit_unavailable"));
                    return;
                }

                if (MultiplayerBridge.IsMultiplayerRunning())
                {
                    if (!MultiplayerBridge.IsServer())
                    {
                        SetMessage(I18n.T("mp.client_cannot_save"));
                        return;
                    }
                    if (!MultiplayerBridge.TrySaveMpGame())
                    {
                        SetMessage(I18n.T("msg.save_exit_unavailable"));
                        return;
                    }
                    _window.ClosePanel();
                    PlayerCamera.main.ToMainMenu();
                    SetMessage(I18n.T("msg.save_exit_mp_done"));
                    return;
                }

                if (!GameSaveBridge.TrySaveGame())
                {
                    SetMessage(I18n.T("msg.save_exit_unavailable"));
                    return;
                }

                _store.PersistCurrentSaveForContinue();
                _window.ClosePanel();
                PlayerCamera.main.ToMainMenu();
                SetMessage(I18n.T("msg.save_exit_done"));
            }
            catch (Exception ex)
            {
                SetMessage(I18n.F("fmt.save_exit_failed", ex.Message));
                ModLog.Warning(ex.ToString());
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
                ModLog.Warning(ex.ToString());
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
                ModLog.Warning(ex.ToString());
            }
        }

        private void SetMessage(string msg)
        {
            _lastMessage = msg;
            _lastMessageAt = Time.unscaledTime;
            ModLog.Info(msg);
        }

        private string BuildStatusLine()
        {
            if (_lastMessage != null && Time.unscaledTime - _lastMessageAt < 8f)
            {
                return _lastMessage;
            }
            return I18n.F("status.save_path", SaveStore.CurrentSaveDisplayPath);
        }

        private void ResetAutoBackupTimer()
        {
            float interval = Mathf.Max(0.5f, _cfg.AutoBackupIntervalMinutes.Value) * 60f;
            _nextAutoBackupAt = Time.unscaledTime + interval;
        }

        // —— 阻穿透 —— //

        private void OnPanelOpened()
        {
            UiBlocker.Block();
            ModLog.Info(SaveStore.DescribeSavePathDecision());
        }

        private void OnPanelClosed()
        {
            UiBlocker.Unblock();
        }
    }
}
