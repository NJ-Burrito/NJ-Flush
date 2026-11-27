// NJ-Flush panel: the CPU page.
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
    // ---- CPU ----
    void CpuPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        var big = Header(body, 1, CpuMeter.Model() + "   ·   " + Tx.F("threads", Environment.ProcessorCount), out sub);
        ActionRow(body, Tx.T("cpu.calm"), 1, () => Do("cpu", false), Settings.Keys_[2], Tx.T("cpu.calm_note"));
        var busyLine = T("", 13, Muted); busyLine.Margin = new W.Thickness(0, 6, 0, 0); body.Children.Add(busyLine);
        Graph(body, 1, out g, out g2, Tx.T("cpu.graph"));
        var list = Section(body, Tx.T("cpu.top")); hoverGuard = list;
        int n = Environment.ProcessorCount;
        Action apps = null;
        apps = () => {
            if (ListBusy()) return;
            Dictionary<int, double> now; List<ProcRow> rows; List<BusyApp> busy;
            lock (Live.L) { now = Live.CpuNow; rows = Live.Rows; busy = Live.BusyNow; }
            busyLine.Text = busy.Count == 0 ? Tx.T("cpu.busy_none") : Tx.F("cpu.busy_list", Tx.Join(busy.Select(b => Procs.Short(b.Name)).Distinct().Take(4)));
            busyLine.Foreground = busy.Count == 0 ? Muted : Amber;
            list.Children.Clear();
            var names = rows.GroupBy(r => r.Pid).ToDictionary(x => x.Key, x => x.First().Name);
            foreach (var kv in now.Where(x => names.ContainsKey(x.Key) && x.Key > 4).OrderByDescending(x => x.Value).Take(8)) {
                int pid = kv.Key; string name = Procs.Short(names[pid]); bool eco = Eco.IsOn(pid), isBusy = busy.Any(b => b.Pid == pid), sys = Busy.Protected(names[pid]);
                var dp = new List<string>(); if (eco) dp.Add(Tx.T("cpu.eco")); if (isBusy) dp.Add(Tx.T("cpu.busy"));
                string detail = sys ? Tx.T("proc.system") : Tx.Join(dp);
                W.UIElement[] right = sys ? new W.UIElement[0] : new W.UIElement[] {
                    eco ? Btn(Tx.T("cpu.restore"), null, () => { Eco.Set(pid, false); Say(Tx.F("cpu.restored", name)); Refresh(apps); }, false)
                        : Btn(Tx.T("cpu.calm_btn"), null, () => { Eco.Set(pid, true); Say(Tx.F("cpu.calmed", name)); Refresh(apps); }, false),
                    EndButton(pid, name) };
                list.Children.Add(Row(name, detail.Length > 0 ? detail : Tx.F("proc.pid", pid), string.Format("{0:0.0}%", kv.Value / n * 100), isBusy ? Amber : null, right));
            }
            if (list.Children.Count == 0) list.Children.Add(Empty(Tx.T("list.measuring")));
        };
        apps();
        tickPage = () => {
            double cpu; lock (Live.L) cpu = Live.Cpu;
            big.Text = Tx.Pct(cpu); big.Foreground = Scale(cpu) ?? Ident[1];
            PlotG(g, Live.Ordered(Live.CpuH));
            if (ticks % 3 == 0) apps();
        };
    }
}
