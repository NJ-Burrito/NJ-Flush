// NJ-Flush: one self-installing file.
//   double-click        -> first time: copies itself to %LOCALAPPDATA%\NJ-Flush and registers an elevated task (one UAC prompt);
//                          every time: runs the task, so no prompt.
//   /flush  (the task)  -> dimmed full-screen toilet animation + sound while it restarts File Explorer and flushes RAM
//                          (empty working sets, flush modified list, purge standby cache), then starts the widget.
//   /preview <tier>     -> plays one tier's animation (light|medium|heavy|clogged) without flushing.
//   /widget             -> tiny RAM widget above the clock: live "XX%  used/total GB", dim until hovered,
//                          double-click or Ctrl+Alt+F = flush; right-click = flush, breakdown, settings, previews, uninstall.
//   /uninstall          -> (elevated) removes the task; the widget menu runs it.
//   /settings           -> the settings window on its own.   /update -> install the latest release quietly.
//   /breakdown -> the memory breakdown window on its own.
// Settings: %LOCALAPPDATA%\NJ-Flush\settings.ini (animation, sound, restart Explorer, auto-flush, start with Windows).
// Build: csc /target:winexe /win32icon:NJ-Flush.ico /resource:flush-<tier>.wav x4 (+ WPF refs), see build.cmd.
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NJFlush.Tests")]  // tests/NJFlush.Tests only

static class App {
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NJ-Flush");
    public static readonly string Exe = Path.Combine(Dir, "NJ-Flush.exe");
    const string TaskName = "NJ-Flush";

    [STAThread]
    static int Main(string[] args) {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        Settings.Load();
        if (mode == "/widget") { Widget.Start(); return 0; }
        if (mode == "/flush") {
            if (Settings.CooldownLeft() > 0) return 0;   // a flush ran moments ago: repeated triggers never stack (Nader 2026-11-23)
            Settings.MarkFlush();
            if (Settings.Animation) Overlay.Run(null); else Mem.Flush();
            StartWidget(); return 0;
        }
        if (mode == "/preview") {  // light|medium|heavy|clogged [solid], no flush; "solid" = opaque backdrop for screen recordings
            Overlay.Solid = args.Any(a => a.ToLowerInvariant() == "solid");
            Overlay.Run(args.Length > 1 ? args[1] : "medium"); return 0;
        }
        if (mode == "/settings") { Application.EnableVisualStyles(); Application.Run(new SettingsForm(null)); return 0; }  // standalone (no widget)
        if (mode == "/update") {  // quiet update: 0 updated, 1 already latest, 2 GitHub unreachable, 3 refused (checksum / file)
            var u = Updater.Check(); if (u.Error != null) return 2; if (!u.Newer) return 1; return Updater.Apply(u) == null ? 0 : 3;
        }
        if (mode == "/breakdown") { BreakdownWindow.Show(FlushNow); return 0; }
        if (mode == "/uninstall") return Run("schtasks.exe", "/delete /tn " + TaskName + " /f", null);
        if (mode == "/install") return Install() ? 0 : 1;
        return Launch();
    }

    static int Launch() {
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(self, Exe, StringComparison.OrdinalIgnoreCase)) CopySelf(self);
        int left = Settings.CooldownLeft();
        if (left > 0) {
            MessageBox.Show(string.Format("NJ-Flush just flushed. The next flush is available in {0} s.", left), "NJ-Flush",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        // fast path: the task exists -> one schtasks call (the separate /query cost ~0.5 s on every double-click)
        if (TaskOk(fast: true)) return 0;
        if (!TaskOk()) {
            try {
                using (var p = Process.Start(new ProcessStartInfo(Exe, "/install") { Verb = "runas", UseShellExecute = true })) {
                    p.WaitForExit();
                    if (p.ExitCode != 0) { MessageBox.Show("NJ-Flush could not register its task.", "NJ-Flush"); return 1; }
                }
            } catch (System.ComponentModel.Win32Exception) {
                MessageBox.Show("NJ-Flush needs admin approval once, the first time, to install.", "NJ-Flush");
                return 1;
            }
        }
        Run("schtasks.exe", "/run /tn " + TaskName, null);
        return 0;
    }

    // copy (or update) this file into the install folder; a running copy is renamed out of the way first
    static void CopySelf(string self) {
        try {
            Directory.CreateDirectory(Dir);
            if (File.Exists(Exe) && File.ReadAllBytes(Exe).SequenceEqual(File.ReadAllBytes(self))) return;
            try { File.Copy(self, Exe, true); }
            catch (IOException) {
                string old = Exe + ".old";
                try { File.Delete(old); } catch (Exception) { }
                File.Move(Exe, old);
                File.Copy(self, Exe, true);
            }
        } catch (Exception) { }  // keep whatever copy is installed
    }

    static bool TaskOk(bool fast = false) {
        if (fast) return Run("schtasks.exe", "/run /tn " + TaskName, null) == 0;  // runs it when it exists
        var sb = new StringBuilder();
        return Run("schtasks.exe", "/query /tn " + TaskName + " /xml", sb) == 0 &&
               sb.ToString().IndexOf(Exe, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // elevated: on-demand task for the current user, highest privileges, interactive (so the animation shows)
    static bool Install() {
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            " <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
            " <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
            "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
            " <Actions Context=\"Author\"><Exec><Command>" +
            System.Security.SecurityElement.Escape(Exe) + "</Command><Arguments>/flush</Arguments></Exec></Actions>\n" +
            "</Task>";
        string tmp = Path.Combine(Path.GetTempPath(), "nj-flush-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        int rc = Run("schtasks.exe", "/create /tn " + TaskName + " /xml \"" + tmp + "\" /f", null);
        try { File.Delete(tmp); } catch (Exception) { }
        return rc == 0;
    }

    static void StartWidget() {
        foreach (string name in new[] { "NJ-Widget", "NJ-Flush" })  // NJ-Widget: the older two-file version
            foreach (var p in Process.GetProcessesByName(name))
                if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); p.WaitForExit(2000); } catch (Exception) { }
        Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "/widget") { UseShellExecute = false });
    }

    public static int Run(string file, string args, StringBuilder output) {
        var psi = new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = output != null };
        using (var p = Process.Start(psi)) {
            if (output != null) output.Append(p.StandardOutput.ReadToEnd());
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    // never on the caller's thread: under full RAM starting schtasks takes seconds, and the widget froze on the double-click (1.2.1)
    public static void FlushNow() { if (Settings.CooldownLeft() == 0) new Thread(() => Run("schtasks.exe", "/run /tn " + TaskName, null)) { IsBackground = true }.Start(); }

    // removes the task (one elevated call), the autostart entry and, after this process exits, the install folder
    public static void Uninstall() {
        try {
            using (var p = Process.Start(new ProcessStartInfo(Exe, "/uninstall") { Verb = "runas", UseShellExecute = true })) p.WaitForExit();
        } catch (System.ComponentModel.Win32Exception) { return; }  // UAC declined: nothing removed
        Settings.SetAutostart(false);
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}

static class Settings {
    static readonly string File_ = Path.Combine(App.Dir, "settings.ini");
    public static readonly string LastFile = Path.Combine(App.Dir, "last.txt");
    public static bool Animation = true, Sound = true, RestartExplorer = true, AutoFlush = false, Hotkey = true, AutoUpdate = true;
    public static int Volume = 100, IdleOpacity = 40;  // volume: % of the mixed level (itself <= 10% full scale); widget brightness when idle
    public static int AutoAt = 90;  // auto-flush when RAM in use stays at or above this % for 30 s, at most once per 10 min
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Load() {
        try { Parse(File.ReadAllLines(File_)); } catch (Exception) { }  // no file yet: defaults
    }
    // settings.ini lines; an unknown key or an out-of-range number keeps the current value (tests: tests/NJFlush.Tests)
    public static void Parse(System.Collections.Generic.IEnumerable<string> lines) {
            foreach (string line in lines) {
                int i = line.IndexOf('='); if (i < 0) continue;
                string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                bool b = v == "1" || v.ToLowerInvariant() == "true"; int n;
                if (k == "animation") Animation = b; else if (k == "sound") Sound = b;
                else if (k == "restartexplorer") RestartExplorer = b; else if (k == "autoflush") AutoFlush = b;
                else if (k == "autoat" && int.TryParse(v, out n) && n >= 50 && n <= 99) AutoAt = n;
                else if (k == "cooldown" && int.TryParse(v, out n) && n >= 10 && n <= 600) Cooldown = n;
                else if (k == "hotkey") Hotkey = b;
                else if (k == "autoupdate") AutoUpdate = b;
                else if (k == "volume" && int.TryParse(v, out n) && n >= 0 && n <= 100) Volume = n;
                else if (k == "idleopacity" && int.TryParse(v, out n) && n >= 20 && n <= 100) IdleOpacity = n;
            }
    }
    public static void Save() {
        try {
            Directory.CreateDirectory(App.Dir);
            File.WriteAllText(File_, string.Format("animation={0}\r\nsound={1}\r\nrestartexplorer={2}\r\nautoflush={3}\r\nautoat={4}\r\ncooldown={5}\r\n" +
                "hotkey={6}\r\nvolume={7}\r\nidleopacity={8}\r\nautoupdate={9}\r\n",
                Animation ? 1 : 0, Sound ? 1 : 0, RestartExplorer ? 1 : 0, AutoFlush ? 1 : 0, AutoAt, Cooldown, Hotkey ? 1 : 0, Volume, IdleOpacity, AutoUpdate ? 1 : 0));
        } catch (Exception) { }
    }
    public static bool Autostart() {
        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("NJ-Flush") != null;
    }
    public static void SetAutostart(bool on) {
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) {
            if (on) k.SetValue("NJ-Flush", "\"" + App.Exe + "\" /widget"); else k.DeleteValue("NJ-Flush", false);
        }
    }
    public static string Last() { try { return File.ReadAllText(LastFile).Trim(); } catch (Exception) { return "no flush yet"; } }

    // cooldown: seconds after a flush starts before the next may run (settings.ini cooldown=, 10-600, default 60)
    public static int Cooldown = 60;
    static readonly string StartFile = Path.Combine(App.Dir, "flush.at");
    public static void MarkFlush() { try { Directory.CreateDirectory(App.Dir); File.WriteAllText(StartFile, DateTime.Now.Ticks.ToString()); } catch (Exception) { } }
    public static int CooldownLeft() {
        try { return CooldownLeft(long.Parse(File.ReadAllText(StartFile).Trim()), DateTime.Now, Cooldown); } catch (Exception) { return 0; }
    }
    public static int CooldownLeft(long startTicks, DateTime now, int cooldown) {
        double since = (now - new DateTime(startTicks)).TotalSeconds;
        return since < 0 ? 0 : Math.Max(0, (int)Math.Ceiling(cooldown - since));  // clock moved back: do not lock
    }
}

