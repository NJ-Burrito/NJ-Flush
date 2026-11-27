// NJ-Flush 1.3 (Nader 2026-11-27): the CPU, the GPU and the processes, measured cheaply, plus the efficiency guard.
//   Procs     one kernel call lists every process (name, parent, session, start time, CPU time, private memory): no handle each
//   CpuMeter  whole-machine CPU % from GetSystemTimes
//   Busy      the "runaway" rule a CPU flush calms: at least 90% of one core in every 3 s slice of the last 60 s
//   Eco       efficiency mode (EcoQoS + Idle priority, what Task Manager sets) on / off / read
//   Guard     keeps the apps on its list out of efficiency mode (design/1.3-ux.md)
//   Gpu       adapters (DXGI), VRAM and GPU load (PDH, locale-independent English counter names), and the device restart
//   Live      the sampler thread the widget and the panel read: 60 s of history for RAM, CPU, VRAM and GPU load
//   Hotkey    "Ctrl+Alt+F" <-> modifiers + key, for settings.ini and the settings window
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class ProcRow { public int Pid, Parent, Session; public long Created, Cpu, Private; public string Name; }

static class Procs {
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int pid, out int session);
    static IntPtr buf = IntPtr.Zero; static int size;
    static readonly object gate = new object();
    public static readonly int MyPid = Process.GetCurrentProcess().Id;
    public static int MySession { get { int s; return ProcessIdToSessionId(MyPid, out s) ? s : -1; } }

    // SYSTEM_PROCESS_INFORMATION, x64 layout (the shipped exe runs 64-bit; on 32-bit the list is empty and the CPU features rest)
    public static List<ProcRow> Snapshot() {
        var rows = new List<ProcRow>();
        if (IntPtr.Size != 8) return rows;
        lock (gate) {
            for (int tries = 0; ; tries++) {
                if (buf == IntPtr.Zero) { size = Math.Max(size, 1 << 20); buf = Marshal.AllocHGlobal(size); }
                int ret, st = NtQuerySystemInformation(5, buf, size, out ret);
                if (st == 0) break;
                if (st != unchecked((int)0xC0000004) || tries >= 4) return rows;  // STATUS_INFO_LENGTH_MISMATCH: grow and retry
                Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; size = Math.Max(ret + (256 << 10), size * 2);
            }
            long off = 0;
            while (true) {
                IntPtr p = new IntPtr(buf.ToInt64() + off);
                int next = Marshal.ReadInt32(p, 0);
                var r = new ProcRow {
                    Private = Marshal.ReadInt64(p, 8), Created = Marshal.ReadInt64(p, 32),
                    Cpu = Marshal.ReadInt64(p, 40) + Marshal.ReadInt64(p, 48),
                    Pid = (int)Marshal.ReadIntPtr(p, 80).ToInt64(), Parent = (int)Marshal.ReadIntPtr(p, 88).ToInt64(), Session = Marshal.ReadInt32(p, 100)
                };
                int len = Marshal.ReadInt16(p, 56) & 0xFFFF; IntPtr name = Marshal.ReadIntPtr(p, 64);
                r.Name = name == IntPtr.Zero || len == 0 ? (r.Pid == 0 ? "Idle" : "System") : Marshal.PtrToStringUni(name, len / 2);
                rows.Add(r);
                if (next == 0) break;
                off += next;
            }
        }
        return rows;
    }

    // "opera.exe" -> "opera"; the display name and the key the guard list uses
    public static string Short(string name) {
        name = name ?? ""; return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }

    public static bool End(int pid) { try { using (var p = Process.GetProcessById(pid)) { p.Kill(); return true; } } catch (Exception) { return false; } }
}

class CpuMeter {
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    long pIdle, pTotal;
    public CpuMeter() { Sample(); }
    public double Sample() {
        long i, k, u; if (!GetSystemTimes(out i, out k, out u)) return 0;
        long total = k + u, dt = total - pTotal, di = i - pIdle;  // kernel time includes idle time
        pIdle = i; pTotal = total;
        return Pct(dt, di);
    }
    public static double Pct(long dTotal, long dIdle) { return dTotal <= 0 ? 0 : Math.Max(0, Math.Min(100, (dTotal - dIdle) * 100.0 / dTotal)); }
    public static string Model() {
        if (Live.Demo) return "Intel Core i7-12700";
        try {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                return System.Text.RegularExpressions.Regex.Replace(((string)k.GetValue("ProcessorNameString", "")).Replace("(R)", "").Replace("(TM)", ""), @"\s+", " ").Trim();
        } catch (Exception) { return "CPU"; }
    }
}

