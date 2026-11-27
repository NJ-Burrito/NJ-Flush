using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
