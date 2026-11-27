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

class MemInfo {
    public double Total, InUse, Apps, Kernel, Modified, Hidden, Standby, Free, CommitUsed, CommitLimit;
    public bool Lists, Hvci;
    public int Procs, Node, Cmd, Conhost, Leftovers;
}

static class Mem {
    [DllImport("ntdll.dll")] static extern int RtlAdjustPrivilege(int p, bool enable, bool thread, out bool was);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, [Out] IntPtr[] buf, int len, out int ret);
    [StructLayout(LayoutKind.Sequential)]
    public struct MemStatus { public uint len, load; public ulong total, avail, tPage, aPage, tVirt, aVirt, aExt; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
    [StructLayout(LayoutKind.Sequential)]
    struct PerfInfo { public uint cb; public IntPtr commitTotal, commitLimit, commitPeak, physTotal, physAvail, sysCache, kTotal, kPaged, kNonpaged, pageSize; public uint handles, procs, threads; }
    [DllImport("psapi.dll")] static extern bool GetPerformanceInfo(out PerfInfo pi, int size);

    const double GB = 1073741824.0;

    public static bool Status(ref MemStatus m) { m.len = (uint)Marshal.SizeOf(typeof(MemStatus)); return GlobalMemoryStatusEx(ref m); }

    public static double UsedGB() { var m = new MemStatus(); Status(ref m); return (m.total - m.avail) / GB; }

    static void Privileges() { bool was; RtlAdjustPrivilege(13, true, false, out was); RtlAdjustPrivilege(5, true, false, out was); }

    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSizeEx(IntPtr h, IntPtr min, IntPtr max, uint flags);
    [DllImport("kernel32.dll")] static extern bool GetProcessWorkingSetSizeEx(IntPtr h, out IntPtr min, out IntPtr max, out uint flags);

    // 1.2.1: keep this process resident during "empty all working sets": the animation and its sound otherwise page back in from
    // disk frame by frame on a full machine. Best effort (needs SeIncreaseWorkingSetPrivilege, which the elevated task has).
    static Action PinSelf() {
        try {
            bool was; RtlAdjustPrivilege(33, true, false, out was);
            var me = Process.GetCurrentProcess(); me.Refresh();
            IntPtr min0, max0; uint f0;
            if (!GetProcessWorkingSetSizeEx(me.Handle, out min0, out max0, out f0)) return () => { };
            long min = me.WorkingSet64 + (32L << 20), max = Math.Max(min * 2, max0.ToInt64());
            if (!SetProcessWorkingSetSizeEx(me.Handle, new IntPtr(min), new IntPtr(max), 0x1 | 0x8)) return () => { };  // HARDWS_MIN_ENABLE | HARDWS_MAX_DISABLE
            return () => { try { SetProcessWorkingSetSizeEx(me.Handle, min0, max0, 0x2 | 0x8); } catch (Exception) { } };  // MIN_DISABLE
        } catch (Exception) { return () => { }; }
    }

    public static Action Pin() { return PinSelf(); }

    // NtSetSystemInformation(SystemMemoryListInformation): 2 empty working sets, 3 flush modified, 4 purge standby, 5 purge low-priority standby
    public static string Purge(int[] commands) {
        Privileges();
        var ok = new System.Collections.Generic.List<int>();
        foreach (int c in commands) { int cmd = c; if (NtSetSystemInformation(80, ref cmd, 4) == 0) ok.Add(c); }
        return ok.Count == commands.Length ? "commands " + string.Join(",", commands) : "commands " + string.Join(",", ok) + " of " + string.Join(",", commands) + " (no admin rights?)";
    }

    // 1.3: Explorer is closed through Restart Manager (it saves its state and comes back), never killed: a kill in the middle of
    // a page-fault storm was part of the 11-27 freezes. Started by hand only if it does not come back.
    [StructLayout(LayoutKind.Sequential)] struct RmProcess { public int Pid; public System.Runtime.InteropServices.ComTypes.FILETIME Start; }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint h, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint h, uint nFiles, string[] files, uint nApps, RmProcess[] apps, uint nSvc, string[] svc);
    [DllImport("rstrtmgr.dll")] static extern int RmShutdown(uint h, uint flags, IntPtr cb);
    [DllImport("rstrtmgr.dll")] static extern int RmRestart(uint h, int reserved, IntPtr cb);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint h);
    public static bool RestartExplorerGently() {
        var ex = Process.GetProcessesByName("explorer");
        if (ex.Length == 0) return false;
        uint h; var key = new StringBuilder(64);
        if (RmStartSession(out h, 0, key) != 0) return false;
        bool ok = false;
        try {
            var apps = ex.Select(p => { long t = p.StartTime.ToFileTime(); return new RmProcess { Pid = p.Id, Start = new System.Runtime.InteropServices.ComTypes.FILETIME { dwLowDateTime = (int)(t & 0xFFFFFFFF), dwHighDateTime = (int)(t >> 32) } }; }).ToArray();
            if (RmRegisterResources(h, 0, null, (uint)apps.Length, apps, 0, null) == 0 && RmShutdown(h, 0, IntPtr.Zero) == 0) { ok = true; RmRestart(h, 0, IntPtr.Zero); }
        } catch (Exception) { }
        finally { RmEndSession(h); }
        for (int i = 0; i < 40 && Process.GetProcessesByName("explorer").Length == 0; i++) Thread.Sleep(150);
        if (Process.GetProcessesByName("explorer").Length == 0) try { Process.Start("explorer.exe"); } catch (Exception) { }
        return ok;
    }

    // RAMMap-style snapshot from the kernel's own page lists (locale-independent, no extra tools); shown by BreakdownWindow
    public static MemInfo Snapshot() {
        if (Live.Demo) return new MemInfo { Total = 32, InUse = 18.6, Apps = 13.2, Kernel = 1.9, Modified = 0.3, Hidden = 3.2, Standby = 9.1, Free = 4.3,
                                            CommitUsed = 24.5, CommitLimit = 52, Lists = true, Hvci = true, Procs = 214, Node = 6, Cmd = 3, Conhost = 9, Leftovers = 0 };
        var m = new MemStatus(); Status(ref m);
        var r = new MemInfo { Total = m.total / GB, InUse = (m.total - m.avail) / GB };
        double page = Environment.SystemPageSize;
        Privileges();
        var buf = new IntPtr[22]; int ret;
        r.Lists = NtQuerySystemInformation(80, buf, buf.Length * IntPtr.Size, out ret) == 0;
        Func<int, int, double> pages = (from, count) => { double s = 0; for (int i = from; i < from + count; i++) s += buf[i].ToInt64(); return s * page / GB; };
        if (r.Lists) { r.Free = pages(0, 2); r.Modified = pages(2, 2); r.Standby = pages(5, 8); }
        var procs = Process.GetProcesses();
        foreach (var p in procs) try { r.Apps += p.WorkingSet64 / GB; } catch (Exception) { }
        r.Apps = Math.Min(r.Apps, r.InUse);  // working sets share pages: never show more than is in use
        PerfInfo pi;
        if (GetPerformanceInfo(out pi, Marshal.SizeOf(typeof(PerfInfo))))
            r.Kernel = (pi.kPaged.ToInt64() + pi.kNonpaged.ToInt64()) * (double)pi.pageSize.ToInt64() / GB;
        r.Hidden = Math.Max(0, r.InUse - r.Apps - r.Kernel - r.Modified);
        // 2026-11-23: reserved (commit) memory, process counts, leftovers, Memory Integrity: what RAMMap showed behind the "fake" RAM
        r.CommitUsed = (m.tPage - m.aPage) / GB; r.CommitLimit = m.tPage / GB;
        Func<string, int> nOf = n => procs.Count(p => string.Equals(p.ProcessName, n, StringComparison.OrdinalIgnoreCase));
        r.Procs = procs.Length; r.Node = nOf("node"); r.Cmd = nOf("cmd"); r.Conhost = nOf("conhost");
        try { r.Leftovers = Orphans.Find().Count; } catch (Exception) { }
        try {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
                r.Hvci = k != null && Convert.ToInt32(k.GetValue("Enabled", 0)) == 1;
        } catch (Exception) { }
        return r;
    }
}