// Leftover helper processes (2026-11-23, RAMMap on Nader's PC: 5.2 GB of page tables, three full sets of Claude MCP helper servers still
// running from sessions closed a day earlier). A process tree is a leftover only when ALL hold: its root is node.exe / cmd.exe, its parent
// process no longer exists, it started more than 6 hours ago, and its command line names a known helper (npx, Playwright MCP / test-server,
// context7, knip, flint, NJ-QA, business-wrap). Its children (conhost, node) go with it. Anything else is never touched.
static class Orphans {
    static readonly string[] Helpers = { "npx-cli.js", @"@playwright\mcp", "@playwright/mcp", "context7-mcp", @"@knip\mcp", "@knip/mcp", "flint-chart-mcp",
                                         @"business-wrap\mcp", "business-wrap/mcp", @"NJ-QA\mcp", "NJ-QA/mcp", "test-server -c", "mcp-server" };
    const double MinAgeHours = 6;

    public static System.Collections.Generic.List<int> Find() {
        var rows = new System.Collections.Generic.List<ManagementObject>();
        using (var q = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate FROM Win32_Process"))
            foreach (ManagementObject o in q.Get()) rows.Add(o);
        var live = new System.Collections.Generic.HashSet<uint>(rows.Select(o => (uint)o["ProcessId"]));
        var kids = rows.GroupBy(o => (uint)o["ParentProcessId"]).ToDictionary(g => g.Key, g => g.Select(o => (uint)o["ProcessId"]).ToList());
        var found = new System.Collections.Generic.List<int>();
        foreach (var o in rows) {
            string cd = (string)o["CreationDate"];
            if (!IsLeftover((string)o["Name"], live.Contains((uint)o["ParentProcessId"]), cd == null ? (DateTime?)null : ManagementDateTimeConverter.ToDateTime(cd), DateTime.Now, (string)o["CommandLine"])) continue;
            var stack = new System.Collections.Generic.Stack<uint>(); stack.Push((uint)o["ProcessId"]);
            while (stack.Count > 0) {
                uint id = stack.Pop(); if (found.Contains((int)id)) continue; found.Add((int)id);
                System.Collections.Generic.List<uint> c; if (kids.TryGetValue(id, out c)) foreach (var k in c) stack.Push(k);
            }
        }
        return found;
    }

    // the whole rule for a leftover root (anything else is never stopped): node/cmd, parent gone, older than 6 h, a known helper
    public static bool IsLeftover(string name, bool parentAlive, DateTime? created, DateTime now, string cmd) {
        name = (name ?? "").ToLowerInvariant(); cmd = cmd ?? "";
        if (name != "node.exe" && name != "cmd.exe") return false;
        if (parentAlive || created == null) return false;
        if ((now - created.Value).TotalHours < MinAgeHours) return false;
        return Helpers.Any(h => cmd.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static int Reap() {
        int n = 0;
        try { foreach (int id in Find()) try { Process.GetProcessById(id).Kill(); n++; } catch (Exception) { } } catch (Exception) { }
        return n;
    }
}

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

    static double UsedGB() { var m = new MemStatus(); Status(ref m); return (m.total - m.avail) / GB; }

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

    public static string Flush(bool keepSelf = false, bool explorer = true) {
        Action unpin = keepSelf ? PinSelf() : () => { };
        try { return FlushCore(explorer); } finally { unpin(); }
    }

    static string FlushCore(bool explorer) {
        double before = UsedGB();
        int leftovers = Orphans.Reap();  // 2026-11-23: leftover helper processes first (their page tables and RAM come back)
        if (explorer && Settings.RestartExplorer) RestartExplorer();
        Privileges();
        int failed = 0;
        foreach (int c in new[] { 2, 3, 4, 5 }) {  // empty working sets, flush modified, purge standby, purge low-priority standby
            int cmd = c;
            if (NtSetSystemInformation(80, ref cmd, 4) != 0) failed++;
        }
        double after = UsedGB();
        string line = failed >= 3 ? "failed (no admin rights)" : string.Format("freed {0:0.00} GB ({1:0.0} -> {2:0.0} GB)", before - after, before, after);
        try { File.WriteAllText(Settings.LastFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + line); } catch (Exception) { }
        return failed >= 3 ? "Flush failed: no admin rights"
                           : string.Format("Freed {0:0.00} GB   ({1:0.0} GB -> {2:0.0} GB)", before - after, before, after)
                             + (leftovers > 0 ? string.Format("\nStopped {0} leftover helper processes", leftovers) : "");
    }

    // Kill File Explorer; Windows restarts the shell itself (as the normal user). Start it only if it does not come back.
    public static void RestartExplorer() {
        var ex = Process.GetProcessesByName("explorer");
        if (ex.Length == 0) return;
        foreach (var p in ex) try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
        for (int i = 0; i < 40 && Process.GetProcessesByName("explorer").Length == 0; i++) Thread.Sleep(150);
        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
    }

    // RAMMap-style snapshot from the kernel's own page lists (locale-independent, no extra tools); shown by BreakdownWindow
    public static MemInfo Snapshot() {
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

// Overlay: four flush tiers by RAM in use when the flush starts (Nader 2026-11-23): 70 light, 80 medium, 90 heavy, 96 clogged.
// Motion built on nj-ui-motion-laws (L1 squash, L2 anticipation, L3 one motion at a time, L5 follow-through, L6 easing, L7 arcs,
// L8 secondary action, L10 exaggerated feedback, L26 peak-end) and nj-animation-direction (beats = pressure changes, holds never frozen).
// Sound (nj-real-sound): one premixed soundtrack per tier from real Mixkit recordings (make_sfx.py), started with the clock, so every
// sound sits on its cue. Change a cue time here -> change it in make_sfx.py.
// Windows "Show animations" off -> no motion, the tier and the result still show (reduced motion keeps the feedback).
static class Overlay {
    public static int FPS = 30;
    public static bool Solid;  // opaque backdrop: screen recordings never show the desktop behind
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr device, uint volume);  // this process's volume (SoundPlayer)
    const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent' Topmost='True' ShowInTaskbar='False'
        SizeToContent='WidthAndHeight' WindowStartupLocation='CenterScreen' ResizeMode='NoResize'>
  <!-- 1.2.2 (Nader: super light): the window is only as big as the art, not a full-screen layered window, and nothing is blurred
       (a DropShadowEffect on the animated canvas was re-blurred every frame). Solid (recording) mode is still full screen. -->
  <Grid x:Name='Root' Opacity='0'>
    <!-- Nader 2026-11-23: a lighter dim (not a blackout), no glow behind the toilet; solid (recording) mode = opaque + glow -->
    <Rectangle x:Name='Dim' Fill='#05080F' Opacity='0.45' Visibility='Collapsed'/>
    <Ellipse x:Name='GlowDisc' Width='1100' Height='1100' IsHitTestVisible='False' Opacity='0' Visibility='Collapsed'>
      <Ellipse.Fill><RadialGradientBrush>
        <GradientStop x:Name='Glow' Color='#3322D3EE' Offset='0'/><GradientStop Color='#0022D3EE' Offset='1'/>
      </RadialGradientBrush></Ellipse.Fill>
    </Ellipse>
    <!-- 1.2.3: fixed width, so the longer result line never widens the window and shifts the toilet right -->
    <StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Margin='40' Width='560'>
      <Viewbox Height='460' RenderTransformOrigin='0.5,0.5'>
        <Viewbox.RenderTransform><ScaleTransform x:Name='Pop' ScaleX='0.6' ScaleY='0.6'/></Viewbox.RenderTransform>
        <Canvas Width='300' Height='440'>
          <Canvas.RenderTransform><TranslateTransform x:Name='Shake'/></Canvas.RenderTransform>
          <Canvas.Resources>
            <LinearGradientBrush x:Key='Porcelain' StartPoint='0,0.5' EndPoint='1,0.5'>
              <GradientStop Color='#B9C6D1' Offset='0'/><GradientStop Color='#F8FAFC' Offset='0.32'/>
              <GradientStop Color='#FFFFFF' Offset='0.45'/><GradientStop Color='#DCE4EA' Offset='0.78'/><GradientStop Color='#A7B5C1' Offset='1'/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key='Chrome' StartPoint='0,0' EndPoint='1,1'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#CBD5E1' Offset='0.35'/>
              <GradientStop Color='#64748B' Offset='0.55'/><GradientStop Color='#E2E8F0' Offset='1'/>
            </LinearGradientBrush>
          </Canvas.Resources>

          <!-- floor shadow, pedestal -->
          <Ellipse CacheMode='BitmapCache' Canvas.Left='30' Canvas.Top='398' Width='240' Height='34'>
            <Ellipse.Fill><RadialGradientBrush><GradientStop Color='#99000000' Offset='0'/><GradientStop Color='#00000000' Offset='1'/></RadialGradientBrush></Ellipse.Fill>
          </Ellipse>
          <Path CacheMode='BitmapCache' Data='M92,320 C100,365 92,392 78,412 L222,412 C208,392 200,365 208,320 Z' Fill='{StaticResource Porcelain}'/>
          <Rectangle CacheMode='BitmapCache' Canvas.Left='76' Canvas.Top='406' Width='148' Height='8' RadiusX='4' RadiusY='4' Fill='#8A99A6'/>

          <!-- tank and lid -->
          <Rectangle CacheMode='BitmapCache' Canvas.Left='46' Canvas.Top='16' Width='208' Height='96' RadiusX='18' RadiusY='18' Fill='{StaticResource Porcelain}'/>
          <Rectangle CacheMode='BitmapCache' Canvas.Left='56' Canvas.Top='104' Width='188' Height='10' RadiusX='5' RadiusY='5' Fill='#7C8B98' Opacity='0.45'/>
          <Rectangle CacheMode='BitmapCache' Canvas.Left='36' Canvas.Top='4' Width='228' Height='24' RadiusX='12' RadiusY='12'>
            <Rectangle.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#E8EEF2' Offset='0.6'/><GradientStop Color='#B3C0CB' Offset='1'/>
            </LinearGradientBrush></Rectangle.Fill>
          </Rectangle>
          <Rectangle CacheMode='BitmapCache' Canvas.Left='54' Canvas.Top='8' Width='192' Height='3' RadiusX='1.5' RadiusY='1.5' Fill='White' Opacity='0.95'/>

          <!-- chrome flush button -->
          <Ellipse Canvas.Left='122' Canvas.Top='38' Width='56' Height='56' Fill='{StaticResource Chrome}'>
          </Ellipse>
          <Canvas>
            <Canvas.RenderTransform><ScaleTransform x:Name='BtnScale' CenterX='150' CenterY='66'/></Canvas.RenderTransform>
            <Ellipse Canvas.Left='129' Canvas.Top='45' Width='42' Height='42'>
              <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.35,0.3'>
                <GradientStop x:Name='BtnHi' Color='#CFFAFE' Offset='0'/><GradientStop x:Name='BtnLo' Color='#0891B2' Offset='1'/>
              </RadialGradientBrush></Ellipse.Fill>
            </Ellipse>
            <Ellipse Canvas.Left='137' Canvas.Top='49' Width='18' Height='10' Fill='White' Opacity='0.75'/>
          </Canvas>

          <!-- bowl, seat, inner wall -->
          <Ellipse CacheMode='BitmapCache' Canvas.Left='20' Canvas.Top='112' Width='260' Height='300'>
            <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.35,0.28' Center='0.45,0.4' RadiusX='0.68' RadiusY='0.68'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#F1F5F9' Offset='0.5'/><GradientStop Color='#A9B7C3' Offset='1'/>
            </RadialGradientBrush></Ellipse.Fill>
          </Ellipse>
          <Ellipse CacheMode='BitmapCache' Canvas.Left='33' Canvas.Top='124' Width='234' Height='276' StrokeThickness='15'>
            <Ellipse.Stroke><LinearGradientBrush StartPoint='0.2,0' EndPoint='0.8,1'>
              <GradientStop Color='#FFFFFF' Offset='0'/><GradientStop Color='#E2E8F0' Offset='0.5'/><GradientStop Color='#B6C3CE' Offset='1'/>
            </LinearGradientBrush></Ellipse.Stroke>
          </Ellipse>
          <Ellipse CacheMode='BitmapCache' Canvas.Left='52' Canvas.Top='142' Width='196' Height='244'>
            <Ellipse.Fill><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
              <GradientStop Color='#7E93A3' Offset='0'/><GradientStop Color='#C8D3DC' Offset='0.35'/><GradientStop Color='#EEF3F6' Offset='1'/>
            </LinearGradientBrush></Ellipse.Fill>
          </Ellipse>

          <!-- water -->
          <Grid Canvas.Left='64' Canvas.Top='158' Width='172' Height='216'>
            <Grid.Clip><EllipseGeometry Center='86,108' RadiusX='86' RadiusY='108'/></Grid.Clip>
            <Ellipse><Ellipse.Fill><RadialGradientBrush GradientOrigin='0.5,0.55'>
              <GradientStop x:Name='WaterIn' Color='#CFFAFE' Offset='0'/><GradientStop x:Name='WaterMid' Color='#22D3EE' Offset='0.55'/>
              <GradientStop x:Name='WaterOut' Color='#0E7490' Offset='1'/>
            </RadialGradientBrush></Ellipse.Fill></Ellipse>
            <Canvas x:Name='Ripples' Width='172' Height='216'/>
            <Ellipse Width='46' Height='46' RenderTransformOrigin='0.5,0.5'>
              <Ellipse.Fill><RadialGradientBrush>
                <GradientStop Color='#020617' Offset='0'/><GradientStop Color='#0B2A3C' Offset='0.7'/><GradientStop Color='#000B2A3C' Offset='1'/>
              </RadialGradientBrush></Ellipse.Fill>
              <Ellipse.RenderTransform><ScaleTransform x:Name='DrainScale' ScaleX='0.3' ScaleY='0.3'/></Ellipse.RenderTransform>
            </Ellipse>
            <Canvas x:Name='Swirl' Width='172' Height='216'>
              <Canvas.RenderTransform><TransformGroup>
                <ScaleTransform x:Name='SwScale' CenterX='86' CenterY='108'/>
                <RotateTransform x:Name='SwRot' CenterX='86' CenterY='108'/>
              </TransformGroup></Canvas.RenderTransform>
            </Canvas>
            <Canvas x:Name='Bubbles' Width='172' Height='216'/>
            <Ellipse Width='78' Height='26' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='26,26,0,0' RenderTransformOrigin='0.5,0.5'>
              <Ellipse.RenderTransform><RotateTransform Angle='-24'/></Ellipse.RenderTransform>
              <Ellipse.Fill><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>
                <GradientStop Color='#99FFFFFF' Offset='0'/><GradientStop Color='#00FFFFFF' Offset='1'/>
              </LinearGradientBrush></Ellipse.Fill>
            </Ellipse>
          </Grid>
          <Ellipse Canvas.Left='52' Canvas.Top='142' Width='196' Height='244' StrokeThickness='2' Opacity='0.7'>
            <Ellipse.Stroke><LinearGradientBrush StartPoint='0.5,0' EndPoint='0.5,1'>
              <GradientStop Color='#40FFFFFF' Offset='0'/><GradientStop Color='#FFFFFFFF' Offset='1'/>
            </LinearGradientBrush></Ellipse.Stroke>
          </Ellipse>

          <!-- clogged: the water rising to the rim -->
          <Ellipse x:Name='Rise' Canvas.Left='56' Canvas.Top='146' Width='188' Height='236' Opacity='0' RenderTransformOrigin='0.5,0.5'
                   Stroke='#FECACA' StrokeThickness='3'>
            <Ellipse.Fill><RadialGradientBrush GradientOrigin='0.4,0.35'>
              <GradientStop Color='#CCFCA5A5' Offset='0'/><GradientStop Color='#CCDC2626' Offset='0.7'/><GradientStop Color='#E67F1D1D' Offset='1'/>
            </RadialGradientBrush></Ellipse.Fill>
            <Ellipse.RenderTransform><ScaleTransform x:Name='RiseScale' ScaleX='0.8' ScaleY='0.8'/></Ellipse.RenderTransform>
          </Ellipse>
          <Canvas x:Name='Splash' Canvas.Left='145' Canvas.Top='190'/>

          <!-- plunger -->
          <Canvas x:Name='Plunger' Canvas.Left='108' Canvas.Top='92' Opacity='0'>
            <Canvas.RenderTransform><TranslateTransform x:Name='PlY' Y='-540'/></Canvas.RenderTransform>
            <Rectangle Canvas.Left='35' Canvas.Top='0' Width='14' Height='152' RadiusX='7' RadiusY='7'>
              <Rectangle.Fill><LinearGradientBrush StartPoint='0,0.5' EndPoint='1,0.5'>
                <GradientStop Color='#FBBF24' Offset='0'/><GradientStop Color='#D97706' Offset='0.35'/>
                <GradientStop Color='#92400E' Offset='0.75'/><GradientStop Color='#78350F' Offset='1'/>
              </LinearGradientBrush></Rectangle.Fill>
            </Rectangle>
            <Rectangle Canvas.Left='39' Canvas.Top='14' Width='1.5' Height='120' Fill='#78350F' Opacity='0.35'/>
            <Rectangle Canvas.Left='44' Canvas.Top='30' Width='1' Height='96' Fill='#FDE68A' Opacity='0.35'/>
            <Canvas Canvas.Top='140'>
              <Canvas.RenderTransform><ScaleTransform x:Name='Cup' CenterX='42' CenterY='50'/></Canvas.RenderTransform>
              <Path Data='M2,44 C2,10 22,0 42,0 C62,0 82,10 82,44 Z'>
                <Path.Fill><RadialGradientBrush GradientOrigin='0.32,0.22' Center='0.45,0.4' RadiusX='0.7' RadiusY='0.8'>
                  <GradientStop Color='#FCA5A5' Offset='0'/><GradientStop Color='#DC2626' Offset='0.45'/><GradientStop Color='#7F1D1D' Offset='1'/>
                </RadialGradientBrush></Path.Fill>
              </Path>
              <Ellipse Canvas.Left='0' Canvas.Top='38' Width='84' Height='12' Fill='#7F1D1D'/>
              <Ellipse Canvas.Left='16' Canvas.Top='8' Width='22' Height='11' Fill='White' Opacity='0.5'/>
            </Canvas>
          </Canvas>
        </Canvas>
      </Viewbox>
      <Border x:Name='Pill' HorizontalAlignment='Center' Margin='0,22,0,0' Padding='16,5' CornerRadius='15' BorderThickness='1'>
        <TextBlock x:Name='Tier' FontFamily='Thmanyah Sans, Segoe UI' FontSize='15' FontWeight='Bold'/>
      </Border>
      <Border Background='#D90B1220' CornerRadius='14' Padding='18,6' Margin='0,10,0,0' HorizontalAlignment='Center'>
      <TextBlock x:Name='Msg' Text='Flushing RAM...' Foreground='White' FontFamily='Thmanyah Sans, Segoe UI' FontSize='26'
                 HorizontalAlignment='Center' TextAlignment='Center' TextWrapping='Wrap' RenderTransformOrigin='0.5,0.5'>
        <TextBlock.RenderTransform><ScaleTransform x:Name='MsgScale'/></TextBlock.RenderTransform>
      </TextBlock>
      </Border>
    </StackPanel>
  </Grid>
</Window>";

    static readonly string[] Names = { "LIGHT FLUSH", "MEDIUM FLUSH", "HEAVY FLUSH", "CLOGGED" };
    static readonly string[] Files = { "flush-light.wav", "flush-medium.wav", "flush-heavy.wav", "flush-clogged.wav" };
    static readonly string[] Accents = { "#22D3EE", "#2DD4BF", "#FB923C", "#F87171" };
    static readonly int[] PreviewPct = { 72, 84, 93, 97 };
    // water and button colours: { in, mid, out }
    static readonly string[] Clean = { "#CFFAFE", "#22D3EE", "#0E7490" }, Murky = { "#FFEDD5", "#FB923C", "#9A3412" }, Red = { "#FEE2E2", "#F87171", "#7F1D1D" };

    // ponytail: fixed thresholds (Nader 2026-11-23); under 70% still plays the light flush
    public static int TierOf(double pct) { return pct >= 96 ? 3 : pct >= 90 ? 2 : pct >= 80 ? 1 : 0; }

    static WA.EasingFunctionBase E(WA.EasingFunctionBase e, WA.EasingMode m) { e.EasingMode = m; return e; }
    static readonly WA.IEasingFunction QIn = E(new WA.QuadraticEase(), WA.EasingMode.EaseIn), QInOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseInOut),
        CIn = E(new WA.CubicEase(), WA.EasingMode.EaseIn), COut = E(new WA.CubicEase(), WA.EasingMode.EaseOut),
        Back = new WA.BackEase { Amplitude = 0.5, EasingMode = WA.EasingMode.EaseOut };

    // Nader 2026-11-23: "it took time" -> every duration and cue runs at 60% of the designed length (make_sfx.py SPEED matches)
    public const double Speed = 0.6;
    static TimeSpan Ms(double ms) { return TimeSpan.FromMilliseconds(ms * Speed); }
    static WA.DoubleAnimation A(double? from, double to, int ms, WA.IEasingFunction e) {
        var a = new WA.DoubleAnimation { To = to, Duration = Ms(ms), EasingFunction = e };
        if (from.HasValue) a.From = from.Value;
        return a;
    }
    // keyframes as (ms, value) pairs, eased in-out
    static WA.DoubleAnimationUsingKeyFrames K(params double[] tv) {
        var k = new WA.DoubleAnimationUsingKeyFrames();
        for (int i = 0; i < tv.Length; i += 2) k.KeyFrames.Add(new WA.EasingDoubleKeyFrame(tv[i + 1], WA.KeyTime.FromTimeSpan(Ms(tv[i])), QInOut));
        return k;
    }
    static void S2(WM.ScaleTransform s, WA.AnimationTimeline a) { s.BeginAnimation(WM.ScaleTransform.ScaleXProperty, a); s.BeginAnimation(WM.ScaleTransform.ScaleYProperty, a); }
    static WM.Color C(string hex) { return (WM.Color)WM.ColorConverter.ConvertFromString(hex); }
    static WM.Color Alpha(string hex, byte a) { var c = C(hex); c.A = a; return c; }
    static void Tint(WM.GradientStop s, string hex, int ms) { s.BeginAnimation(WM.GradientStop.ColorProperty, new WA.ColorAnimation(C(hex), Ms(ms))); }
    static void Shake(WM.TranslateTransform t, double amp, int times) {
        t.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-amp, amp, Ms(45)) {
            AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(times), FillBehavior = WA.FillBehavior.Stop });
    }
    static WM.Brush Grad(W.Point a, W.Point b, params WM.Color[] cs) {
        var g = new WM.LinearGradientBrush { StartPoint = a, EndPoint = b };
        for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new WM.GradientStop(cs[i], i / (double)(cs.Length - 1)));
        return g;
    }

    // tapered swirl arms (filled crescents, bright at the centre, fading out)
    static void AddArms(System.Windows.Controls.Canvas swirl) {
        foreach (int i in Enumerable.Range(0, 8)) {
            bool big = i % 2 == 0;
            swirl.Children.Add(new W.Shapes.Path {
                Data = WM.Geometry.Parse(big ? "M86,108 C88,76 118,52 162,60 C122,64 98,82 86,108 Z" : "M86,108 C88,92 104,80 126,82 C106,87 94,96 86,108 Z"),
                Fill = Grad(new W.Point(0, 1), new W.Point(1, 0), Alpha("#FFFFFF", (byte)(big ? 235 : 190)), Alpha("#E0FBFF", 20)),
                RenderTransform = new WM.RotateTransform(i * 45, 86, 108)
            });
        }
    }

    // preview: "light" | "medium" | "heavy" | "clogged" plays that tier without flushing anything
    public static void Run(string preview) {
        var ms = new Mem.MemStatus(); Mem.Status(ref ms);
        double pct = ms.total > 0 ? (ms.total - ms.avail) * 100.0 / ms.total : 0;
        int tier = TierOf(pct);
        string result = null;
        Thread work = null;
        if (preview != null) {
            int i = Array.FindIndex(Names, x => x.StartsWith(preview.ToUpperInvariant()));
            tier = i >= 0 ? i : 1; pct = PreviewPct[tier];
            result = "Preview: nothing was flushed";
        } else {
            // 1.2.3: Explorer restarts after the overlay closes (killing it mid-animation blacked out the desktop behind the toilet)
            work = new Thread(() => { result = Mem.Flush(keepSelf: true, explorer: false); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        }

        // 1.2.2 (super light): animations tick at FPS, not WPF's default 60
        WA.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(WA.Timeline), new W.FrameworkPropertyMetadata { DefaultValue = FPS });
        var win = (W.Window)W.Markup.XamlReader.Parse(Xaml);
        Func<string, object> n = win.FindName;
        var root = (W.UIElement)n("Root");
        var pop = (WM.ScaleTransform)n("Pop"); var btn = (WM.ScaleTransform)n("BtnScale"); var shake = (WM.TranslateTransform)n("Shake");
        var drain = (WM.ScaleTransform)n("DrainScale"); var swScale = (WM.ScaleTransform)n("SwScale"); var swRot = (WM.RotateTransform)n("SwRot");
        var water = new[] { (WM.GradientStop)n("WaterIn"), (WM.GradientStop)n("WaterMid"), (WM.GradientStop)n("WaterOut") };
        var btnHi = (WM.GradientStop)n("BtnHi"); var btnLo = (WM.GradientStop)n("BtnLo");
        var rise = (W.UIElement)n("Rise"); var riseScale = (WM.ScaleTransform)n("RiseScale");
        var plunger = (W.UIElement)n("Plunger"); var plY = (WM.TranslateTransform)n("PlY"); var cup = (WM.ScaleTransform)n("Cup");
        var bubbles = (System.Windows.Controls.Canvas)n("Bubbles"); var splash = (System.Windows.Controls.Canvas)n("Splash");
        var ripples = (System.Windows.Controls.Canvas)n("Ripples");
        var msg = (System.Windows.Controls.TextBlock)n("Msg"); var msgScale = (WM.ScaleTransform)n("MsgScale");
        var tierText = (System.Windows.Controls.TextBlock)n("Tier"); var pill = (System.Windows.Controls.Border)n("Pill");
        AddArms((System.Windows.Controls.Canvas)n("Swirl"));
        if (Solid) {  // recordings: full screen, opaque backdrop and glow
            foreach (string k in new[] { "Dim", "GlowDisc" }) { var u = (W.UIElement)n(k); u.Visibility = W.Visibility.Visible; u.Opacity = 1; }
            win.SizeToContent = W.SizeToContent.Manual; win.WindowState = W.WindowState.Maximized;
        }
        if (tier == 3) msg.Text = "Checking the pipes...";

        string accent = Accents[tier];
        tierText.Text = string.Format("{0}   ·   {1:0}% RAM", Names[tier], pct);
        tierText.Foreground = new WM.SolidColorBrush(C(accent));
        pill.Background = new WM.SolidColorBrush(C("#E00B1220"));  // dark pill: readable on any desktop pill.BorderBrush = new WM.SolidColorBrush(Alpha(accent, 0x88));
        ((WM.GradientStop)n("Glow")).Color = Alpha(accent, 0x40);
        string[] btnCol = tier == 2 ? Murky : tier == 3 ? Red : Clean;
        btnHi.Color = C(btnCol[0]); btnLo.Color = C(btnCol[2]);
        Action<string[], int> tintWater = (cols, t) => { for (int i = 0; i < 3; i++) Tint(water[i], cols[i], t); };

        // ripples on the water surface: the bowl is never frozen
        for (int i = 0; i < 3; i++) {
            var sc = new WM.ScaleTransform(0.2, 0.2, 86, 108);
            var e = new W.Shapes.Ellipse { Width = 172, Height = 216, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 150)), StrokeThickness = 2, Opacity = 0, RenderTransform = sc };
            ripples.Children.Add(e);
            var begin = Ms(i * 900);
            sc.BeginAnimation(WM.ScaleTransform.ScaleXProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            sc.BeginAnimation(WM.ScaleTransform.ScaleYProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            e.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.55, 0, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever });
        }

        // droplets (L8 secondary action) fly out on arcs (L7)
        var drops = new System.Collections.Generic.List<Tuple<W.Shapes.Path, WM.TranslateTransform>>();
        for (int i = 0; i < 9; i++) {
            var t = new WM.TranslateTransform();
            var tf = new WM.TransformGroup(); tf.Children.Add(new WM.ScaleTransform(0.7 + i % 3 * 0.2, 0.7 + i % 3 * 0.2)); tf.Children.Add(t);
            var e = new W.Shapes.Path {
                Data = WM.Geometry.Parse("M5,0 C5,0 10,7 10,11 A5,5 0 1 1 0,11 C0,7 5,0 5,0 Z"), Opacity = 0, RenderTransform = tf,
                Fill = new WM.RadialGradientBrush(C("#F0F9FF"), C("#22D3EE")) { GradientOrigin = new W.Point(0.35, 0.6) }
            };
            splash.Children.Add(e); drops.Add(Tuple.Create(e, t));
        }
        Action burst = () => {
            for (int i = 0; i < drops.Count; i++) {
                double dx = (i - 4) * 26, up = 48 + (i % 3) * 22;
                var y = new WA.DoubleAnimationUsingKeyFrames();
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(-up, WA.KeyTime.FromTimeSpan(Ms(320)), COut));
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(80, WA.KeyTime.FromTimeSpan(Ms(780)), QIn));
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.YProperty, y);
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.XProperty, A(0, dx, 780, COut));
                drops[i].Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, 540, 1, 780, 0));
            }
        };
        // glossy bubbles rise through the clogged water, staggered
        for (int i = 0; i < 6; i++) {
            double d = 7 + i % 3 * 4;
            var e = new W.Shapes.Ellipse {
                Width = d, Height = d, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 200)), StrokeThickness = 1.2, Opacity = 0.9,
                Fill = new WM.RadialGradientBrush(Alpha("#FFFFFF", 30), Alpha("#FFFFFF", 110)) { GradientOrigin = new W.Point(0.3, 0.3) },
                RenderTransform = new WM.TranslateTransform(0, 260)
            };
            System.Windows.Controls.Canvas.SetLeft(e, 34 + i * 20); bubbles.Children.Add(e);
        }
        Action bubble = () => {
            int i = 0;
            foreach (W.UIElement e in bubbles.Children)
                ((WM.TranslateTransform)e.RenderTransform).BeginAnimation(WM.TranslateTransform.YProperty,
                    new WA.DoubleAnimation(210, 30, Ms(900 + i * 90)) { BeginTime = Ms(i++ * 150), RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QIn });
        };

        System.Media.SoundPlayer sound = null;
        if (Settings.Sound) try { sound = new System.Media.SoundPlayer(Assembly.GetExecutingAssembly().GetManifestResourceStream(Files[tier])); sound.Load(); } catch (Exception) { sound = null; }

        // press: wind-up (L2), squash (L1), settle (L5)
        Action press = () => S2(btn, K(0, 1, 70, 1.12, 200, 0.68, 420, 1));
        Action<int, int, double, double> flush = (deg, dur, swirlTo, hole) => {
            swRot.BeginAnimation(WM.RotateTransform.AngleProperty, A(0, deg, dur, QIn));
            S2(swScale, A(1, swirlTo, dur, CIn));
            S2(drain, A(0.3, hole, dur, QIn));
        };
        Action refill = () => { S2(swScale, A(null, 1, 560, Back)); S2(drain, A(null, 0.3, 560, COut)); };
        Action<string> say = s => msg.Text = s;

        var cues = new System.Collections.Generic.List<Tuple<int, Action>>();
        Action<int, Action> at = (t, a) => cues.Add(Tuple.Create((int)(t * Speed), a));
        int endAt;
        bool motion = W.SystemParameters.ClientAreaAnimation;
        if (!motion) endAt = 400;
        else if (tier == 0) {           // light: one press, a half drain, quick refill
            at(420, press);
            at(600, () => flush(720, 1500, 0.4, 1.2));
            at(2150, refill);
            endAt = 2750;
        } else if (tier == 1) {         // medium: the full swirl
            at(450, press);
            at(620, () => flush(1440, 2500, 0.08, 1.7));
            at(3150, refill);
            endAt = 3750;
        } else if (tier == 2) {         // heavy: murky water, two presses, the bowl shakes, splash
            at(250, () => tintWater(Murky, 450));
            at(450, press);
            at(760, press);
            at(950, () => { flush(2520, 2900, 0.05, 2.0); Shake(shake, 3, 26); });
            at(3250, burst);
            at(3900, () => { refill(); tintWater(Clean, 600); });
            endAt = 4500;
        } else {                        // clogged: water rises, the press fails, plunger x3, then it bursts free
            at(200, () => {
                tintWater(Red, 500);
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 900, COut));
                S2(riseScale, A(0.8, 1, 900, COut));
                bubble();
            });
            at(1100, () => { say("It's clogged."); S2(riseScale, new WA.DoubleAnimation(1, 1.035, Ms(450)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut }); });
            at(1400, () => { press(); swRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(0, 40, Ms(220)) { AutoReverse = true, EasingFunction = COut }); Shake(shake, 4, 5); });
            at(2050, () => {
                say("Plunging...");
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 150, null));
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(-540, 0, 520, Back));
            });
            foreach (int t in new[] { 2700, 3250, 3800 })
                at(t, () => {
                    plY.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 90, -14, 240, 26, 440, 0));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 90, 1.05, 240, 0.55, 440, 1));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 90, 0.95, 240, 1.25, 440, 1));
                    Shake(shake, 2, 2);
                });
            at(4400, () => {
                say("Flushing RAM...");
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(null, -540, 420, CIn));
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 420, CIn));
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 350, null));
                S2(riseScale, A(null, 0.8, 350, CIn));
                bubbles.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 300, null));
                burst();
                flush(2880, 2400, 0.04, 2.2);
                tintWater(Clean, 2400);
                Tint(btnHi, Clean[0], 2400); Tint(btnLo, Clean[2], 2400);
            });
            at(6850, refill);
            endAt = 7450;
        }

        bool started = false;
        Action startWork = () => { if (work != null && !started) { started = true; work.Start(); } };
        var clock = new Stopwatch();
        int next = 0, stage = 0; long shownAt = 0;
        var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += delegate {
            long t = clock.ElapsedMilliseconds;
            while (next < cues.Count && t >= cues[next].Item1) cues[next++].Item2();
            // 1.2.3: the flush starts with the last cue (the refill), so the motion runs exactly like the preview instead of
            // stuttering while every working set is emptied
            if (next >= cues.Count) startWork();
            long end = (long)(endAt * Speed);
            // the ending lands (L26); a slow flush (Explorer restart) never holds the screen: after 1.2 s it finishes in the background
            if (stage == 0 && next >= cues.Count && t >= end && (result != null || t >= end + 1200)) {
                msg.Text = result ?? "Finishing in the background..."; shownAt = t; stage = 1;
                if (motion) { S2(msgScale, A(0.85, 1, 380, Back)); msg.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 220, null)); }
            } else if (stage == 1 && t >= shownAt + 1300) {
                var fade = new WA.DoubleAnimation(0, Ms(motion ? 350 : 1));
                fade.Completed += delegate { win.Close(); };
                root.BeginAnimation(W.UIElement.OpacityProperty, fade);
                stage = 2;
            }
        };
        // 1.2.1: the clock and the sound start when the first frame is on screen (Loaded fired before it, so a slow first paint
        // under full RAM skipped frames while the sound ran ahead)
        win.ContentRendered += delegate {
            clock.Start(); timer.Start();
            if (sound != null) {               // one soundtrack per tier, cued to this clock, at the user's volume
                uint v = (uint)(Settings.Volume * 0xFFFF / 100); waveOutSetVolume(IntPtr.Zero, v | (v << 16));
                sound.Play();
            }
            root.BeginAnimation(W.UIElement.OpacityProperty, A(null, 1, motion ? 250 : 1, null));
            if (motion) S2(pop, A(0.6, 1, 450, new WA.BackEase { Amplitude = 0.4, EasingMode = WA.EasingMode.EaseOut }));
            else { pop.ScaleX = 1; pop.ScaleY = 1; }
        };
        win.MouseDown += delegate { win.Close(); };
        win.KeyDown += (s, e) => { if (e.Key == W.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        timer.Stop(); if (sound != null) sound.Stop();
        startWork();  // closed before it started (a click on the overlay): the flush still runs
        if (work != null) { work.Join(20000); if (Settings.RestartExplorer) Mem.RestartExplorer(); }  // flush done, then Explorer, then the widget
    }
}

