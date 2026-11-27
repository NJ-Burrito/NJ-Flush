// NJ-Flush panel: the RAM page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

partial class View {
    // ---- RAM ----
    void RamPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        var big = Header(body, 0, "", out sub);
        ActionRow(body, Tx.T("ram.flush"), 0, () => Do("ram", false), Settings.Keys_[1], Tx.T("ram.deep_note"),
                  Btn(Tx.T("ram.deep"), null, () => Do("ram", true), false));
        Graph(body, 0, out g, out g2, Tx.T("ram.graph"));
        var detail = new WC.StackPanel(); body.Children.Add(detail);
        var list = Section(body, Tx.T("ram.top")); hoverGuard = list;
        Action fill = () => { detail.Children.Clear(); BreakdownWindow.Build(detail, Mem.Snapshot(), false); };
        Action apps = () => {
            if (ListBusy()) return;
            List<ProcRow> rows; lock (Live.L) rows = Live.Rows;
            list.Children.Clear();
            foreach (var grp in rows.Where(r => r.Pid > 4).GroupBy(r => Procs.Short(r.Name)).OrderByDescending(x => x.Sum(r => r.Private)).Take(7)) {
                var topRow = grp.OrderByDescending(r => r.Private).First(); int n = grp.Count();
                list.Children.Add(Row(grp.Key, n > 1 ? Tx.Plural("process", n) : Tx.F("proc.pid", topRow.Pid), string.Format("{0:0.00} GB", grp.Sum(r => r.Private) / 1073741824.0), null,
                                      n == 1 && !Busy.Protected(topRow.Name) ? new W.UIElement[] { EndButton(topRow.Pid, grp.Key) } : new W.UIElement[0]));
            }
        };
        fill(); apps();
        tickPage = () => {
            double ram, used, total; lock (Live.L) { ram = Live.Ram; used = Live.RamUsed; total = Live.RamTotal; }
            big.Text = Tx.Pct(ram); big.Foreground = Scale(ram) ?? Ident[0];
            sub.Text = Tx.F("ram.sub_use", used, total) + "   ·   " +
                       (ram < Settings.MinRam ? Tx.F("ram.sub_fine", Settings.MinRam) : Tx.F("ram.sub_play", Tx.TierName(Overlay.TierOf(ram))));
            PlotG(g, Live.Ordered(Live.RamH));
            if (ticks % 3 == 0) apps();
            if (ticks % 15 == 0 && !detail.IsMouseOver) fill();   // the breakdown reads every process: every 15 s is enough
        };
    }
}
