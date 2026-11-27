// NJ-Flush: the RAM page's breakdown (where the RAM is), drawn inside the panel.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using W = System.Windows;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;

// Memory breakdown (Nader 2026-11-23: "beautify it"): one stacked bar of where the RAM is, a legend that says what each part means,
// the system facts behind "hidden" RAM. 1.3: drawn inside the panel's RAM page. WPF, so the NJ-Font renders.
static class BreakdownWindow {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    static WM.SolidColorBrush B(string hex) { return Theme.Brush(hex); }
    static readonly WM.Brush Bg = B(Theme.Bg), Card = B(Theme.Surface), Fg = B(Theme.Fg), Muted = B(Theme.Muted), Line = B(Theme.Line), Accent = B(Theme.Ident[0]);

    static System.Windows.Controls.TextBlock T(string text, double size, WM.Brush color, bool bold = false) {
        return new System.Windows.Controls.TextBlock { Text = Tx.Bidi(text), FontSize = size, Foreground = color, FontWeight = bold ? W.FontWeights.Bold : W.FontWeights.Normal,
                                                       TextWrapping = W.TextWrapping.Wrap };
    }

    // 1.3: the RAM page of the panel (the panel owns the buttons)
    public static void Build(System.Windows.Controls.StackPanel root, MemInfo m, bool header = true) {
        double pct = m.InUse / m.Total * 100;
        string[] tierCol = { Theme.Ident[0], Theme.Warn, Theme.Hot, Theme.Crit };
        var head = new System.Windows.Controls.DockPanel();
        var big = T(Tx.Pct(pct), 40, B(tierCol[Overlay.TierOf(pct)]), true); big.Margin = new W.Thickness(0, -6, 0, 0);
        System.Windows.Controls.DockPanel.SetDock(big, System.Windows.Controls.Dock.Right); head.Children.Add(big);
        var titles = new System.Windows.Controls.StackPanel();
        titles.Children.Add(T(Tx.T("bd.title"), 22, Fg, true));
        titles.Children.Add(T(Tx.F("bd.sub", m.InUse, m.Total), 14, Muted));
        head.Children.Add(titles);
        if (header) root.Children.Add(head);
        else root.Children.Add(new System.Windows.Controls.TextBlock { Text = Tx.T("bd.title"), FontSize = Theme.Title, FontWeight = W.FontWeights.Bold, Margin = new W.Thickness(0, Theme.S5, 0, Theme.S2) });

        // the stacked bar: every part of physical memory, in order
        var parts = new[] {
            Tuple.Create("apps", m.Apps, "#22D3EE", Tx.T("bd.apps_d")),
            Tuple.Create("kernel", m.Kernel, "#2DD4BF", Tx.T("bd.kernel_d")),
            Tuple.Create("modified", m.Modified, "#FACC15", Tx.T("bd.modified_d")),
            Tuple.Create("hidden", m.Hidden, "#FB923C", Tx.T("bd.hidden_d")),
            Tuple.Create("cache", m.Standby, "#64748B", Tx.T("bd.cache_d")),
            Tuple.Create("free", m.Free, "#1F2D3D", Tx.T("bd.free_d"))
        };
        var bar = new System.Windows.Controls.Grid { Height = 20, Width = Theme.Content, FlowDirection = W.FlowDirection.LeftToRight, HorizontalAlignment = W.HorizontalAlignment.Left, Margin = new W.Thickness(0, Theme.S2, 0, Theme.S4) };
        bar.Clip = new WM.RectangleGeometry(new W.Rect(0, 0, Theme.Content, 20), 10, 10);
        int col = 0;
        foreach (var p in parts) {
            if (p.Item2 <= 0.005) continue;
            bar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new W.GridLength(p.Item2, W.GridUnitType.Star) });
            var seg = new System.Windows.Shapes.Rectangle { Fill = B(p.Item3), Margin = new W.Thickness(0, 0, 2, 0), ToolTip = string.Format("{0}: {1:0.00} GB", Tx.T("bd." + p.Item1), p.Item2) };
            System.Windows.Controls.Grid.SetColumn(seg, col++); bar.Children.Add(seg);
        }
        root.Children.Add(bar);

        // legend: two columns, each part with its size and one line of meaning
        var legend = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var p in parts) {
            if (!m.Lists && (p.Item1 == "cache" || p.Item1 == "free" || p.Item1 == "modified")) continue;
            var cell = new System.Windows.Controls.DockPanel { Margin = new W.Thickness(0, 0, Theme.S5, Theme.S4) };
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = B(p.Item3), Margin = new W.Thickness(0, 5, 10, 0), VerticalAlignment = W.VerticalAlignment.Top };
            if (p.Item1 == "free") { dot.Stroke = Muted; dot.StrokeThickness = 1; }
            System.Windows.Controls.DockPanel.SetDock(dot, System.Windows.Controls.Dock.Left); cell.Children.Add(dot);
            var txt = new System.Windows.Controls.StackPanel();
            var row = new System.Windows.Controls.DockPanel();
            var gb = T(string.Format("{0:0.00} GB", p.Item2), 14, Fg, true); System.Windows.Controls.DockPanel.SetDock(gb, System.Windows.Controls.Dock.Right);
            row.Children.Add(gb); row.Children.Add(T(Tx.T("bd." + p.Item1), 14, Fg));
            txt.Children.Add(row); txt.Children.Add(T(p.Item4, 12, Muted));
            cell.Children.Add(txt); legend.Children.Add(cell);
        }
        root.Children.Add(legend);
        if (!m.Lists) root.Children.Add(T(Tx.T("bd.no_admin"), 12, Muted));

        // system facts on a card
        var card = new System.Windows.Controls.Border { Background = Card, BorderBrush = Line, BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(Theme.RLg), Padding = new W.Thickness(Theme.S4, Theme.S4, Theme.S4, Theme.S2), Margin = new W.Thickness(0, Theme.S2, 0, 0) };
        var facts = new System.Windows.Controls.StackPanel(); card.Child = facts;
        double commit = m.CommitLimit > 0 ? m.CommitUsed / m.CommitLimit : 0;
        Action<string, string, WM.Brush, string> fact = (name, value, vcol, note) => {
            var r = new System.Windows.Controls.DockPanel { Margin = new W.Thickness(0, 0, 0, Theme.S3) };
            var v = T(value, 14, vcol, true); System.Windows.Controls.DockPanel.SetDock(v, System.Windows.Controls.Dock.Right); r.Children.Add(v);
            var s = new System.Windows.Controls.StackPanel(); s.Children.Add(T(name, 14, Fg));
            if (!string.IsNullOrEmpty(note)) s.Children.Add(T(note, 12, Muted));
            r.Children.Add(s); facts.Children.Add(r);
        };
        fact(Tx.T("bd.reserved"), Tx.F("tile.ram_sub", m.CommitUsed, m.CommitLimit), commit >= 0.9 ? B(Theme.Crit) : Fg,
             commit >= 0.9 ? Tx.T("bd.reserved_near") : Tx.T("bd.reserved_ok"));
        fact(Tx.T("bd.procs"), m.Procs.ToString(), Fg, string.Format("node {0}   cmd {1}   conhost {2}", m.Node, m.Cmd, m.Conhost));
        fact(Tx.T("bd.left"), m.Leftovers.ToString(), m.Leftovers > 0 ? B(Theme.Hot) : Fg,
             m.Leftovers > 0 ? Tx.T("bd.left_yes") : Tx.T("bd.left_none"));
        fact(Tx.T("bd.mi"), m.Hvci ? Tx.T("bd.mi_on") : Tx.T("bd.mi_off"), Fg, m.Hvci ? Tx.T("bd.mi_note") : "");
        root.Children.Add(card);

        // tips, only when they apply
        var tips = new System.Collections.Generic.List<string>();
        if (m.Node > 30) tips.Add(Tx.T("bd.tip_node"));
        if (m.Hidden > m.Total * 0.25) tips.Add(Tx.T("bd.tip_hidden"));
        foreach (string tip in tips) {
            var tb = new System.Windows.Controls.Border { BorderBrush = B(Theme.Hot), BorderThickness = new W.Thickness(3, 0, 0, 0), Padding = new W.Thickness(Theme.S3, Theme.S1, Theme.S1, Theme.S1), Margin = new W.Thickness(0, Theme.S4, 0, 0) };
            tb.Child = T(tip, 13, Fg); root.Children.Add(tb);
        }

    }
}