class Widget : Form {
    [StructLayout(LayoutKind.Sequential)]
    struct WindowPos { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr p);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    const double Lit = 0.95;
    static double Dim { get { return Settings.IdleOpacity / 100.0; } }
    public void ApplySettings() {              // called by the settings window: hotkey and idle brightness change live
        UnregisterHotKey(Handle, 1);
        if (Settings.Hotkey) RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.F);
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) Opacity = Dim;
    }
    Mem.MemStatus m;
    string pctText = "", gbText = "";
    float pct;
    readonly Font big, small;
    readonly SolidBrush fg = new SolidBrush(Color.White), track = new SolidBrush(Color.FromArgb(55, 65, 81));
    readonly SolidBrush ok = new SolidBrush(Color.FromArgb(34, 211, 238)), hot = new SolidBrush(Color.FromArgb(251, 146, 60));
    readonly SolidBrush warm = new SolidBrush(Color.FromArgb(250, 204, 21)), red = new SolidBrush(Color.FromArgb(248, 113, 113));

    public static void Start() {
        bool first;
        using (new Mutex(true, "NJ-Flush-Widget", out first)) {
            if (!first) return;  // one widget only
            Application.EnableVisualStyles();
            Application.Run(new Widget());
        }
    }

    Widget() {
        string face = FontFamily.Families.Any(f => f.Name == "Thmanyah Sans") ? "Thmanyah Sans" : "Segoe UI";
        big = new Font(face, 10f, FontStyle.Bold); small = new Font(face, 8f);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(158, 34);
        BackColor = Color.FromArgb(17, 24, 39);
        Opacity = Dim;
        DoubleBuffered = true;
        Pin();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate { Pin(); };

        var menu = new ContextMenuStrip();
        ((ToolStripMenuItem)menu.Items.Add("Flush now", null, delegate { TryFlush(); })).ShortcutKeyDisplayString = "Ctrl+Alt+F";
        menu.Items.Add("Memory breakdown", null, delegate { BreakdownWindow.Show(TryFlush); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, delegate { SettingsForm.Open(this); });
        var preview = new ToolStripMenuItem("Preview animations");
        foreach (string t in new[] { "light", "medium", "heavy", "clogged" }) {
            string tier = t;
            preview.DropDownItems.Add(char.ToUpper(t[0]) + t.Substring(1), null, delegate {
                Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "/preview " + tier) { UseShellExecute = false });
            });
        }
        menu.Items.Add(preview);
        menu.Items.Add(new ToolStripSeparator());
        updateItem = (ToolStripMenuItem)menu.Items.Add("Check for updates...", null, delegate { Updater.Interactive(this); });
        menu.Items.Add("Uninstall NJ-Flush...", null, delegate {
            if (MessageBox.Show("Remove NJ-Flush from this PC?\n\nThe task, the autostart entry, the settings and the installed copy are removed.",
                                "NJ-Flush", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) { App.Uninstall(); Close(); }
        });
        menu.Items.Add("Close", null, delegate { Close(); });
        ContextMenuStrip = menu;
        tip = new ToolTip { InitialDelay = 400 };
        MouseEnter += delegate {
            Opacity = Lit;
            tip.SetToolTip(this, string.Format("Free {0:0.00} GB\nLast flush: {1}\nDouble-click or Ctrl+Alt+F to flush{2}", m.avail / 1073741824.0, Settings.Last(), updateNote));
        };
        MouseLeave += delegate { Opacity = Dim; };
        DoubleClick += delegate { TryFlush(); };

        try { File.Delete(App.Exe + ".old"); } catch (Exception) { }   // left by the last update
        var upd = new System.Windows.Forms.Timer { Interval = 15000 };    // first check 15 s after start, then every 24 h
        upd.Tick += delegate { upd.Interval = 24 * 3600 * 1000; if (Settings.AutoUpdate) CheckInBackground(); };
        upd.Start();
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += delegate { Sample(); };
        timer.Start();
        Sample();
    }

    // tool window (no taskbar / Alt+Tab), never takes focus
    protected override CreateParams CreateParams {
        get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);  // Win11 rounded corners
        StickToDesktop();
        if (Settings.Hotkey) RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (uint)Keys.F);  // Ctrl+Alt+F, no auto-repeat; taken by another app = silently off
    }
    protected override void OnHandleDestroyed(EventArgs e) { UnregisterHotKey(Handle, 1); base.OnHandleDestroyed(e); }

    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        GC.Collect();
        EmptyWorkingSet(Process.GetCurrentProcess().Handle);  // start lean
    }

    // WM_WINDOWPOSCHANGING: always insert at HWND_BOTTOM, so it sits on the desktop under every window
    protected override void WndProc(ref Message msg) {
        if (msg.Msg == 0x46) {
            var wp = (WindowPos)Marshal.PtrToStructure(msg.LParam, typeof(WindowPos));
            wp.after = new IntPtr(1);
            wp.flags &= ~0x4u;  // clear SWP_NOZORDER
            Marshal.StructureToPtr(wp, msg.LParam, false);
        }
        if (msg.Msg == 0x312 && msg.WParam.ToInt32() == 1) TryFlush();  // WM_HOTKEY
        if (msg.Msg == TaskbarCreated && TaskbarCreated != 0) StickToDesktop();  // Explorer restarted: a new desktop window
        base.WndProc(ref msg);
    }

    // 1.2.3: owned by the desktop (Progman), so "Show desktop" (the corner button, Win+D) keeps the widget instead of hiding it
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr v);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    void StickToDesktop() {
        IntPtr desk = FindWindow("Progman", null);
        if (desk != IntPtr.Zero) SetWindowLongPtr(Handle, -8, desk);  // GWL_HWNDPARENT = owner
    }

    // fixed spot on the main monitor: right edge, just above the taskbar clock
    void Pin() {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 8, wa.Bottom - Height - 6);
    }

    ToolTip tip;
    ToolStripMenuItem updateItem;
    string updateNote = "";
    void CheckInBackground() {
        new Thread(() => {
            var i = Updater.Check();
            if (i.Error == null && i.Newer) BeginInvoke((Action)delegate {
                updateItem.Text = "Update to " + i.Tag.TrimStart('v') + "..."; updateItem.Font = new Font(updateItem.Font, FontStyle.Bold);
                updateNote = "\nUpdate " + i.Tag.TrimStart('v') + " is available (right-click)";
            });
        }) { IsBackground = true }.Start();
    }
    // during the cooldown a flush request explains itself instead of doing nothing silently
    void TryFlush() {
        int left = Settings.CooldownLeft();
        if (left > 0) { tip.Show(string.Format("Just flushed. Next flush in {0} s.", left), this, 0, -44, 2000); return; }
        if ((DateTime.Now - lastTry).TotalSeconds < 3) return;  // a second double-click while the first is still starting
        lastTry = DateTime.Now;
        tip.Show("Flushing...", this, 0, -44, 1500);  // answers at once; the overlay follows when Windows starts the task
        App.FlushNow();
    }
    DateTime lastTry = DateTime.MinValue;

    bool commitTight;
    int highFor; DateTime lastAuto = DateTime.MinValue;
    void Sample() {
        if (!Mem.Status(ref m)) return;
        double total = m.total / 1073741824.0, used = (m.total - m.avail) / 1073741824.0;
        // auto-flush: RAM at or above the threshold for 30 s in a row, at most once per 10 minutes
        highFor = used / total * 100 >= Settings.AutoAt ? highFor + 1 : 0;
        if (Settings.AutoFlush && highFor >= 30 && (DateTime.Now - lastAuto).TotalMinutes >= 10) { lastAuto = DateTime.Now; highFor = 0; App.FlushNow(); }
        string p = string.Format("{0:0}%", used / total * 100), g = Settings.CooldownLeft() > 0 ? string.Format("next flush {0}s", Settings.CooldownLeft()) : string.Format("{0:0.00}/{1:0.00} GB", used, total);
        bool tight = m.tPage > 0 && (m.tPage - m.aPage) / (double)m.tPage >= 0.9;  // reserved memory near its limit (2026-11-23)
        if (p == pctText && g == gbText && tight == commitTight) return;  // repaint only on change
        pct = (float)(used / total); pctText = p; gbText = g; commitTight = tight;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics gr = e.Graphics;
        gr.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        gr.DrawString(pctText, big, fg, 8, 5);
        SizeF s = gr.MeasureString(gbText, small);
        gr.DrawString(gbText, small, fg, ClientSize.Width - s.Width - 6, 8);
        int w = ClientSize.Width - 16;
        gr.FillRectangle(track, 8, 27, w, 3);
        // bar colour = the flush tier it would play: cyan, yellow, orange, red (commit near its limit counts as orange)
        SolidBrush bar = pct >= 0.96f ? red : pct >= 0.9f || commitTight ? hot : pct >= 0.8f ? warm : ok;
        gr.FillRectangle(bar, 8, 27, (int)(w * pct), 3);
    }
}