class BusyApp { public int Pid; public long Created; public string Name; public double Cores; }

// a process is busy when it kept at least 90% of one core in every slice of the last minute (20 slices of 3 s)
class Busy {
    public const int Slices = 20;
    public const double MinCores = 0.9;
    class H { public string Name; public int Session; public long LastCpu; public readonly Queue<double> Cores = new Queue<double>(); }
    readonly Dictionary<string, H> hist = new Dictionary<string, H>();
    long lastWall;
    public readonly Dictionary<int, double> Now = new Dictionary<int, double>();   // pid -> cores used in the last slice

    static string Key(ProcRow r) { return r.Pid + ":" + r.Created; }

    public void Update(List<ProcRow> rows, long wallTicks) {
        long dWall = wallTicks - lastWall; lastWall = wallTicks;
        var seen = new HashSet<string>(); Now.Clear();
        foreach (var r in rows) {
            if (r.Pid == 0) continue;
            string k = Key(r); seen.Add(k);
            H h; bool had = hist.TryGetValue(k, out h);
            if (!had) { h = new H { Name = r.Name, Session = r.Session, LastCpu = r.Cpu }; hist[k] = h; continue; }
            double cores = dWall > 0 ? Math.Max(0, (r.Cpu - h.LastCpu) / (double)dWall) : 0;
            h.LastCpu = r.Cpu; h.Cores.Enqueue(cores); while (h.Cores.Count > Slices) h.Cores.Dequeue();
            Now[r.Pid] = cores;
        }
        foreach (var k in hist.Keys.Where(k => !seen.Contains(k)).ToList()) hist.Remove(k);
    }

    public List<BusyApp> List() {
        var list = new List<BusyApp>();
        foreach (var kv in hist) {
            if (!Sustained(kv.Value.Cores, Slices)) continue;
            string[] p = kv.Key.Split(':');
            list.Add(new BusyApp { Pid = int.Parse(p[0]), Created = long.Parse(p[1]), Name = kv.Value.Name, Cores = kv.Value.Cores.Average() });
        }
        return list.OrderByDescending(b => b.Cores).ToList();
    }

    public static bool Sustained(IEnumerable<double> cores, int slices) {
        var a = cores.ToArray(); return a.Length >= slices && a.All(c => c >= MinCores);
    }

    // never calmed: system and shell processes, other sessions (services), NJ-Flush, guarded apps, and the app in front
    static readonly string[] Never = { "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
                                       "explorer", "audiodg", "fontdrvhost", "sihost", "ctfmon", "taskmgr", "searchhost", "startmenuexperiencehost",
                                       "shellexperiencehost", "textinputhost", "nj-flush", "conhost", "msmpeng", "memory compression" };
    // system and shell processes: the panel never offers Calm or End for them
    public static bool Protected(string name) { return Never.Contains(Procs.Short(name).ToLowerInvariant()); }
    public static bool MayCalm(string name, int session, int mySession, int pid, int myPid, string frontName, ICollection<int> frontTree, ICollection<string> guarded) {
        string n = Procs.Short(name).ToLowerInvariant();
        if (pid == myPid || pid <= 4 || session != mySession) return false;
        if (Never.Contains(n)) return false;
        if (guarded != null && guarded.Contains(n)) return false;
        if (frontName != null && n == Procs.Short(frontName).ToLowerInvariant()) return false;
        if (frontTree != null && frontTree.Contains(pid)) return false;
        return true;
    }

    // the app in front and every process under it (a game's helpers, a browser's renderers)
    public static HashSet<int> Tree(List<ProcRow> rows, int root) {
        var set = new HashSet<int>(); if (root <= 0) return set;
        set.Add(root);
        for (bool grew = true; grew; ) {
            grew = false;
            foreach (var r in rows) if (!set.Contains(r.Pid) && set.Contains(r.Parent) && r.Pid != r.Parent) { set.Add(r.Pid); grew = true; }
        }
        return set;
    }

    // the widget hands the busy list to the elevated flush through busy.txt (pid|created|name|cores), rewritten only on change
    public static readonly string File_ = Path.Combine(App.Dir, "busy.txt");
    public static string Format(List<BusyApp> list) {
        var sb = new StringBuilder();
        foreach (var b in list) sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3:0.00}\r\n", b.Pid, b.Created, b.Name, b.Cores);
        return sb.ToString();
    }
    public static List<BusyApp> Parse(IEnumerable<string> lines) {
        var list = new List<BusyApp>();
        foreach (string l in lines) {
            var p = l.Split('|'); int pid; long cr; double c;
            if (p.Length == 4 && int.TryParse(p[0], out pid) && long.TryParse(p[1], out cr) &&
                double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c))
                list.Add(new BusyApp { Pid = pid, Created = cr, Name = p[2], Cores = c });
        }
        return list;
    }
    // older than 15 s = the widget is not running: no data, so the CPU flush calms nothing rather than guess
    public static List<BusyApp> Read() {
        try {
            if ((DateTime.Now - File.GetLastWriteTime(File_)).TotalSeconds > 15) return null;
            return Parse(File.ReadAllLines(File_));
        } catch (Exception) { return null; }
    }
}

