using System;
using System.Collections;
using UnityEngine;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>供 MpPositionRestorer 在主线程延迟重试位置写回。</summary>
    internal sealed class MpRollbackRunner : MonoBehaviour
    {
        internal static MpRollbackRunner Instance { get; private set; }

        internal static void Ensure(Plugin plugin)
        {
            if (Instance != null) return;
            if (plugin == null) return;
            Instance = plugin.gameObject.AddComponent<MpRollbackRunner>();
        }

        private void Awake()
        {
            Instance = this;
            hideFlags = HideFlags.HideAndDontSave;
        }

        internal void RunApplyRetries(Action apply, Action onComplete = null)
        {
            if (apply == null) return;
            StartCoroutine(ApplyRetries(apply, onComplete));
        }

        private static IEnumerator ApplyRetries(Action apply, Action onComplete)
        {
            yield return null;
            apply();
            yield return null;
            apply();
            yield return new WaitForSeconds(0.5f);
            apply();
            onComplete?.Invoke();
        }
    }
}
