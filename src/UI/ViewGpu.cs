// NJ-Flush panel: the GPU page.
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
    // ---- GPU ----
    void GpuPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        string name; lock (Live.L) name = Live.GpuName;
        var big = Header(body, 2, name ?? Tx.T("gpu.looking"), out sub);
        ActionRow(body, Tx.T("gpu.restart"), 2, () => Do("gpu", false), Settings.Keys_[3], Tx.T("gpu.restart_note"));
        var facts = T("", 13, Muted); facts.Margin = new W.Thickness(0, 6, 0, 0); body.Children.Add(facts);
        Graph(body, 2, out g, out g2, Tx.T("gpu.graph"));
        var list = Section(body, Tx.T("gpu.top")); hoverGuard = list;
        Action apps = () => {
            if (ListBusy()) return;
            List<GpuProc> gp; List<ProcRow> rows; lock (Live.L) { gp = Live.GpuProcs; rows = Live.Rows; }
            var names = rows.GroupBy(r => r.Pid).ToDictionary(x => x.Key, x => Procs.Short(x.First().Name));
            list.Children.Clear();
            foreach (var p in gp.Where(x => names.ContainsKey(x.Pid)).OrderByDescending(x => x.VramGB).Take(8)) {
                string nm = names[p.Pid]; bool sys = Busy.Protected(nm);
                list.Children.Add(Row(nm, Tx.F("gpu.row", p.Load, sys ? Tx.T("proc.system") : Tx.F("proc.pid", p.Pid)),
                                      p.VramGB >= 1 ? string.Format("{0:0.0} GB", p.VramGB) : string.Format("{0:0} MB", p.VramGB * 1024), null,
                                      sys ? new W.UIElement[0] : new W.UIElement[] { EndButton(p.Pid, nm) }));
            }
            if (list.Children.Count == 0) list.Children.Add(Empty(Tx.T("list.measuring")));
        };
        apps();
        tickPage = () => {
            double v, load, vu, vt, sh; string now;
            lock (Live.L) { v = Live.Vram; load = Live.GpuLoad; vu = Live.VramUsed; vt = Live.VramTotal; sh = Live.Shared; now = Live.GpuName; }
            if (name == null) { if (now != null) { current = -1; Select(2); } return; }   // the first GPU sample arrived: build the page for real
            big.Text = Tx.Pct(v); big.Foreground = Scale(v) ?? Ident[2];
            facts.Text = Tx.F("gpu.facts", vu, vt, load, sh);
            PlotG(g, Live.Ordered(Live.VramH)); PlotG(g2, Live.Ordered(Live.GpuH));
            if (ticks % 3 == 0) apps();
        };
    }
}