static class Eco {
    [StructLayout(LayoutKind.Sequential)] struct Throttle { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GetProcessInformation(IntPtr h, int cls, ref Throttle t, int size);
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int cls, ref Throttle t, int size);
    [DllImport("kernel32.dll")] static extern uint GetPriorityClass(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetPriorityClass(IntPtr h, uint c);
    const int PowerThrottling = 4; const uint ExecutionSpeed = 1, Idle = 0x40, Normal = 0x20, BelowNormal = 0x4000;
    const uint QueryLimited = 0x1000, SetInfo = 0x0200;

    // efficiency mode = EcoQoS on (what Task Manager's leaf shows); measured on Nader's PC 2026-11-27: 12 Opera processes, Mattermost,
    // Steam and VS Code had it, with Idle priority
    public static bool IsOn(int pid) {
        if (Live.Demo) return Live.DemoEco.Contains(pid);
        IntPtr h = OpenProcess(QueryLimited, false, pid); if (h == IntPtr.Zero) return false;
        try { var t = new Throttle { Version = 1 }; return GetProcessInformation(h, PowerThrottling, ref t, 12) && On(t.ControlMask, t.StateMask); }
        finally { CloseHandle(h); }
    }
    public static bool On(uint control, uint state) { return (control & ExecutionSpeed) != 0 && (state & ExecutionSpeed) != 0; }

    public static bool Set(int pid, bool on) {
        IntPtr h = OpenProcess(QueryLimited | SetInfo, false, pid); if (h == IntPtr.Zero) return false;
        try {
            // off = EcoQoS explicitly off (Windows does not put it back on its own); on = EcoQoS + Idle, as Task Manager does
            var t = new Throttle { Version = 1, ControlMask = ExecutionSpeed, StateMask = on ? ExecutionSpeed : 0 };
            bool ok = SetProcessInformation(h, PowerThrottling, ref t, 12);
            uint pc = GetPriorityClass(h);
            if (on) SetPriorityClass(h, Idle); else if (pc == Idle || pc == BelowNormal) SetPriorityClass(h, Normal);
            return ok;
        } finally { CloseHandle(h); }
    }
}

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

class GpuProc { public int Pid; public double VramGB, Load; }

static class Gpu {
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref int size, out int count, IntPtr items);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
    [StructLayout(LayoutKind.Sequential)] struct Item { public IntPtr Name; public uint Status; public double Value; }
    const uint FmtDouble = 0x200, NoCap = 0x8000;

    public class Adapter { public string Name, Luid, Vendor, Device; public double TotalGB; }
    public static Adapter Main;      // the adapter with the most dedicated memory (the Parsec / Basic Render virtual ones have none)
    static IntPtr qA, cDed, cShared, qP, cProc, cEng;
    static readonly object gate = new object();
    static DateTime retryAt = DateTime.MinValue;   // no adapter found: look again in 30 s, not every second

