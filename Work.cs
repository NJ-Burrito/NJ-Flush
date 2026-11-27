// NJ-Flush 1.3: one flush job, whatever its kind (ram | cpu | gpu | all), run by the elevated task.
// 2026-11-27 freeze report (Desktop\NJ-Flush FREEZE-REPORT.md, tools\pc-freeze\report.md): the old RAM flush (empty every working
// set, write the modified list, purge the whole standby cache, kill Explorer) turned a slow-disk stall into hard freezes on this PC.
// The RAM flush is now gentle by default: nothing below Settings.MinRam, the low-priority cache only (command 5), idle background
// apps trimmed one by one, Explorer restart off by default and graceful (Restart Manager) when on. The old one is "Deep flush".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class FlushRequest { public string Kind = "all"; public bool Auto, Deep, Force; }   // force: the RAM flush runs under MinRam (tests, A/B runs)

class Flush {
    public static readonly string ReqFile = Path.Combine(App.Dir, "request.txt");
    public static readonly string LogFile = Path.Combine(App.Dir, "flush.log");
    static readonly string[] Kinds = { "all", "ram", "cpu", "gpu" };

    // "<kind> [auto] [deep]"; anything unreadable is All
    public static FlushRequest ParseRequest(string text) {
        var r = new FlushRequest();
        foreach (string w in (text ?? "").ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            if (Kinds.Contains(w)) r.Kind = w; else if (w == "auto") r.Auto = true; else if (w == "deep") r.Deep = true; else if (w == "force") r.Force = true;
        }
        return r;
    }
    public static void Ask(string kind, bool auto, bool deep = false) {
        try { Directory.CreateDirectory(App.Dir); File.WriteAllText(ReqFile, kind + (auto ? " auto" : "") + (deep ? " deep" : "")); } catch (Exception) { }
    }
    // read once and remove; a request older than a minute is stale (a task that never ran), so it is All
    public static FlushRequest TakeRequest() {
        try {
            if (!File.Exists(ReqFile)) return new FlushRequest();
            bool fresh = (DateTime.Now - File.GetLastWriteTime(ReqFile)).TotalSeconds < 60;
            string t = File.ReadAllText(ReqFile); File.Delete(ReqFile);
            return fresh ? ParseRequest(t) : new FlushRequest();
        } catch (Exception) { return new FlushRequest(); }
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    public readonly FlushRequest Req;
    public readonly int Tier, FrontPid;
    public readonly double RamPct, AvailGB, VramPct, VramUsed;
    public readonly string FrontName;
    public readonly bool FullScreen, HasGpu;
    public volatile string Result;
    readonly HashSet<string> done = new HashSet<string>();
    public bool IsDone(string part) { lock (done) return done.Contains(part); }
    void Mark(string part) { lock (done) done.Add(part); }

    public Flush(FlushRequest req) {
        Req = req;
        var m = new Mem.MemStatus(); Mem.Status(ref m);
        RamPct = m.total > 0 ? (m.total - m.avail) * 100.0 / m.total : 0; AvailGB = m.avail / 1073741824.0;
        Tier = Overlay.TierOf(RamPct);
        double shared; HasGpu = Gpu.Vram(out VramUsed, out shared);
        VramPct = HasGpu && Gpu.Main != null && Gpu.Main.TotalGB > 0 ? VramUsed / Gpu.Main.TotalGB * 100 : 0;
        // the app in front, taken before any overlay shows: it is never calmed or trimmed, and a full-screen one blocks the GPU restart
        IntPtr fg = GetForegroundWindow(); int pid; GetWindowThreadProcessId(fg, out pid); FrontPid = pid;
        try { FrontName = Process.GetProcessById(pid).ProcessName; } catch (Exception) { }
        var cls = new StringBuilder(64); GetClassName(fg, cls, 64);
        RECT r; var scr = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
        FullScreen = fg != IntPtr.Zero && cls.ToString() != "WorkerW" && cls.ToString() != "Progman" && cls.ToString() != "Shell_TrayWnd" &&
                     GetWindowRect(fg, out r) && r.L <= scr.Left && r.T <= scr.Top && r.R >= scr.Right && r.B >= scr.Bottom;
    }

    public bool Has(string part) { return Req.Kind == "all" || Req.Kind == part; }
    public bool RamNeeded { get { return Req.Deep || Req.Force || RamNeededAt(RamPct, Settings.MinRam); } }
    public static bool RamNeededAt(double pct, int min) { return pct >= min; }
    // GPU: always when asked for by name; in Flush all only on a Heavy/Clogged tier or VRAM 85%+, never on an auto-flush or under a full-screen app
    public string GpuSkip { get { return GpuSkipFor(Req, Settings.GpuReset, HasGpu, Tier, VramPct, FullScreen); } }
    public static string GpuSkipFor(FlushRequest q, bool setting, bool hasGpu, int tier, double vramPct, bool fullScreen) {
        if (q.Kind == "gpu") return hasGpu ? null : "no graphics card with its own memory";
        if (q.Kind != "all") return "not asked";
        if (!setting) return "off in Settings";
        if (!hasGpu) return "no graphics card with its own memory";
        if (q.Auto) return "never on an auto-flush";
        if (fullScreen) return "a full-screen app is in front";
        if (tier < 2 && vramPct < 85) return "not needed";
        return null;
    }
    public bool CpuPlanned { get { return Req.Kind == "cpu" || (Req.Kind == "all" && Settings.CalmCpu); } }

    // the whole job; the overlay runs it on a worker thread and waits for Result
    public string Run() {
        Action unpin = Mem.Pin();
        // lines: what the overlay shows; log: English, for flush.log; last: the line kept for "Last flush", in the language in force now
        var lines = new List<string>(); var log = new List<string>(); var last = new List<string>();
        try {
            if (Has("gpu")) {
                string skip = GpuSkip;
                if (skip == null) GpuPart(lines, log, last);
                else { log.Add("GPU skipped: " + skip); last.Add(Tx.F("last.gpu_skipped", Reason(skip))); if (Req.Kind == "gpu" || skip.StartsWith("a full")) lines.Add(Tx.F("res.gpu_not", Reason(skip))); }
            }
            Mark("gpu");
            if (Has("cpu")) { if (CpuPlanned) CpuPart(lines, log, last); else { log.Add("CPU skipped: off in Settings"); last.Add(Tx.T("last.cpu_off")); } }
            Mark("cpu");
            if (Has("ram")) {
                if (RamNeeded) { string l, ll; lines.Add(RamPart(out l, out ll)); log.Add(l); last.Add(ll); }
                else {
                    lines.Add(Tx.F("res.ram_fine", RamPct, AvailGB));
                    log.Add(string.Format("RAM skipped: {0:0}% in use, under {1}%", RamPct, Settings.MinRam));
                    last.Add(Tx.F("last.ram_skipped", RamPct, Settings.MinRam));
                }
            }
            Mark("ram");
        } catch (Exception e) { lines.Add(Tx.F("res.stopped", e.Message)); log.Add("error " + e.Message); last.Add(Tx.F("last.error", e.Message)); }
        finally { unpin(); }
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        try {
            File.WriteAllText(Settings.LastFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "  " + string.Join(Tx.T("sep").Replace(",", ";").Replace("،", "؛"), last));
            File.AppendAllText(LogFile, stamp + "  " + Req.Kind + (Req.Auto ? " auto" : "") + (Req.Deep ? " deep" : "") +
                               string.Format("  RAM {0:0}%", RamPct) + "  " + string.Join("; ", log) + "\r\n");
        } catch (Exception) { }
        return Result = string.Join("\n", lines.Take(3));
    }

    // the English reason a part was skipped (the tests and flush.log keep these) -> the language in force; unknown text is shown as it is
    public static string Reason(string en) {
        switch (en) {
            case "no graphics card with its own memory": return Tx.T("reason.no_gpu");
            case "the graphics card's device was not found": return Tx.T("reason.no_device");
            case "not asked": return Tx.T("reason.not_asked");
            case "off in Settings": return Tx.T("reason.off");
            case "never on an auto-flush": return Tx.T("reason.auto");
            case "a full-screen app is in front": return Tx.T("reason.fullscreen");
            case "not needed": return Tx.T("reason.not_needed");
        }
        return en;
    }

    void GpuPart(List<string> lines, List<string> log, List<string> last) {
        double before = VramUsed, after = 0, shared;
        string err = Gpu.Restart();
        if (err != null) { lines.Add(Tx.F("res.gpu_not", Reason(err))); log.Add("GPU restart failed: " + err); last.Add(Tx.F("last.gpu_failed", Reason(err))); return; }
        bool back = false;
        for (int i = 0; i < 16 && !back; i++) { Thread.Sleep(500); back = Gpu.Vram(out after, out shared); }  // the device comes back with a new LUID
        lines.Add(back ? Tx.F("res.gpu_ok", before, after) : Tx.T("res.gpu_ok2"));
        log.Add(back ? string.Format("GPU restarted, VRAM {0:0.00} -> {1:0.00} GB", before, after) : "GPU restarted");
        last.Add(back ? Tx.F("last.gpu_ok", before, after) : Tx.T("last.gpu_ok2"));
    }

    void CpuPart(List<string> lines, List<string> log, List<string> last) {
        var busy = Busy.Read();
        if (busy == null) { lines.Add(Tx.T("res.cpu_nodata")); log.Add("CPU: no busy.txt"); last.Add(Tx.T("last.cpu_nodata")); return; }
        var rows = Procs.Snapshot(); int session = Procs.MySession;
        var front = Busy.Tree(rows, FrontPid); var guarded = new HashSet<string>(Settings.GuardApps);
        var calmed = new List<string>();
        foreach (var b in busy) {
            var r = rows.FirstOrDefault(x => x.Pid == b.Pid && x.Created == b.Created); if (r == null) continue;   // gone, or the PID was reused
            if (!Busy.MayCalm(r.Name, r.Session, session, r.Pid, Procs.MyPid, FrontName, front, guarded)) continue;
            if (Eco.Set(r.Pid, true)) calmed.Add(Procs.Short(r.Name));
        }
        if (calmed.Count == 0) { lines.Add(Tx.T("res.cpu_none")); log.Add("CPU: none calmed"); last.Add(Tx.T("last.cpu_none")); return; }
        string names = string.Join(", ", calmed.Distinct().Take(3)) + (calmed.Distinct().Count() > 3 ? "..." : "");
        lines.Add(Tx.F("res.cpu_calmed", Tx.Plural("busyapp", calmed.Count), names));
        log.Add("CPU calmed " + string.Join(",", calmed));
        last.Add(Tx.F("last.cpu_calmed", string.Join(Tx.T("sep"), calmed)));
    }

    // gentle by default (freeze report 2026-11-27); Deep = the 1.2 flush
    string RamPart(out string log, out string last) {
        double before = Mem.UsedGB();
        int leftovers = Orphans.Reap();
        var used = new List<string>(); var extra = new List<string>();   // used: English, for flush.log; extra: the same facts for "Last flush"
        string trimmed = "";
        if (Req.Deep) { used.Add(Mem.Purge(new[] { 2, 3, 4, 5 })); }
        else {
            int n; double gb; Trim(out n, out gb);
            trimmed = string.Format("trimmed {0} idle apps", n);
            used.Add(trimmed); used.Add(Mem.Purge(new[] { 5 })); extra.Add(Tx.F("last.trimmed", n));
        }
        if (Settings.RestartExplorer) { Thread.Sleep(2000); bool ex = Mem.RestartExplorerGently(); used.Add(ex ? "Explorer restarted" : "Explorer not restarted"); extra.Add(Tx.T(ex ? "last.explorer_ok" : "last.explorer_no")); }
        double after = Mem.UsedGB();
        if (leftovers > 0) extra.Add(Tx.F("last.leftover", leftovers));
        last = Tx.F(Req.Deep ? "last.ram_deep" : "last.ram", before, after) + (extra.Count > 0 ? " (" + Tx.Join(extra) + ")" : "");
        log = string.Format("RAM{0} {1:0.0} -> {2:0.0} GB ({3}){4}", Req.Deep ? " deep" : "", before, after, string.Join(", ", used),
                            leftovers > 0 ? ", stopped " + leftovers + " leftover helpers" : "");
        return Tx.F("res.freed", Math.Max(0, before - after), before, after)
               + (leftovers > 0 ? "\n" + Tx.F("res.leftover", leftovers) : "");
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    static readonly string[] NeverTrim = { "explorer", "dwm", "csrss", "audiodg", "sihost", "ctfmon", "startmenuexperiencehost", "shellexperiencehost",
                                           "searchhost", "textinputhost", "fontdrvhost", "winlogon", "lsass", "system", "registry", "memory compression", "nj-flush" };
    public static bool MayTrim(string name, int session, int mySession, double cores, long privateBytes, bool inFront) {
        return session == mySession && !inFront && cores < 0.02 && privateBytes >= (64L << 20) && !NeverTrim.Contains(Procs.Short(name).ToLowerInvariant());
    }
    // idle background apps of this session (under 2% of a core over 0.7 s, 64 MB private or more), one at a time
    void Trim(out int n, out double gb) {
        n = 0; gb = 0;
        var a = Procs.Snapshot(); var clock = Stopwatch.StartNew(); Thread.Sleep(700);
        var b = Procs.Snapshot(); double wall = clock.Elapsed.Ticks;
        var front = Busy.Tree(b, FrontPid); int session = Procs.MySession;
        var prev = a.GroupBy(x => x.Pid).ToDictionary(g => g.Key, g => g.First());
        foreach (var r in b) {
            ProcRow p; if (!prev.TryGetValue(r.Pid, out p) || p.Created != r.Created) continue;
            double cores = (r.Cpu - p.Cpu) / wall;
            if (!MayTrim(r.Name, r.Session, session, cores, r.Private, front.Contains(r.Pid) || r.Pid == Procs.MyPid)) continue;
            IntPtr h = OpenProcess(0x0400 | 0x0100, false, r.Pid);  // QUERY_INFORMATION | SET_QUOTA
            if (h == IntPtr.Zero) continue;
            try { if (EmptyWorkingSet(h)) { n++; gb += r.Private / 1073741824.0; } } finally { CloseHandle(h); }
            Thread.Sleep(5);  // one at a time, so the disk never sees a burst
        }
    }
}
