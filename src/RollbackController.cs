using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CasualtiesUnknown.SaveManager
{
    internal enum RollbackState
    {
        Idle,
        Counting,
        Executing,
    }

    /// <summary>
    /// 回档状态机：
    /// - 单机：每帧检查 PlayerCamera.main 死亡判据（!body.alive && blackAmount >= 1f），
    ///   若 cfg.AutoRollbackOnDeath 启用且当前未在倒计时，则用 SaveStore.FindLatestRollbackTarget 启动倒计时。
    /// - 多人：仅 host 端基于 MultiplayerBridge.DeadPlayerCount 判触发条件；广播提示由 host 调 Server_AnnounceAlert。
    /// - 倒计时：每秒刷新一次屏幕中央 DoAlert；到点执行 ExecuteNow。
    /// - 取消：UI 显式取消 / 倒计时期间 PlayerCamera.main 不再满足死亡条件。
    /// </summary>
    internal sealed class RollbackController
    {
        private readonly HotkeyConfig _cfg;
        private readonly SaveStore _store;
        private readonly ManualLogSource _log;

        private RollbackState _state = RollbackState.Idle;
        private float _remaining;
        private SlotInfo _target;
        private string _lastError;
        private int _lastBroadcastSecond = -1;
        private float _nextMpDeathCheckAt;

        // 死亡判据满足后的本生节流标志，每次场景切换重置
        private bool _deathHandledThisLife;

        internal RollbackController(HotkeyConfig cfg, SaveStore store, ManualLogSource log)
        {
            _cfg = cfg;
            _store = store;
            _log = log;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>解订 SceneManager 事件并复位静态接管标志。</summary>
        internal void Dispose()
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            IsActiveGlobal = false;
            IsDeathSuspected = false;
            PlayerCameraDeathDetector.Reset();
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            // 进新场景重置一次性标志，本次回档完成
            _deathHandledThisLife = false;
            IsDeathSuspected = false;
            _mpDyingCached = false;
            _nextMpDeathCheckAt = 0f;
            PlayerCameraDeathDetector.Reset();
            if (_state == RollbackState.Executing)
            {
                SetState(RollbackState.Idle);
                _target = null;
                _remaining = 0f;
                _lastBroadcastSecond = -1;
            }
        }

        internal RollbackState State => _state;
        internal float Remaining => _remaining;
        internal SlotInfo Target => _target;
        internal string LastError => _lastError;

        /// <summary>给 Harmony patch 用的全局态：只要面板在倒计时或正在执行回档，就视为"接管中"。</summary>
        internal static bool IsActiveGlobal { get; private set; }

        /// <summary>
        /// 死亡判据满足后立即变 true，比 RollbackState.Counting 早一帧。
        /// <see cref="GameAutoRespawnGuard"/> 据此拦截游戏自身的 RegenerateWorld / ToMainMenu。
        /// </summary>
        internal static bool IsDeathSuspected { get; private set; }

        private void SetState(RollbackState s)
        {
            _state = s;
            IsActiveGlobal = (s == RollbackState.Counting || s == RollbackState.Executing);
        }

        /// <summary>每帧调用：维护死亡检测 + 倒计时刷新。</summary>
        internal void Tick()
        {
            try
            {
                // 死亡判据先于倒计时刷新，确保 GameAutoRespawnGuard 抢先一帧。AutoRollbackOnDeath 关时不接管。
                IsDeathSuspected = _cfg.AutoRollbackOnDeath.Value && IsDying();
                if (_state == RollbackState.Counting) TickCountdown();
                else if (_state == RollbackState.Idle) CheckDeathTrigger();
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"RollbackController.Tick 异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 判定是否处于死亡过渡帧。
        /// 单机：读 <see cref="PlayerCameraDeathDetector.LocalDeathLatched"/>。
        /// 多人 host：节流到 500ms 一次反射 KrokMP DeadPlayerCount。
        /// </summary>
        private bool IsDying()
        {
            try
            {
                if (MultiplayerBridge.IsMultiplayerRunning(_log))
                {
                    if (!MultiplayerBridge.IsServer(_log)) return false;
                    float now = Time.unscaledTime;
                    if (now < _nextMpDeathCheckAt) return _mpDyingCached;
                    _nextMpDeathCheckAt = now + 0.5f;
                    int dead = MultiplayerBridge.DeadPlayerCount(_log);
                    _mpDyingCached = dead >= Mathf.Max(1, _cfg.MultiplayerDeathThreshold.Value);
                    return _mpDyingCached;
                }
                return PlayerCameraDeathDetector.LocalDeathLatched;
            }
            catch { return false; }
        }

        private bool _mpDyingCached;

        // —— 主动接口 —— //

        internal void RequestRollback(SlotInfo slot)
        {
            if (slot == null || !File.Exists(slot.FullSlotPath))
            {
                _lastError = I18n.T("msg.rollback_target_missing");
                return;
            }
            _target = slot;
            _remaining = Mathf.Max(1f, _cfg.RollbackDelaySeconds.Value);
            SetState(RollbackState.Counting);
            _lastError = null;
            _lastBroadcastSecond = -1;
            BroadcastCountdown(forceFirst: true);
        }

        internal void Cancel(string reason)
        {
            if (_state != RollbackState.Counting) return;
            SetState(RollbackState.Idle);
            _target = null;
            _remaining = 0f;
            _lastError = string.IsNullOrEmpty(reason) ? null : reason;
            _lastBroadcastSecond = -1;
            BroadcastLocal(I18n.T("msg.rollback_canceled"));
            TryBroadcastMP(I18n.T("msg.rollback_canceled"));
        }

        // —— 内部 —— //

        private void CheckDeathTrigger()
        {
            if (!_cfg.AutoRollbackOnDeath.Value) return;
            if (_deathHandledThisLife) return;
            if (!IsDying()) return;

            var slot = _store.FindLatestRollbackTarget();
            if (slot == null)
            {
                _deathHandledThisLife = true;
                _lastError = I18n.T("msg.rollback_no_target");
                BroadcastLocal(_lastError);
                _log?.LogInfo("[SaveManager] 检测到死亡但 FindLatestRollbackTarget 返回 null");
                return;
            }
            _deathHandledThisLife = true;
            RequestRollback(slot);
        }

        private void TickCountdown()
        {
            _remaining -= Time.unscaledDeltaTime;
            int currentSec = Mathf.CeilToInt(_remaining);
            if (currentSec != _lastBroadcastSecond)
            {
                _lastBroadcastSecond = currentSec;
                BroadcastCountdown(forceFirst: false);
            }
            if (_remaining <= 0f) ExecuteNow();
        }

        private void BroadcastCountdown(bool forceFirst)
        {
            int sec = forceFirst ? Mathf.CeilToInt(_remaining) : _lastBroadcastSecond;
            if (sec < 0) sec = 0;
            string nick = _target == null ? "" : (string.IsNullOrEmpty(_target.Sidecar.Nickname)
                ? _target.File.Name : _target.Sidecar.Nickname);
            string msg = I18n.F("fmt.rollback_in_seconds", sec, nick);
            BroadcastLocal(msg);
            TryBroadcastMP(msg);
        }

        private void BroadcastLocal(string msg)
        {
            try
            {
                var cam = PlayerCamera.main;
                cam?.DoAlert(msg, important: true);
            }
            catch { }
        }

        private void TryBroadcastMP(string msg)
        {
            // 仅 host 才能广播；mp 未跑或非 server 时静默
            if (!MultiplayerBridge.IsMultiplayerRunning(_log)) return;
            if (!MultiplayerBridge.IsServer(_log)) return;
            MultiplayerBridge.TryAnnounceAlert(msg, _log);
        }

        private void ExecuteNow()
        {
            if (_target == null)
            {
                SetState(RollbackState.Idle);
                return;
            }
            SetState(RollbackState.Executing);
            try
            {
                File.Copy(_target.FullSlotPath, SaveStore.GameSavePath, overwrite: true);
                SaveSystem.loadedRun = true;
                QolBridge.PrepareRollback(_target.Sidecar, _log);
                _log?.LogInfo($"[SaveManager] 执行回档：{_target.File.Name}");
                BroadcastLocal(I18n.T("msg.rollback_loading"));
                TryBroadcastMP(I18n.T("msg.rollback_loading"));
                StartLoadSampleScene();
            }
            catch (Exception ex)
            {
                SetState(RollbackState.Idle);
                _lastError = I18n.F("fmt.rollback_failed", ex.Message);
                _log?.LogWarning(_lastError);
                BroadcastLocal(_lastError);
            }
        }

        /// <summary>仿 PreRunScript.WaitLoad：先 GlobalDark.Darken 黑屏过渡再 LoadScene。</summary>
        private static void StartLoadSampleScene()
        {
            try
            {
                var gd = GlobalDark.main;
                if (gd != null) gd.Darken();
            }
            catch { }
            // 直接 LoadScene；游戏其他课程脚本（BasicCourse 等）也是同样模式
            SceneManager.LoadScene("SampleScene");
        }
    }
}