    // DXGI through its vtable (no COM wrappers in C# 5): IDXGIFactory1::EnumAdapters1 = slot 12, IDXGIAdapter1::GetDesc1 = slot 10
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumFn(IntPtr self, uint i, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DescFn(IntPtr self, out Desc1 d);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Desc1 {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision; public UIntPtr Dedicated, DedicatedSystem, SharedSystem; public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    static T Slot<T>(IntPtr obj, int slot) where T : class {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), typeof(T));
    }
    public static List<Adapter> Adapters() {
        var list = new List<Adapter>();
        try {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); IntPtr f;
            if (CreateDXGIFactory1(ref iid, out f) != 0) return list;
            try {
                for (uint i = 0; ; i++) {
                    IntPtr a; if (Slot<EnumFn>(f, 12)(f, i, out a) != 0) break;
                    try {
                        Desc1 d; if (Slot<DescFn>(a, 10)(a, out d) != 0) continue;
                        if ((d.Flags & 2) != 0) continue;  // DXGI_ADAPTER_FLAG_SOFTWARE
                        list.Add(new Adapter { Name = d.Description, Luid = LuidName((uint)d.LuidHigh, d.LuidLow), TotalGB = d.Dedicated.ToUInt64() / 1073741824.0,
                                               Vendor = d.VendorId.ToString("X4"), Device = d.DeviceId.ToString("X4") });
                    } finally { Slot<ReleaseFn>(a, 2)(a); }
                }
            } finally { Slot<ReleaseFn>(f, 2)(f); }
        } catch (Exception) { }
        return list;
    }
    public static string LuidName(uint high, uint low) { return string.Format("luid_0x{0:x8}_0x{1:x8}", high, low); }
    public static Adapter Pick(List<Adapter> list) { return list.Where(a => a.TotalGB > 0.25).OrderByDescending(a => a.TotalGB).FirstOrDefault(); }

    // PDH instance names: "pid_2000_luid_0x00000000_0x00012f7e_phys_0[_eng_0_engtype_3D]"
    public static int PidOf(string inst) {
        if (inst == null || !inst.StartsWith("pid_")) return -1;
        int end = inst.IndexOf('_', 4); int pid;
        return end > 4 && int.TryParse(inst.Substring(4, end - 4), out pid) ? pid : -1;
    }
    public static bool OnAdapter(string inst, string luid) { return inst != null && luid != null && inst.IndexOf(luid, StringComparison.OrdinalIgnoreCase) >= 0; }
    public static string EngType(string inst) { int i = inst == null ? -1 : inst.IndexOf("_engtype_"); return i < 0 ? "" : inst.Substring(i + 9); }

    static List<KeyValuePair<string, double>> Read(IntPtr c, uint fmt) {
        var r = new List<KeyValuePair<string, double>>();
        int size = 0, count;
        PdhGetFormattedCounterArray(c, fmt, ref size, out count, IntPtr.Zero);
        if (size <= 0) return r;
        IntPtr b = Marshal.AllocHGlobal(size);
        try {
            if (PdhGetFormattedCounterArray(c, fmt, ref size, out count, b) != 0) return r;
            int step = Marshal.SizeOf(typeof(Item));
            for (int i = 0; i < count; i++) {
                var it = (Item)Marshal.PtrToStructure(new IntPtr(b.ToInt64() + i * step), typeof(Item));
                if (it.Status <= 1) r.Add(new KeyValuePair<string, double>(Marshal.PtrToStringUni(it.Name), it.Value));  // PDH_CSTATUS_VALID_DATA / NEW_DATA
            }
        } finally { Marshal.FreeHGlobal(b); }
        return r;
    }

    // whole-adapter VRAM (dedicated and shared), cheap: two counters on the adapter instances only
    public static bool Vram(out double usedGB, out double sharedGB) {
        usedGB = sharedGB = 0;
        lock (gate) {
            if (Main == null) {
                if (DateTime.Now < retryAt) return false;
                Main = Pick(Adapters()); if (Main == null) { retryAt = DateTime.Now.AddSeconds(30); return false; }
            }
            if (qA == IntPtr.Zero) {
                if (PdhOpenQuery(null, IntPtr.Zero, out qA) != 0) { qA = IntPtr.Zero; return false; }
                PdhAddEnglishCounter(qA, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out cDed);
                PdhAddEnglishCounter(qA, @"\GPU Adapter Memory(*)\Shared Usage", IntPtr.Zero, out cShared);
            }
            if (PdhCollectQueryData(qA) != 0) return false;
            bool found = false;
            foreach (var kv in Read(cDed, FmtDouble)) if (OnAdapter(kv.Key, Main.Luid)) { usedGB += kv.Value / 1073741824.0; found = true; }
            foreach (var kv in Read(cShared, FmtDouble)) if (OnAdapter(kv.Key, Main.Luid)) sharedGB += kv.Value / 1073741824.0;
            if (!found) { Main = null; PdhCloseQuery(qA); qA = IntPtr.Zero; retryAt = DateTime.Now.AddSeconds(5); }  // the LUID changes when the device restarts: find it again next time
            return found;
        }
    }

    // per process: VRAM and GPU load (load = the busiest engine type, as Task Manager shows it); the first call gives load 0
    public static List<GpuProc> Processes(out double totalLoad) {
        totalLoad = 0;
        var by = new Dictionary<int, GpuProc>();
        lock (gate) {
            if (Main == null) return new List<GpuProc>();
            if (qP == IntPtr.Zero) {
                if (PdhOpenQuery(null, IntPtr.Zero, out qP) != 0) { qP = IntPtr.Zero; return new List<GpuProc>(); }
                PdhAddEnglishCounter(qP, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out cProc);
                PdhAddEnglishCounter(qP, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out cEng);
            }
            if (PdhCollectQueryData(qP) != 0) return new List<GpuProc>();
            Func<int, GpuProc> get = pid => { GpuProc g; if (!by.TryGetValue(pid, out g)) by[pid] = g = new GpuProc { Pid = pid }; return g; };
            foreach (var kv in Read(cProc, FmtDouble)) { int pid = PidOf(kv.Key); if (pid > 0 && OnAdapter(kv.Key, Main.Luid)) get(pid).VramGB += kv.Value / 1073741824.0; }
            var perType = new Dictionary<string, double>(); var perPid = new Dictionary<string, double>();
            foreach (var kv in Read(cEng, FmtDouble | NoCap)) {
                int pid = PidOf(kv.Key); if (pid <= 0 || !OnAdapter(kv.Key, Main.Luid)) continue;
                string t = EngType(kv.Key), k = pid + "|" + t; double v;
                perType[t] = (perType.TryGetValue(t, out v) ? v : 0) + kv.Value;
                perPid[k] = (perPid.TryGetValue(k, out v) ? v : 0) + kv.Value;
            }
            foreach (var kv in perPid) { var g = get(int.Parse(kv.Key.Split('|')[0])); g.Load = Math.Max(g.Load, Math.Min(100, kv.Value)); }
            totalLoad = perType.Count == 0 ? 0 : Math.Min(100, perType.Values.Max());
        }
        return by.Values.ToList();
    }

    // Owner choice 2026-11-27: restart the display device (the sent Win+Ctrl+Shift+B is ignored by Windows). Needs the elevated task.
    // The screen goes black for a few seconds; apps that use the GPU recreate their surfaces, compute (CUDA) apps may fail.
    public static string Restart() {
        var a = Main ?? Pick(Adapters());
        if (a == null) return Tx.T("reason.no_gpu");
        string id = null;
        try {
            using (var q = new System.Management.ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController"))
                foreach (System.Management.ManagementObject o in q.Get()) {
                    string pnp = (string)o["PNPDeviceID"] ?? "";
                    if (pnp.IndexOf("VEN_" + a.Vendor, StringComparison.OrdinalIgnoreCase) >= 0 && pnp.IndexOf("DEV_" + a.Device, StringComparison.OrdinalIgnoreCase) >= 0) id = pnp;
                }
        } catch (Exception) { }
        if (id == null) return Tx.T("reason.no_device");
        var sb = new StringBuilder();
        int rc = App.Run(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), "/restart-device \"" + id + "\"", sb);
        lock (gate) { Main = null; if (qA != IntPtr.Zero) { PdhCloseQuery(qA); qA = IntPtr.Zero; } if (qP != IntPtr.Zero) { PdhCloseQuery(qP); qP = IntPtr.Zero; } }
        return rc == 0 ? null : Tx.F(rc == 5 ? "reason.pnp_admin" : "reason.pnp", rc);
    }
}

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