// Settings window (Nader 2026-11-23): right-click the widget > Settings... (or NJ-Flush.exe /settings). Every change is saved and applied at once.
class SettingsForm : Form {
    static SettingsForm open;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    static readonly Color Bg = Color.FromArgb(15, 23, 35), Panel_ = Color.FromArgb(28, 39, 53), Fg = Color.FromArgb(230, 237, 243),
                          Muted = Color.FromArgb(154, 168, 182), Off = Color.FromArgb(85, 96, 108), Accent = Color.FromArgb(34, 211, 238),
                          Edge = Color.FromArgb(50, 65, 85);
    const int W = 540;                          // layout in 96-dpi units, scaled by sc (fonts scale by themselves)
    readonly Widget owner;
    readonly Font body, head;
    readonly float sc;
    int y = 18;
    bool loading = true;

    public static void Open(Widget w) {
        if (open != null && !open.IsDisposed) { open.Activate(); return; }
        open = new SettingsForm(w); open.Show();
    }

    int S(int v) { return (int)Math.Round(v * sc); }
    Point P(int x, int yy) { return new Point(S(x), S(yy)); }

    public SettingsForm(Widget w) {
        owner = w;
        AutoScaleMode = AutoScaleMode.None;
        using (var g = CreateGraphics()) sc = g.DpiX / 96f;
        string face = FontFamily.Families.Any(f => f.Name == "Thmanyah Sans") ? "Thmanyah Sans" : "Segoe UI";
        body = new Font(face, 10f); head = new Font(face, 11.5f, FontStyle.Bold);
        Text = "NJ-Flush settings"; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Bg; ForeColor = Fg; Font = body;

        Section("Animation and sound");
        Check("Play the flush animation", Settings.Animation, v => Settings.Animation = v);
        Note("Off: the flush runs quietly in the background.");
        var sound = Check("Play sound effects", Settings.Sound, v => Settings.Sound = v);
        var vol = Slider("Volume", 0, 100, Settings.Volume, "{0}%", v => Settings.Volume = v);
        sound.CheckedChanged += delegate { vol.Enabled = sound.Checked; };
        vol.Enabled = sound.Checked;
        PreviewRow();

        Section("Flush");
        Check("Restart File Explorer on flush", Settings.RestartExplorer, v => Settings.RestartExplorer = v);
        Note("Frees Explorer's memory. Turn off to keep your open folders.");
        Number("Wait between flushes (seconds)", 10, 600, Settings.Cooldown, v => Settings.Cooldown = v);
        var auto = Check("Auto-flush when RAM stays high", Settings.AutoFlush, v => Settings.AutoFlush = v);
        var at = Slider("Auto-flush at", 70, 99, Settings.AutoAt, "{0}% RAM", v => Settings.AutoAt = v);
        auto.CheckedChanged += delegate { at.Enabled = auto.Checked; };
        at.Enabled = auto.Checked;

        Section("Widget and shortcuts");
        Check("Ctrl+Alt+F flushes from anywhere", Settings.Hotkey, v => Settings.Hotkey = v);
        Slider("Brightness when idle", 20, 100, Settings.IdleOpacity, "{0}%", v => Settings.IdleOpacity = v);
        Check("Start the widget with Windows", Settings.Autostart(), Settings.SetAutostart, save: false);

        Section("Updates");
        Check("Check for updates once a day", Settings.AutoUpdate, v => Settings.AutoUpdate = v);
        Text_("You have version " + Updater.Version, 22, y + 6, Muted);
        var chk = Btn("Check for updates", W - 22 - 180, 180, false);
        chk.Click += delegate { Updater.Interactive(this); };
        y += 40;

        y += 16;
        var reset = Btn("Reset to defaults", 22, 160, false);
        reset.Click += delegate {
            Settings.Animation = Settings.Sound = Settings.RestartExplorer = Settings.Hotkey = true; Settings.AutoFlush = false;
            Settings.Volume = 100; Settings.AutoAt = 90; Settings.Cooldown = 60; Settings.IdleOpacity = 40; Settings.AutoUpdate = true;
            Settings.Save(); if (owner != null) owner.ApplySettings();
            var again = new SettingsForm(owner) { StartPosition = FormStartPosition.Manual, Location = Location };
            open = again; again.Show(); Close();
        };
        var done = Btn("Done", W - 22 - 120, 120, true);
        done.Click += delegate { Close(); };
        AcceptButton = done; CancelButton = done;
        ClientSize = new Size(S(W), S(y + 54));
        loading = false;
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4);  // dark title bar (Win10 20H1+/Win11)
    }

    void Changed() { if (loading) return; Settings.Save(); if (owner != null) owner.ApplySettings(); }

    Label Text_(string text, int x, int yy, Color c, Font f = null) {
        var l = new Label { Text = text, AutoSize = true, Location = P(x, yy), ForeColor = c, BackColor = Bg };
        if (f != null) l.Font = f;
        Controls.Add(l); return l;
    }
    void Section(string text) { if (y > 20) y += 12; Text_(text, 20, y, Accent, head); y += 32; }
    void Note(string text) { Text_(text, 44, y - 6, Muted); y += 22; }
    CheckBox Check(string text, bool value, Action<bool> set, bool save = true) {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = P(22, y), ForeColor = Fg, BackColor = Bg, Cursor = Cursors.Hand };
        c.CheckedChanged += delegate { set(c.Checked); if (save) Changed(); else if (!loading && owner != null) owner.ApplySettings(); };
        Controls.Add(c); y += 30;
        return c;
    }
    TrackBar Slider(string text, int min, int max, int value, string fmt, Action<int> set) {
        var name = Text_(text, 44, y + 4, Fg);
        var t = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickStyle = TickStyle.None, AutoSize = false,
                               Location = P(240, y), Size = new Size(S(190), S(30)), BackColor = Bg, SmallChange = 1, LargeChange = 5 };
        var v = Text_(string.Format(fmt, t.Value), 440, y + 4, Muted);
        t.ValueChanged += delegate { set(t.Value); v.Text = string.Format(fmt, t.Value); Changed(); };
        t.EnabledChanged += delegate { v.ForeColor = t.Enabled ? Muted : Off; name.ForeColor = t.Enabled ? Fg : Off; };
        Controls.Add(t); y += 36;
        return t;
    }
    void Number(string text, int min, int max, int value, Action<int> set) {
        Text_(text, 22, y + 3, Fg);
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = S(90), Increment = 10,
                                    Location = P(340, y), BackColor = Panel_, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle };
        n.ValueChanged += delegate { set((int)n.Value); Changed(); };
        Controls.Add(n); y += 36;
    }
    void PreviewRow() {
        Text_("Preview", 44, y + 5, Fg);
        int x = 140;
        foreach (string t in new[] { "Light", "Medium", "Heavy", "Clogged" }) {
            string tier = t.ToLowerInvariant();
            var b = Btn(t, x, 88, false); x += 94;
            b.Click += delegate {
                Settings.Save();  // the preview process reads the saved animation / sound / volume
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "/preview " + tier) { UseShellExecute = false });
            };
        }
        y += 42;
    }
    Button Btn(string text, int x, int width, bool primary) {
        var b = new Button { Text = text, Location = P(x, y), Size = new Size(S(width), S(32)), FlatStyle = FlatStyle.Flat,
                             BackColor = primary ? Accent : Panel_, ForeColor = primary ? Color.FromArgb(6, 42, 51) : Fg, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? Accent : Edge;
        if (primary) b.Font = new Font(body, FontStyle.Bold);
        Controls.Add(b);
        return b;
    }
}

