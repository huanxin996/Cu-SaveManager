using System;
using System.Collections;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 启动时拉取 GitHub releases/latest 比对当前版本，发现新版在屏幕左上角红字提示并可点开 release 页。
    /// 受 AcceptUpdateNotice 开关控制；关闭则不检测不提示。
    /// </summary>
    [DisallowMultipleComponent]
    public class UpdateChecker : MonoBehaviour
    {
        private const string PluginGuid = "com.casualtiesUnknown.saveManager";
        private const string ApiUrl = "https://api.github.com/repos/huanxin996/Cu-SaveManager/releases/latest";
        private const string ReleasesUrl = "https://github.com/huanxin996/Cu-SaveManager/releases";

        internal static bool Enabled = true;

        private static bool _checked;
        private static string _currentVersion = "";
        private static string _latestTag = "";
        private static bool _updateAvailable;

        private void Start()
        {
            if (_checked || !Enabled) return;
            _checked = true;
            try
            {
                if (Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
                    _currentVersion = info.Metadata.Version.ToString();
            }
            catch (Exception ex)
            {
                ModLog.Warning("UpdateChecker init failed: " + ex.Message);
                return;
            }
            StartCoroutine(CheckForUpdates());
        }

        private IEnumerator CheckForUpdates()
        {
            using (UnityWebRequest www = UnityWebRequest.Get(ApiUrl))
            {
                www.SetRequestHeader("User-Agent", "Cu-SaveManager");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    ModLog.Warning("Update check failed: " + www.error);
                    yield break;
                }

                string json = www.downloadHandler.text;
                const string key = "\"tag_name\":\"";
                int idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) yield break;
                int start = idx + key.Length;
                int end = json.IndexOf('"', start);
                if (end <= start) yield break;
                _latestTag = json.Substring(start, end - start);

                if (IsNewer(_currentVersion, _latestTag))
                {
                    _updateAvailable = true;
                    ModLog.Warning($"Update available: {_currentVersion} -> {_latestTag}");
                }
                else
                {
                    ModLog.Info($"Up to date. Current: {_currentVersion}, Latest: {_latestTag}");
                }
            }
        }

        private static bool IsNewer(string current, string latest)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return false;
            string cur = current.TrimStart('v', 'V').Trim();
            string lat = latest.TrimStart('v', 'V').Trim();
            return Version.TryParse(cur, out Version vc) && Version.TryParse(lat, out Version vl) && vl > vc;
        }

        private void OnGUI()
        {
            if (!_updateAvailable || !Enabled) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                richText = false,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
                hover = { textColor = new Color(1f, 0.55f, 0.55f) },
                active = { textColor = new Color(1f, 0.2f, 0.2f) },
            };

            string text = I18n.F("update.available", _latestTag);
            float x = 32f;
            float y = Screen.height * 0.18f;
            Vector2 size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(x, y, size.x + 8f, size.y + 4f);
            if (GUI.Button(rect, text, style))
                Application.OpenURL(ReleasesUrl);
        }
    }
}
