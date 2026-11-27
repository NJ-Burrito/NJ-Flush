using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Guard {
    public static readonly string[] Browsers = { "msedge", "chrome", "opera", "brave", "firefox", "vivaldi" };
    public static int Kept;          // processes taken out of efficiency mode since the widget started
    public static bool Paused;       // on battery with "pause on battery"

    [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AC, Battery, Percent, Saver; public int Life, Full; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);
    public static bool OnBattery() { PowerStatus s; return GetSystemPowerStatus(out s) && s.AC == 0; }

    // one pass over a snapshot: every listed process in efficiency mode gets it turned off. Returns how many it changed.
    public static int Pass(List<ProcRow> rows, int mySession) {
        Paused = Settings.GuardBattery && OnBattery();
        if (!Settings.Guard || Paused) return 0;
        var list = new HashSet<string>(Settings.GuardApps);
        int n = 0;
        foreach (var r in rows)
            if (r.Session == mySession && list.Contains(Procs.Short(r.Name).ToLowerInvariant()) && Eco.IsOn(r.Pid) && Eco.Set(r.Pid, false)) n++;
        Kept += n;
        return n;
    }
}