// NJ-Font (Thmanyah Sans) when this machine has it: installed, or the NJ-Font folder on the Desktop. Never shipped inside the exe
// (the font licence forbids redistributing the files); elsewhere Segoe UI.
static class NjFont {
    static WM.FontFamily cached;
    public static WM.FontFamily Wpf() {
        if (cached != null) return cached;
        if (WM.Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, "Thmanyah Sans", StringComparison.OrdinalIgnoreCase)))
            return cached = new WM.FontFamily("Thmanyah Sans");
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Desktop\NJ-Font\thmanyah\thmanyahsans\otf\");
        if (Directory.Exists(dir)) return cached = new WM.FontFamily(new Uri(dir), "./#Thmanyah Sans");
        return cached = new WM.FontFamily("Segoe UI");
    }
}

// Memory breakdown (Nader 2026-11-23: "beautify it"): one stacked bar of where the RAM is, a legend that says what each part means,
// the system facts behind "hidden" RAM, and Refresh / Flush now. WPF, so the NJ-Font renders.
static class BreakdownWindow {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    static WM.SolidColorBrush B(string hex) { return new WM.SolidColorBrush((WM.Color)WM.ColorConverter.ConvertFromString(hex)); }
    static readonly WM.Brush Bg = B("#0F1723"), Card = B("#162130"), Fg = B("#E6EDF3"), Muted = B("#9AA8B6"), Line = B("#243246"), Accent = B("#22D3EE");