// "Ctrl+Alt+F" <-> (modifiers, key). Modifiers: 1 Alt, 2 Ctrl, 4 Shift, 8 Win (RegisterHotKey's bits). Empty = no shortcut.
static class Hotkeys {
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
    public static bool Parse(string s, out uint mods, out uint key) {
        mods = 0; key = 0;
        if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) return true;   // none
        foreach (string raw in s.Split('+')) {
            string p = raw.Trim().ToLowerInvariant();
            if (p == "ctrl" || p == "control") mods |= Ctrl; else if (p == "alt") mods |= Alt; else if (p == "shift") mods |= Shift; else if (p == "win") mods |= Win;
            else {
                if (key != 0) return false;
                System.Windows.Forms.Keys k;
                if (p.Length == 1 && char.IsDigit(p[0])) p = "d" + p;
                if (!Enum.TryParse(p, true, out k) || k == System.Windows.Forms.Keys.None) return false;
                key = (uint)k;
            }
        }
        return key != 0 && mods != 0;   // a global shortcut needs a modifier, or it would steal a plain key from every app
    }
    public static string Format(uint mods, uint key) {
        if (key == 0) return "";
        var parts = new List<string>();
        if ((mods & Ctrl) != 0) parts.Add("Ctrl"); if ((mods & Alt) != 0) parts.Add("Alt"); if ((mods & Shift) != 0) parts.Add("Shift"); if ((mods & Win) != 0) parts.Add("Win");
        var k = (System.Windows.Forms.Keys)key; string n = k.ToString();
        if (k >= System.Windows.Forms.Keys.D0 && k <= System.Windows.Forms.Keys.D9) n = n.Substring(1);
        parts.Add(n);
        return string.Join("+", parts);
    }
    public static string Normal(string s) { uint m, k; return Parse(s, out m, out k) ? Format(m, k) : null; }
}
