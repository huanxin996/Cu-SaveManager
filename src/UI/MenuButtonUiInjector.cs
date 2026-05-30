using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 主菜单"存档管理"按钮加载逻辑——仿 KrokoshaCasualtiesMP 多人模组：
    /// 不用 IMGUI，而是 Harmony patch <see cref="PreRunScript.Start"/> 的 Postfix；
    /// 克隆原版 loadButton 的 GameObject，挂到 prerun.transform 下成为真子物体。
    /// 生命周期：PreRunScript 销毁时按钮自动一起销毁；scene 重载会再次走 Start，再次注入。
    /// 可见性：直接跟随 PreRunScript 父节点 active 状态，无需自己判断 contentWarning / cover。
    /// </summary>
    internal static class MenuButtonUiInjector
    {
        private const string InjectedName = "SaveManager_MenuButton";

        private static Action _onClick;
        private static Harmony _harmony;
        /// <summary>注入后的按钮 RectTransform；patch AdaptiveButton.overlayActive 时用来判断鼠标是否在按钮上以阻穿透。</summary>
        internal static RectTransform InjectedRect;

        internal static void Setup(Action onClick)
        {
            _onClick = onClick;
            try
            {
                _harmony = new Harmony("com.casualtiesUnknown.saveManager.menuButton");
                _harmony.PatchAll(typeof(MenuButtonUiInjector).Assembly);
                SeededWorldPatcher.TryPatch(_harmony);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Harmony PatchAll 失败：{ex.Message}");
            }

            // KrokoshaCasualtiesMP 同时 patch PreRunScript.Start 与 sceneLoaded，
            // 这里订阅 sceneLoaded 是兜底，保证场景重载时仍能注入。
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 解订 sceneLoaded 与 UnpatchSelf 移除所有 Harmony patch，
        /// 让游戏退出阶段不再触发 mod 反射回调。
        /// </summary>
        internal static void Dispose()
        {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { _harmony?.UnpatchSelf(); } catch { }
            _harmony = null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "PreGen") return;
            if (PreRunScript.instance == null) return;
            try { InjectOnce(PreRunScript.instance); }
            catch (Exception ex) { ModLog.Warning($"sceneLoaded 注入失败：{ex.Message}"); }
        }

        /// <summary>由 <see cref="PreRunScriptStartPatch"/> 在 PreRunScript.Start 完成后调用。</summary>
        internal static void OnPreRunScriptStarted(PreRunScript pre)
        {
            try
            {
                InjectOnce(pre);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"主菜单按钮注入失败：{ex.Message}");
            }
        }

        private static void InjectOnce(PreRunScript pre)
        {
            if (pre == null || pre.loadButton == null)
            {
                ModLog.Warning("PreRunScript / loadButton 为空，跳过注入。");
                return;
            }

            // 父级直接用 PreRunScript 自身 transform——不要用 loadButton 的原 parent，
            // 因为 loadButton 实际在 RunSettings 子面板下，RunSettings 默认隐藏，
            // 挂到那里按钮也会跟着隐藏。
            var parent = pre.transform;

            // 幂等：已经存在则跳过
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == InjectedName) return;
            }

            // 克隆 loadButton 的 GameObject，挂到 prerun.transform 下
            var clone = UnityEngine.Object.Instantiate(pre.loadButton.gameObject, parent, false);
            clone.name = InjectedName;
            clone.SetActive(true);

            // 解绑可能挂载的 LoadRun 监听并强制可点
            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                // 关掉 Inspector 里挂的 Persistent Listener（loadButton 默认绑了 LoadRun，
                // 单纯 RemoveAllListeners 只能清运行时加的，关 persistent state 才能彻底断开）
                int n = btn.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    btn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                }
                btn.onClick.RemoveAllListeners();
                btn.interactable = true;
                btn.onClick.AddListener(InvokeOnClick);
            }

            // 销毁所有本地化组件 (UILocalizer / SetLocaleText / etc.)：
            // 它们每帧用 Locale 把按钮文字覆盖回 "继续前进"。
            DestroyLocalizers(clone);

            // 销毁克隆来的子物体里所有 Image：原版 loadButton 含一个 BiomeIcon 缩略图
            // 与 saveTimeText 子节点，存档管理按钮不需要这些视觉。
            // 只保留按钮自身的 Image（背景）。
            CleanupLoadButtonChildren(clone);

            ReplaceAllTexts(clone, I18n.T("app.menu_button"));

            ReplaceAllTexts(clone, I18n.T("app.menu_button"));

            // 位置：屏幕顶部正中（避开屏幕中下方主菜单 5 个 AdaptiveButton；
            // 避开左上角的语言选择控件 + 右上角版本号）。
            var rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(280f, 96f);
                rt.anchoredPosition = new Vector2(0f, -20f);
                rt.localScale = Vector3.one;
            }
            clone.transform.SetAsLastSibling();
            InjectedRect = rt;

            ModLog.Info($"主菜单按钮已注入：parent={parent.name} sibling={clone.transform.GetSiblingIndex()}");
        }

        private static void InvokeOnClick()
        {
            try { _onClick?.Invoke(); }
            catch (Exception ex) { ModLog.Warning($"存档管理按钮点击异常：{ex.Message}"); }
        }

        /// <summary>
        /// 销毁克隆按钮里所有 Localizer / Tooltip 组件。
        /// UILocalizer 会反复把按钮文字覆盖回原版；UITooltip 会让悬停显示原版"继续前进"提示。
        /// 类型名通常含 Localizer / Localize / LocaleText / Tooltip 等关键字，反射列举不依赖编译期类型。
        /// </summary>
        private static void DestroyLocalizers(GameObject clone)
        {
            var components = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            foreach (var c in components)
            {
                if (c == null) continue;
                var name = c.GetType().Name;
                if (name == null) continue;
                if (name.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("LocaleText", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(c);
                }
            }
        }

        /// <summary>
        /// 清理 loadButton 克隆体内的 BiomeIcon 缩略图与 saveTimeText 子节点。
        /// 留下根节点本身的 Image（按钮背景）与第一个 TMP/Text（按钮文字），其余视觉子元素销毁掉。
        /// </summary>
        private static void CleanupLoadButtonChildren(GameObject clone)
        {
            // 销毁子级（不含 root）。第一层之外的节点统一干掉，让按钮回归"纯文字 + 背景"。
            var rootTransform = clone.transform;
            for (int i = rootTransform.childCount - 1; i >= 0; i--)
            {
                var child = rootTransform.GetChild(i);
                // 保留含 Text / TMP 的子节点（按钮文字）；其它销毁
                bool hasText = child.GetComponentInChildren<Text>(true) != null;
                if (!hasText)
                {
                    var allMb = child.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var mb in allMb)
                    {
                        if (mb == null) continue;
                        var fn = mb.GetType().FullName;
                        if (fn != null && fn.StartsWith("TMPro.TextMesh"))
                        {
                            hasText = true;
                            break;
                        }
                    }
                }
                if (!hasText)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        /// <summary>把克隆按钮里第一个文本改成 newText，其余清空；UI.Text 与 TMP_Text 都覆盖。</summary>
        private static void ReplaceAllTexts(GameObject clone, string newText)
        {
            bool first = true;

            var legacyTexts = clone.GetComponentsInChildren<Text>(includeInactive: true);
            foreach (var t in legacyTexts)
            {
                if (t == null) continue;
                t.text = first ? newText : "";
                first = false;
            }

            // TMP_Text 反射写入
            var allMb = clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            foreach (var mb in allMb)
            {
                if (mb == null) continue;
                var type = mb.GetType();
                if (type.FullName == null || !type.FullName.StartsWith("TMPro.TextMesh")) continue;
                var prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) continue;
                try
                {
                    prop.SetValue(mb, first ? newText : "", null);
                    first = false;
                }
                catch
                {
                    // 单条失败不致命
                }
            }
        }
    }

    [HarmonyPatch(typeof(PreRunScript), "Start")]
    internal static class PreRunScriptStartPatch
    {
        private static void Postfix(PreRunScript __instance)
        {
            MenuButtonUiInjector.OnPreRunScriptStarted(__instance);
        }
    }
}