    public static void Show(Action flush) {
        var win = new W.Window {
            Title = "NJ-Flush: where your RAM is", Width = 600, SizeToContent = W.SizeToContent.Height, ResizeMode = W.ResizeMode.NoResize,
            WindowStartupLocation = W.WindowStartupLocation.CenterScreen, Background = Bg, Foreground = Fg, FontFamily = NjFont.Wpf(), FontSize = 14
        };
        try { win.Icon = WM.Imaging.BitmapFrame.Create(new Uri(Assembly.GetExecutingAssembly().Location)); } catch (Exception) { }
        win.SourceInitialized += delegate { int dark = 1; DwmSetWindowAttribute(new W.Interop.WindowInteropHelper(win).Handle, 20, ref dark, 4); };
        Action fill = null;
        fill = () => {
            var root = new System.Windows.Controls.StackPanel { Margin = new W.Thickness(28, 24, 28, 24) };
            Build(root, Mem.Snapshot(), () => fill(), () => { win.Close(); flush(); }, () => win.Close());
            win.Content = root;
        };
        win.Cursor = W.Input.Cursors.Wait; fill(); win.Cursor = null;
        win.KeyDown += (s, e) => { if (e.Key == W.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
    }

    static System.Windows.Controls.TextBlock T(string text, double size, WM.Brush color, bool bold = false) {
        return new System.Windows.Controls.TextBlock { Text = text, FontSize = size, Foreground = color, FontWeight = bold ? W.FontWeights.Bold : W.FontWeights.Normal,
                                                       TextWrapping = W.TextWrapping.Wrap };
    }

    static void Build(System.Windows.Controls.StackPanel root, MemInfo m, Action refresh, Action flush, Action close) {
        double pct = m.InUse / m.Total * 100;
        string[] tierCol = { "#22D3EE", "#FACC15", "#FB923C", "#F87171" };
        var head = new System.Windows.Controls.DockPanel();
        var big = T(string.Format("{0:0}%", pct), 40, B(tierCol[Overlay.TierOf(pct)]), true); big.Margin = new W.Thickness(0, -6, 0, 0);
        System.Windows.Controls.DockPanel.SetDock(big, System.Windows.Controls.Dock.Right); head.Children.Add(big);
        var titles = new System.Windows.Controls.StackPanel();
        titles.Children.Add(T("Where your RAM is", 22, Fg, true));
        titles.Children.Add(T(string.Format("{0:0.00} GB in use of {1:0.00} GB", m.InUse, m.Total), 14, Muted));
        head.Children.Add(titles); root.Children.Add(head);

        // the stacked bar: every part of physical memory, in order
        var parts = new[] {
            Tuple.Create("Apps", m.Apps, "#22D3EE", "Programs you are running"),
            Tuple.Create("Kernel", m.Kernel, "#2DD4BF", "Windows' own memory pools"),
            Tuple.Create("Waiting to be written", m.Modified, "#FACC15", "Changed pages on their way to disk; a flush writes them"),
            Tuple.Create("Hidden", m.Hidden, "#FB923C", "Drivers, hypervisor, Memory Integrity, page tables; no flush reaches it"),
            Tuple.Create("Cache", m.Standby, "#64748B", "Recently used files, handed back the moment an app needs it"),
            Tuple.Create("Free", m.Free, "#1F2D3D", "Not used at all")
        };
        var bar = new System.Windows.Controls.Grid { Height = 22, Margin = new W.Thickness(0, 20, 0, 18) };
        bar.Clip = new WM.RectangleGeometry(new W.Rect(0, 0, 544, 22), 11, 11);
        int col = 0;
        foreach (var p in parts) {
            if (p.Item2 <= 0.005) continue;
            bar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new W.GridLength(p.Item2, W.GridUnitType.Star) });
            var seg = new System.Windows.Shapes.Rectangle { Fill = B(p.Item3), Margin = new W.Thickness(0, 0, 2, 0), ToolTip = string.Format("{0}: {1:0.00} GB", p.Item1, p.Item2) };
            System.Windows.Controls.Grid.SetColumn(seg, col++); bar.Children.Add(seg);
        }
        root.Children.Add(bar);

        // legend: two columns, each part with its size and one line of meaning
        var legend = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var p in parts) {
            if (!m.Lists && (p.Item1 == "Cache" || p.Item1 == "Free" || p.Item1 == "Waiting to be written")) continue;
            var cell = new System.Windows.Controls.DockPanel { Margin = new W.Thickness(0, 0, 18, 14) };
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = B(p.Item3), Margin = new W.Thickness(0, 6, 10, 0), VerticalAlignment = W.VerticalAlignment.Top };
            if (p.Item1 == "Free") { dot.Stroke = Muted; dot.StrokeThickness = 1; }
            System.Windows.Controls.DockPanel.SetDock(dot, System.Windows.Controls.Dock.Left); cell.Children.Add(dot);
            var txt = new System.Windows.Controls.StackPanel();
            var row = new System.Windows.Controls.DockPanel();
            var gb = T(string.Format("{0:0.00} GB", p.Item2), 14, Fg, true); System.Windows.Controls.DockPanel.SetDock(gb, System.Windows.Controls.Dock.Right);
            row.Children.Add(gb); row.Children.Add(T(p.Item1, 14, Fg));
            txt.Children.Add(row); txt.Children.Add(T(p.Item4, 12, Muted));
            cell.Children.Add(txt); legend.Children.Add(cell);
        }
        root.Children.Add(legend);
        if (!m.Lists) root.Children.Add(T("Cache and free memory need admin rights to read. They show after a flush has run once.", 12, Muted));

        // system facts on a card
        var card = new System.Windows.Controls.Border { Background = Card, CornerRadius = new W.CornerRadius(12), Padding = new W.Thickness(18, 14, 18, 6), Margin = new W.Thickness(0, 10, 0, 0) };
        var facts = new System.Windows.Controls.StackPanel(); card.Child = facts;
        double commit = m.CommitLimit > 0 ? m.CommitUsed / m.CommitLimit : 0;
        Action<string, string, WM.Brush, string> fact = (name, value, vcol, note) => {
            var r = new System.Windows.Controls.DockPanel { Margin = new W.Thickness(0, 0, 0, 10) };
            var v = T(value, 14, vcol, true); System.Windows.Controls.DockPanel.SetDock(v, System.Windows.Controls.Dock.Right); r.Children.Add(v);
            var s = new System.Windows.Controls.StackPanel(); s.Children.Add(T(name, 14, Fg));
            if (!string.IsNullOrEmpty(note)) s.Children.Add(T(note, 12, Muted));
            r.Children.Add(s); facts.Children.Add(r);
        };
        fact("Reserved memory", string.Format("{0:0.0} of {1:0.0} GB", m.CommitUsed, m.CommitLimit), commit >= 0.9 ? B("#F87171") : Fg,
             commit >= 0.9 ? "Near the limit: apps may fail to start. Close some, or flush." : "What apps have asked Windows to set aside");
        fact("Processes", m.Procs.ToString(), Fg, string.Format("node {0}   cmd {1}   conhost {2}", m.Node, m.Cmd, m.Conhost));
        fact("Leftover helpers", m.Leftovers.ToString(), m.Leftovers > 0 ? B("#FB923C") : Fg,
             m.Leftovers > 0 ? "Helper processes from closed sessions. A flush stops them." : "None found");
        fact("Memory Integrity", m.Hvci ? "On" : "Off", Fg, m.Hvci ? "Holds memory no tool can see. It is part of Hidden." : "");
        root.Children.Add(card);

        // tips, only when they apply
        var tips = new System.Collections.Generic.List<string>();
        if (m.Node > 30) tips.Add("Many node processes are running. Each VS Code window and Claude session starts its own helpers; close the ones you do not use.");
        if (m.Hidden > m.Total * 0.25) tips.Add("A large Hidden share is memory no flush can reach. Restarting Windows clears it; turning off Memory Integrity frees part of it, at some cost to security.");
        foreach (string tip in tips) {
            var tb = new System.Windows.Controls.Border { BorderBrush = B("#FB923C"), BorderThickness = new W.Thickness(3, 0, 0, 0), Padding = new W.Thickness(12, 4, 4, 4), Margin = new W.Thickness(0, 14, 0, 0) };
            tb.Child = T(tip, 13, Fg); root.Children.Add(tb);
        }

        // actions
        var btns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = W.HorizontalAlignment.Right, Margin = new W.Thickness(0, 22, 0, 0) };
        Func<string, bool, Action, System.Windows.Controls.Button> btn = (text, primary, act) => {
            var b = new System.Windows.Controls.Button { Content = text, Padding = new W.Thickness(18, 8, 18, 8), Margin = new W.Thickness(10, 0, 0, 0), Cursor = W.Input.Cursors.Hand,
                                                         Background = primary ? Accent : Card, Foreground = primary ? B("#062A33") : Fg, BorderBrush = primary ? Accent : Line,
                                                         FontWeight = primary ? W.FontWeights.Bold : W.FontWeights.Normal, FontSize = 14 };
            b.Click += delegate { act(); }; btns.Children.Add(b); return b;
        };
        btn("Refresh", false, refresh);
        btn("Close", false, close);
        var f = btn("Flush now", true, flush);
        // 1.2.3: the cooldown counts down live and the button wakes up by itself at 0
        Func<bool> tick = () => {
            int left = Settings.CooldownLeft();
            f.IsEnabled = left == 0; f.Content = left > 0 ? string.Format("Flush in {0} s", left) : "Flush now";
            return left > 0;
        };
        if (tick()) {
            var cd = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            cd.Tick += delegate { if (!tick() || !f.IsLoaded) cd.Stop(); };
            cd.Start();
        }
        root.Children.Add(btns);
    }
}

