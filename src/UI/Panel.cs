// NJ-Flush 1.3 panel (Nader 2026-11-27: "the widget opens a list like Task Manager for CPU, GPU and RAM; flush each or all";
// round 2: "design the settings and tabs better, smoother transitions using NJ-Kit; the flush button on top; the guard must show
// the real processes; a shortcut can be cleared"). Layout: tiles on the left like Task Manager's Performance tab (Jakob's law),
// the page on the right with its flush button first, Flush all as the largest target at the bottom (Fitts), Settings as the
// last tile. Motion (NJ-Kit nj-ui-motion-laws): a page enters with a 200 ms fade and a 10 px rise (L9, L6 ease-out), tiles and
// switches change colour in 150 ms, presses squash to 97% (L1), nothing animates on the once-a-second refresh (L20), lists
// never rebuild under the mouse, every action answers in the bar at once (L17); Windows "Show animations" off = no motion.
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

static class Panel {
    static W.Window win;
    static View view;
    static W.Threading.Dispatcher disp;
    public static string StartPage;   // /panel ram|cpu|gpu|guard|settings

    // from the widget: one panel, on its own UI thread, so the widget keeps sampling and its shortcuts keep working
    // reload: rebuild the page shown (a double-click on NJ-Flush.exe while the panel is open)
    public static void Open(string page = null, bool reload = false) {
        if (disp != null && win != null) {
            disp.BeginInvoke((Action)delegate {
                if (win.WindowState == W.WindowState.Minimized) win.WindowState = W.WindowState.Normal;
                win.Activate(); win.Topmost = true; win.Topmost = false;   // to the front even when another app has focus
                if (page != null) view.Go(page); else if (reload) view.Reload();
            });
            return;
        }
        StartPage = page;
        var t = new Thread(Run) { IsBackground = true, Name = "NJ-Flush panel" };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }

    // standalone (NJ-Flush.exe /panel [page], /settings) or the panel thread
    public static void Run() {
        disp = W.Threading.Dispatcher.CurrentDispatcher;
        Live.WantDetail = true;
        view = new View(); win = view.Window;
        win.Closed += delegate {
            Live.WantDetail = false; win = null; view = null; disp = null;
            W.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(W.Threading.DispatcherPriority.Background);
        };
        win.Show(); win.Activate();
        W.Threading.Dispatcher.Run();
    }

    // why a flush would be refused right now, or null (the same rules the widget and the task use)
    public static string Refusal(string kind, bool deep) {
        int left = Settings.CooldownLeft(kind);
        if (left > 0) return Tx.F("refusal.cooldown", left);
        double ram; lock (Live.L) ram = Live.Ram;
        if (kind == "ram" && !deep && ram < Settings.MinRam) return Tx.F("refusal.ram_fine", ram, Settings.MinRam);
        return null;
    }

    // settings changed in the panel: saved at once, and the widget (its own thread) applies them
    public static void Applied() {
        Settings.Save(); Tx.Resolve();
        var w = Widget.Current; if (w != null) try { w.BeginInvoke((Action)w.ApplySettings); } catch (Exception) { }
    }
}
