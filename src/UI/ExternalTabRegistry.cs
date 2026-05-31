using System;
using System.Collections.Generic;

namespace CasualtiesUnknown.SaveManager
{
    /// <summary>
    /// 供其它 mod 注册附加设置分页的入口。无注册时主面板分页与原先完全一致。
    /// 外部通过反射调用 Register；SaveManagerWindow 在 tab 区追加这些页。
    /// 标题用 Func 动态求值，随宿主语言实时变化。
    /// </summary>
    public static class ExternalTabRegistry
    {
        public sealed class Entry
        {
            public Func<string> TitleFn;
            public Action Draw;
            public string Title => TitleFn != null ? TitleFn() : "";
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static void Register(string title, Action draw)
        {
            Register(() => title, draw);
        }

        public static void Register(Func<string> titleFn, Action draw)
        {
            if (titleFn == null || draw == null) return;
            string key = titleFn();
            foreach (var e in _entries)
            {
                if (e.Title == key) { e.TitleFn = titleFn; e.Draw = draw; return; }
            }
            _entries.Add(new Entry { TitleFn = titleFn, Draw = draw });
        }

        internal static IReadOnlyList<Entry> Entries => _entries;
        internal static int Count => _entries.Count;
    }
}