// Updates (Nader 2026-11-23, "like my other tools"): reads the latest GitHub release, compares it with this version, and on request
// downloads NJ-Flush.exe over HTTPS, checks its SHA-256 against the digest GitHub publishes, swaps it in and restarts the widget.
static class Updater {
    public const string Version = "1.2.3", Repo = "NJ-Burrito/NJ-Flush";
    public class Info { public string Tag, Url, Sha, Error; public bool Newer; }

    static string Get(string url) {
        System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;  // TLS 1.2 on .NET 4
        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        req.UserAgent = "NJ-Flush/" + Version; req.Accept = "application/vnd.github+json"; req.Timeout = 10000;
        using (var r = new StreamReader(req.GetResponse().GetResponseStream())) return r.ReadToEnd();
    }

    public static Info Check() {
        var i = new Info();
        try {
            return Parse(Get("https://api.github.com/repos/" + Repo + "/releases/latest"), Version);
        } catch (Exception e) { i.Error = "Could not reach GitHub (" + e.Message + ")."; }
        return i;
    }

    // a release reply -> tag, exe URL, its SHA-256 (from GitHub's digest) and whether it is newer than `current`
    public static Info Parse(string json, string current) {
        var i = new Info();
        try {
            var tag = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            var asset = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"NJ-Flush\\.exe\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success || !asset.Success) { i.Error = "The latest release has no NJ-Flush.exe."; return i; }
            i.Tag = tag.Groups[1].Value; i.Url = asset.Groups[1].Value;
            var sha = System.Text.RegularExpressions.Regex.Match(asset.Value, "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
            if (sha.Success) i.Sha = sha.Groups[1].Value.ToLowerInvariant();
            i.Newer = new Version(i.Tag.TrimStart('v', 'V')) > new Version(current);
        } catch (Exception e) { i.Error = "The release could not be read (" + e.Message + ")."; }
        return i;
    }
    public static bool ShaMatches(byte[] data, string sha) {
        if (data == null || sha == null || data.Length < 2 || data[0] != 'M' || data[1] != 'Z') return false;
        using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant() == sha;
    }

    // returns null when the new version is in place and the widget restart is queued; otherwise the reason it stopped
    public static string Apply(Info i) {
        if (i == null || !i.Newer || i.Url == null) return "No newer version to install.";
        if (i.Sha == null) return "GitHub did not publish a checksum for this file, so it was not installed.";
        string tmp = App.Exe + ".new";
        try {
            Directory.CreateDirectory(App.Dir);
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
            using (var wc = new System.Net.WebClient()) { wc.Headers.Add("User-Agent", "NJ-Flush/" + Version); wc.DownloadFile(i.Url, tmp); }
            if (!ShaMatches(File.ReadAllBytes(tmp), i.Sha)) { File.Delete(tmp); return "The download did not match its checksum, so it was not installed."; }
            string old = App.Exe + ".old";
            try { File.Delete(old); } catch (Exception) { }
            if (File.Exists(App.Exe)) File.Move(App.Exe, old);  // a running exe can be renamed, not overwritten
            File.Move(tmp, App.Exe);
        } catch (Exception e) { try { File.Delete(tmp); } catch (Exception) { } return "Update failed: " + e.Message; }
        foreach (var p in Process.GetProcessesByName("NJ-Flush"))   // the old widget holds the one-widget lock: stop it (the caller exits itself)
            if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); } catch (Exception) { }
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + App.Exe + "\" /widget") { CreateNoWindow = true, UseShellExecute = false });
        return null;
    }

    // the menu / settings action: check, ask, install; exits the caller's app when the swap is done
    public static void Interactive(IWin32Window owner) {
        Info i = null;
        Cursor.Current = Cursors.WaitCursor; i = Check(); Cursor.Current = Cursors.Default;
        if (i.Error != null) { MessageBox.Show(owner, i.Error, "NJ-Flush updates", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!i.Newer) { MessageBox.Show(owner, "You have the latest version (" + Version + ").", "NJ-Flush updates", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show(owner, string.Format("NJ-Flush {0} is available (you have {1}).\n\nUpdate now? The widget restarts by itself.", i.Tag.TrimStart('v'), Version),
                            "NJ-Flush updates", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string err = Apply(i);
        if (err != null) { MessageBox.Show(owner, err, "NJ-Flush updates", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Application.Exit();
    }
}