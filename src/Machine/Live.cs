using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// the sampler: one background thread, once a second (RAM, CPU, VRAM), every 3 s the process snapshot, the busy rule and the guard
static class Live {
    public const int N = 60;
    public static readonly object L = new object();
    public static double Ram, RamUsed, RamTotal, Cpu, Vram, VramUsed, VramTotal, Shared, GpuLoad;
    public static string GpuName;
    public static readonly double[] RamH = new double[N], CpuH = new double[N], VramH = new double[N], GpuH = new double[N];
    public static int Head;                     // RamH[(Head + i) % N] is i seconds after the oldest sample
    public static List<ProcRow> Rows = new List<ProcRow>();
    public static Dictionary<int, double> CpuNow = new Dictionary<int, double>();   // pid -> cores
    public static List<BusyApp> BusyNow = new List<BusyApp>();
    public static List<GpuProc> GpuProcs = new List<GpuProc>();
    public static volatile bool WantDetail;     // the panel is open: GPU load and per-process GPU every second
    public static volatile bool WriteBusy;      // the widget owns busy.txt
    static Thread thread;

    // NJFLUSH_DEMO=1 (the off-screen renderer only): neutral example data instead of this PC's apps, hardware and numbers, so
    // public screenshots show nothing private. Read by Live, Mem.Snapshot, CpuMeter.Model, Eco.IsOn and Settings.Last.
    public static readonly bool Demo = Environment.GetEnvironmentVariable("NJFLUSH_DEMO") == "1";
    public static readonly HashSet<int> DemoEco = new HashSet<int> { 5120, 5124, 5128, 6200, 6204 };

    public static void Start() {
        if (thread != null) return;
        if (Demo) { FillDemo(); return; }
        thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "NJ-Flush sampler" };
        thread.Start();
    }
    static void FillDemo() {
        int s = Procs.MySession;
        Func<int, string, long, ProcRow> P = (pid, name, mb) => new ProcRow { Pid = pid, Parent = 1000, Session = s, Created = 1, Cpu = 0, Private = mb << 20, Name = name };
        var rows = new List<ProcRow> {
            P(4100, "chrome.exe", 1450), P(4104, "chrome.exe", 620), P(4108, "chrome.exe", 410), P(4112, "chrome.exe", 280),
            P(5120, "Spotify.exe", 310), P(5124, "Spotify.exe", 140), P(5128, "Spotify.exe", 90),
            P(5300, "Code.exe", 820), P(5304, "Code.exe", 360), P(5308, "node.exe", 240),
            P(6200, "Discord.exe", 390), P(6204, "Discord.exe", 160), P(6400, "Teams.exe", 520), P(6600, "explorer.exe", 180),
            P(6800, "dwm.exe", 150), P(7000, "Photoshop.exe", 1900), P(7200, "steam.exe", 260)
        };
        lock (L) {
            RamTotal = 32; RamUsed = 18.6; Ram = RamUsed / RamTotal * 100; Cpu = 23; GpuName = "NVIDIA GeForce RTX 4060";
            VramTotal = 8; VramUsed = 3.4; Vram = VramUsed / VramTotal * 100; Shared = 0.2; GpuLoad = 14;
            for (int i = 0; i < N; i++) {   // a believable minute: gentle waves, one CPU spike
                RamH[i] = 55 + 3 * Math.Sin(i / 9.0) + i * 0.06; CpuH[i] = 14 + 6 * Math.Sin(i / 4.0) + (i > 44 && i < 50 ? 38 - Math.Abs(i - 47) * 9 : 0);
                VramH[i] = 40 + 2 * Math.Sin(i / 7.0); GpuH[i] = 10 + 5 * Math.Abs(Math.Sin(i / 5.0));
            }
            Head = 0; Rows = rows;
            CpuNow = new Dictionary<int, double> { { 5308, 1.02 }, { 4100, 0.62 }, { 7000, 0.48 }, { 5300, 0.31 }, { 6200, 0.22 }, { 6800, 0.18 }, { 5120, 0.11 }, { 6400, 0.09 } };
            BusyNow = new List<BusyApp> { new BusyApp { Pid = 5308, Created = 1, Name = "node.exe", Cores = 1.02 } };
            GpuProcs = new List<GpuProc> { new GpuProc { Pid = 7000, VramGB = 1.4, Load = 9 }, new GpuProc { Pid = 4100, VramGB = 0.62, Load = 4 }, new GpuProc { Pid = 6800, VramGB = 0.48, Load = 2 },
                                           new GpuProc { Pid = 6200, VramGB = 0.21, Load = 1 }, new GpuProc { Pid = 5300, VramGB = 0.16, Load = 1 } };
        }
    }
    public static double[] Ordered(double[] h) { lock (L) { var r = new double[N]; for (int i = 0; i < N; i++) r[i] = h[(Head + i) % N]; return r; } }

    static void Loop() {
        var cpu = new CpuMeter(); var busy = new Busy(); string lastBusy = null; int tick = 0, session = Procs.MySession;
        var clock = Stopwatch.StartNew();
        while (true) {
            try {
                var m = new Mem.MemStatus(); Mem.Status(ref m);
                double c = cpu.Sample(), used, shared; bool g = Gpu.Vram(out used, out shared);
                double load = 0; List<GpuProc> gp = null;
                if (WantDetail && g) gp = Gpu.Processes(out load);
                List<ProcRow> rows = null;
                if (tick % 3 == 0 || WantDetail) {
                    rows = Procs.Snapshot();
                    if (tick % 3 == 0) {
                        busy.Update(rows, clock.Elapsed.Ticks);
                        Guard.Pass(rows, session);
                    }
                }
                lock (L) {
                    RamTotal = m.total / 1073741824.0; RamUsed = (m.total - m.avail) / 1073741824.0; Ram = RamTotal > 0 ? RamUsed / RamTotal * 100 : 0;
                    Cpu = c;
                    if (g) { VramUsed = used; Shared = shared; VramTotal = Gpu.Main != null ? Gpu.Main.TotalGB : 0; GpuName = Gpu.Main != null ? Gpu.Main.Name : null; }
                    Vram = g && VramTotal > 0 ? Math.Min(100, VramUsed / VramTotal * 100) : 0;
                    if (gp != null) { GpuProcs = gp; GpuLoad = load; }
                    RamH[Head] = Ram; CpuH[Head] = Cpu; VramH[Head] = Vram; GpuH[Head] = GpuLoad; Head = (Head + 1) % N;
                    if (rows != null) { Rows = rows; if (tick % 3 == 0) { CpuNow = new Dictionary<int, double>(busy.Now); BusyNow = busy.List(); } }
                }
                if (WriteBusy && tick % 3 == 0) {
                    string s = Busy.Format(BusyNow);
                    // rewritten on change, and touched every 9 s so the flush can tell a running widget from a stale file
                    if (s != lastBusy || tick % 9 == 0) { try { File.WriteAllText(Busy.File_, s); lastBusy = s; } catch (Exception) { } }
                }
            } catch (Exception) { }
            tick++;
            Thread.Sleep(1000);
        }
    }
}
