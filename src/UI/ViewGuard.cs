// NJ-Flush panel: the Efficiency guard page.
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
    // ---- efficiency guard: the real apps running now, each with a switch (Nader, round 2) ----
    void GuardPage(WC.StackPanel body) {
        WC.TextBlock sub;
        var big = Header(body, 3, "", out sub);
        var intro = T(Tx.T("guard.intro"), 13, Muted);
        intro.Margin = new W.Thickness(0, 10, 0, 6); body.Children.Add(intro);
        var on = Toggle(Tx.T("guard.keep"), Settings.Guard, x => Settings.Guard = x, Ident[3]);
        var bat = Toggle(Tx.T("guard.battery"), Settings.GuardBattery, x => Settings.GuardBattery = x, Ident[3]);
        on.Click += delegate { bat.IsEnabled = Settings.Guard; }; bat.IsEnabled = Settings.Guard;
        body.Children.Add(on); body.Children.Add(bat);
        // a filter box: find any running app
        var search = new WC.TextBox { Style = (W.Style)Styles["Field"] };
        var hint = T(Tx.T("guard.hint"), 13, Muted); hint.IsHitTestVisible = false; hint.Margin = new W.Thickness(13, 0, 0, 0); hint.VerticalAlignment = W.VerticalAlignment.Center; hint.Foreground = B(Theme.Dim);
        var sg = new WC.Grid { Margin = new W.Thickness(0, Theme.S5, 0, 0) }; sg.Children.Add(search); sg.Children.Add(hint); body.Children.Add(sg);
        var list = Section(body, Tx.T("guard.running"), Tx.T("guard.running_note"));
        hoverGuard = list;
        var goneTitle = new WC.TextBlock { Text = Tx.T("guard.gone"), FontSize = Theme.Body, Foreground = Muted, Margin = new W.Thickness(0, Theme.S4, 0, Theme.S2) };
        var gone = new WC.WrapPanel();
        body.Children.Add(goneTitle); body.Children.Add(gone);
        int session = Procs.MySession;
        Action fill = null;
        fill = () => {
            if (ListBusy()) return;
            List<ProcRow> rows; lock (Live.L) rows = Live.Rows;
            var mine = rows.Where(r => r.Session == session && r.Pid > 4 && r.Pid != Procs.MyPid && !Busy.Protected(r.Name)).ToList();
            var guarded = new HashSet<string>(Settings.GuardApps);
            var apps = mine.GroupBy(r => Procs.Short(r.Name).ToLowerInvariant())
                           .Select(x => new { Name = x.Key, Display = Procs.Short(x.First().Name), Pids = x.Select(r => r.Pid).ToList(), Eco = x.Count(r => Eco.IsOn(r.Pid)), Kept = guarded.Contains(x.Key) })
                           .ToList();
            string q = search.Text.Trim().ToLowerInvariant();
            var shown = (q.Length > 0 ? apps.Where(a => a.Name.Contains(q)) : apps.Where(a => a.Eco > 0 || a.Kept))
                        .OrderByDescending(a => a.Eco > 0).ThenByDescending(a => a.Kept).ThenBy(a => a.Name).Take(40).ToList();
            list.Children.Clear();
            foreach (var a in shown) {
                var app = a;
                var sw = new WC.CheckBox { IsChecked = app.Kept, Style = (W.Style)Styles["Toggle"], Tag = Ident[3], Content = "", ToolTip = Tx.F("guard.tip_keep", app.Display) };
                sw.Click += delegate {
                    if (sw.IsChecked == true) { if (!Settings.GuardApps.Contains(app.Name)) Settings.GuardApps.Add(app.Name); foreach (int pid in app.Pids) Eco.Set(pid, false); Say(Tx.F("guard.said_keep", app.Display)); }
                    else { Settings.GuardApps.Remove(app.Name); Say(Tx.F("guard.said_free", app.Display)); }
                    Panel.Applied(); Refresh(fill);
                };
                var info = new WC.WrapPanel();
                if (app.Eco > 0) info.Children.Add(Chip(app.Eco == app.Pids.Count ? Tx.T("chip.eco") : Tx.F("chip.eco_n", app.Eco, app.Pids.Count), "#FACC15"));
                if (app.Kept) info.Children.Add(Chip(Tx.T("chip.kept"), "#60A5FA"));
                var row = Row(app.Display, Tx.Plural("process", app.Pids.Count), null, null, sw);
                ((WC.StackPanel)row.Tag).Children.Add(info);
                list.Children.Add(row);
            }
            if (shown.Count == 0) list.Children.Add(Empty(Tx.T(q.Length > 0 ? "guard.none_q" : "guard.none")));
            gone.Children.Clear();
            foreach (string gn in Settings.GuardApps.Where(x => !apps.Any(a => a.Name == x)).ToList()) {
                string gname = gn;
                var x = Btn(gname + "   ×", null, () => { Settings.GuardApps.Remove(gname); Panel.Applied(); Say(Tx.F("guard.removed", gname)); Refresh(fill); }, false);
                x.MinHeight = 32; x.Padding = new W.Thickness(12, 2, 12, 2); x.FontSize = Theme.Small; x.Margin = new W.Thickness(0, 0, 6, 6); x.ToolTip = Tx.F("guard.remove_tip", gname);
                gone.Children.Add(x);
            }
            goneTitle.Visibility = gone.Visibility = gone.Children.Count > 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
            int ecoApps = apps.Count(a => a.Eco > 0);
            big.Text = ecoApps.ToString(); big.Foreground = ecoApps > 0 ? Amber : Ident[3];
            sub.Text = !Settings.Guard ? Tx.T("guard.off") : Guard.Paused ? Tx.T("guard.paused") :
                       Tx.F("guard.status", Tx.Plural("app", ecoApps), Settings.GuardApps.Count, Tx.Plural("process", Guard.Kept));
        };
        search.TextChanged += delegate { hint.Visibility = search.Text.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed; Refresh(fill); };
        fill();
        tickPage = () => { if (ticks % 3 == 0) fill(); };
    }
}
