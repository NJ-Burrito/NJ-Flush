// NJ-Flush: one self-installing file.
//   double-click        -> first time: copies itself to %LOCALAPPDATA%\NJ-Flush and registers an elevated task (one UAC prompt);
//                          every time (1.3): opens the panel, or brings it forward and reloads it; flushes run through the task.
//   /flush  (the task)  -> dimmed full-screen toilet animation + sound while it restarts File Explorer and flushes RAM
//                          (empty working sets, flush modified list, purge standby cache), then starts the widget.
//   /preview <tier>     -> plays one tier's animation (light|medium|heavy|clogged) without flushing.
//   /widget             -> tiny RAM widget above the clock: live "XX%  used/total GB", dim until hovered,
//                          double-click = the panel, Ctrl+Alt+F = flush all; right-click = flushes, previews, settings, uninstall.
//   /uninstall          -> (elevated) removes the task; the widget menu runs it.
//   /settings           -> the settings window on its own.   /update -> install the latest release quietly.
//   /panel (or /breakdown) -> the panel on its own: RAM, CPU, GPU and the efficiency guard, with a flush button each.
// 1.3 (Nader 2026-11-27): four flushes, RAM (toilet), CPU (exhaust fan), GPU (mirror), All (bubble bath); the widget opens the panel;
//   every action has a shortcut you can change; the efficiency guard keeps browsers out of Windows' efficiency mode. design/1.3-ux.md
// Language: settings.ini language=auto|en|ar (Strings.cs); NJFLUSH_LANG overrides it for off-screen renders.
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
        Settings.Load(); Tx.Resolve();
        if (mode == "/widget") { Widget.Start(); return 0; }
        if (mode == "/flush") {
            // which flush: request.txt (schtasks /run takes no arguments); none = All
            var req = Flush.TakeRequest();
            if (Settings.CooldownLeft(req.Kind) > 0) return 0;   // a flush ran moments ago: repeated triggers never stack (Nader 2026-11-23)
            Settings.MarkFlush(req.Kind);
            var job = new Flush(req);
            if (!Settings.Animation) job.Run();
            else if (req.Kind == "ram" && !job.RamNeeded) Scenes.Run("fine", job, null);
            else if (req.Kind == "ram") Overlay.Run(null, job);
            else Scenes.Run(req.Kind, job, null);
            StartWidget(); return 0;
        }
        if (mode == "/preview") {  // light|medium|heavy|clogged | cpu|gpu|all[-0..3 or -name] [solid], no flush; "solid" = opaque backdrop for screen recordings
            Overlay.Solid = Scenes.Solid = args.Any(a => a.ToLowerInvariant() == "solid");
            string what = args.Length > 1 ? args[1].ToLowerInvariant() : "medium";
            string pk; int pv;
            if (Scenes.ParsePreview(what, out pk, out pv)) Scenes.Run(pk, null, what); else Overlay.Run(what, null);
            return 0;
        }
        if (mode == "/render" && args.Length > 2) {   // design check, off screen: frames + a contact sheet into a folder
            Scenes.RenderDir = args[2]; string rk; int rv;
            if (Scenes.ParsePreview(args[1], out rk, out rv)) Scenes.Run(null, null, args[1]); else Overlay.Run(args[1].ToLowerInvariant(), null);
            return 0;
        }
        if (mode == "/render-ui" && args.Length > 2) { UiRender.Run(args[1].ToLowerInvariant(), args[2]); return 0; }  // panel pages / widget, off screen
        if (mode == "/panel" || mode == "/breakdown") { Panel.StartPage = args.Length > 1 ? args[1].ToLowerInvariant() : null; Live.Start(); Panel.Run(); return 0; }
        if (mode == "/settings") { Panel.StartPage = "settings"; Live.Start(); Panel.Run(); return 0; }  // the panel's Settings page, standalone
        if (mode == "/update") {  // quiet update: 0 updated, 1 already latest, 2 GitHub unreachable, 3 refused (checksum / file)
            var u = Updater.Check(); if (u.Error != null) return 2; if (!u.Newer) return 1; return Updater.Apply(u) == null ? 0 : 3;
        }
        if (mode == "/uninstall") return Run("schtasks.exe", "/delete /tn " + TaskName + " /f", null);
        if (mode == "/install") return Install() ? 0 : 1;
        // 1.3: only a plain double-click (no argument) installs and opens the panel; an unknown argument does nothing (2026-11-27, a mistyped
        // "NJ-Flush.exe cpu-2" copied a test build over the installed one and ran Flush all)
        if (mode.Length > 0) return 2;
        return Launch();
    }

    // A double-click on NJ-Flush.exe (Nader 2026-11-27: "it doesn't flush, it opens the tool; if it is open, it refreshes it").
    // First run: copy to the install folder and register the flush task (one UAC prompt). Then: ask the widget to open the panel
    // (open.req, which it checks every second); with no widget running, start one that opens the panel as it starts.
    // Ctrl+Alt+F and the panel's Flush all still flush.
    static int Launch() {
        string self = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(self, Exe, StringComparison.OrdinalIgnoreCase)) CopySelf(self);
        if (!TaskOk()) {
            try {
                using (var p = Process.Start(new ProcessStartInfo(Exe, "/install") { Verb = "runas", UseShellExecute = true })) {
                    p.WaitForExit();
                    if (p.ExitCode != 0) { Tx.Box(null, Tx.T("app.task_fail"), "NJ-Flush", MessageBoxButtons.OK, MessageBoxIcon.None); return 1; }
                }
            } catch (System.ComponentModel.Win32Exception) {
                Tx.Box(null, Tx.T("app.admin_once"), "NJ-Flush", MessageBoxButtons.OK, MessageBoxIcon.None);
                return 1;
            }
        }
        AskPanel();
        if (!WidgetRunning()) { Process.Start(new ProcessStartInfo(Exe, "/widget") { UseShellExecute = false }); return 0; }
        // a widget runs: it takes the request within a second. An older widget does not know it: after 2.5 s open the panel here.
        for (int i = 0; i < 25 && File.Exists(PanelRequest); i++) Thread.Sleep(100);
        if (File.Exists(PanelRequest)) { try { File.Delete(PanelRequest); } catch (Exception) { } Live.Start(); Panel.Run(); }
        return 0;
    }

    public static readonly string PanelRequest = Path.Combine(Dir, "open.req");
    static void AskPanel() { try { Directory.CreateDirectory(Dir); File.WriteAllText(PanelRequest, DateTime.Now.Ticks.ToString()); } catch (Exception) { } }
    // the widget holds the "NJ-Flush-Widget" mutex; an elevated widget's mutex cannot be opened for writing from here, which also means it exists
    static bool WidgetRunning() {
        try { Mutex m; if (Mutex.TryOpenExisting("NJ-Flush-Widget", out m)) { m.Dispose(); return true; } return false; }
        catch (UnauthorizedAccessException) { return true; }
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
    // kind: ram | cpu | gpu | all. auto: the widget's auto-flush (never restarts the GPU)
    public static void FlushNow(string kind, bool auto = false, bool deep = false) {
        if (Settings.CooldownLeft(kind) > 0) return;
        Flush.Ask(kind, auto, deep);
        new Thread(() => Run("schtasks.exe", "/run /tn " + TaskName, null)) { IsBackground = true }.Start();
    }

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
    public static string Language = "auto";   // auto | en | ar (see Strings.cs)
    public static bool Animation = true, Sound = true, RestartExplorer = false, AutoFlush = false, Hotkey = true, AutoUpdate = true;
    public static int Volume = 100, IdleOpacity = 40;  // volume: % of the mixed level (itself <= 10% full scale); widget brightness when idle
    public static int AutoAt = 90;  // auto-flush when RAM in use stays at or above this % for 30 s, at most once per 10 min
    // 1.3 (freeze report 2026-11-27): a RAM flush below this much RAM in use does nothing (the cache was free memory already)
    public static int MinRam = 75;
    public static int WidgetX = -1, WidgetY = -1;   // the widget's saved spot (screen px); -1 = above the clock
    // 1.3: Flush all restarts the GPU on a Heavy/Clogged tier or VRAM 85%+ (gpureset), calms busy apps (calmcpu); widget bars; the guard
    public static bool GpuReset = true, CalmCpu = true, WidgetBars = true, Guard = true, GuardBattery = true;
    public static System.Collections.Generic.List<string> GuardApps = new System.Collections.Generic.List<string>(global::Guard.Browsers);
    // shortcuts, one per action (empty = none); Hotkey above switches them all off
    public static readonly string[] Actions = { "all", "ram", "cpu", "gpu", "panel" };
    public static readonly string[] DefaultKeys = { "Ctrl+Alt+F", "Ctrl+Alt+R", "Ctrl+Alt+C", "Ctrl+Alt+G", "Ctrl+Alt+P" };
    public static string[] Keys_ = (string[])DefaultKeys.Clone();

    public static void Defaults() {
        Language = "auto"; Animation = Sound = Hotkey = AutoUpdate = true; AutoFlush = RestartExplorer = false;
        Volume = 100; IdleOpacity = 40; AutoAt = 90; Cooldown = 60; MinRam = 75;
        GpuReset = CalmCpu = WidgetBars = Guard = GuardBattery = true; WidgetX = WidgetY = -1;
        GuardApps = new System.Collections.Generic.List<string>(global::Guard.Browsers);
        Keys_ = (string[])DefaultKeys.Clone();
    }
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Load() {
        try {
            var lines = File.ReadAllLines(File_);
            Parse(lines);
            // 1.3 (freeze report 2026-11-27): 1.2 saved restartexplorer=1 for everyone, so the new default would never reach them.
            // A settings file from before 1.3 (no minram= line) gets Explorer restart turned off once; a later choice is kept.
            if (!lines.Any(l => l.TrimStart().StartsWith("minram=", StringComparison.OrdinalIgnoreCase))) { RestartExplorer = false; Save(); }
        } catch (Exception) { }  // no file yet: defaults
    }
    // settings.ini lines; an unknown key or an out-of-range number keeps the current value (tests: tests/NJFlush.Tests)
    public static void Parse(System.Collections.Generic.IEnumerable<string> lines) {
            foreach (string line in lines) {
                int i = line.IndexOf('='); if (i < 0) continue;
                string k = line.Substring(0, i).Trim().ToLowerInvariant(), v = line.Substring(i + 1).Trim();
                bool b = v == "1" || v.ToLowerInvariant() == "true"; int n;
                if (k == "language") { string lg = Tx.ParseLang(v); if (lg != null) Language = lg; }
                else if (k == "animation") Animation = b; else if (k == "sound") Sound = b;
                else if (k == "restartexplorer") RestartExplorer = b; else if (k == "autoflush") AutoFlush = b;
                else if (k == "autoat" && int.TryParse(v, out n) && n >= 50 && n <= 99) AutoAt = n;
                else if (k == "cooldown" && int.TryParse(v, out n) && n >= 10 && n <= 600) Cooldown = n;
                else if (k == "hotkey") Hotkey = b;
                else if (k == "autoupdate") AutoUpdate = b;
                else if (k == "volume" && int.TryParse(v, out n) && n >= 0 && n <= 100) Volume = n;
                else if (k == "idleopacity" && int.TryParse(v, out n) && n >= 20 && n <= 100) IdleOpacity = n;
                else if (k == "minram" && int.TryParse(v, out n) && n >= 50 && n <= 99) MinRam = n;
                else if (k == "widgetx" && int.TryParse(v, out n) && n >= -1 && n <= 100000) WidgetX = n;
                else if (k == "widgety" && int.TryParse(v, out n) && n >= -1 && n <= 100000) WidgetY = n;
                else if (k == "gpureset") GpuReset = b; else if (k == "calmcpu") CalmCpu = b; else if (k == "widgetbars") WidgetBars = b;
                else if (k == "guard") Guard = b; else if (k == "guardbattery") GuardBattery = b;
                else if (k == "guardapps") GuardApps = v.Split(',').Select(x => Procs.Short(x.Trim()).ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
                else if (k.StartsWith("key.")) {
                    int a = Array.IndexOf(Actions, k.Substring(4)); string norm = Hotkeys.Normal(v);
                    if (a >= 0 && norm != null) Keys_[a] = norm;   // an unreadable shortcut keeps the current one
                }
            }
    }
    public static void Save() {
        try {
            Directory.CreateDirectory(App.Dir);
            File.WriteAllText(File_, string.Format("language=" + Language + "\r\nanimation={0}\r\nsound={1}\r\nrestartexplorer={2}\r\nautoflush={3}\r\nautoat={4}\r\ncooldown={5}\r\n" +
                "hotkey={6}\r\nvolume={7}\r\nidleopacity={8}\r\nautoupdate={9}\r\ngpureset={10}\r\ncalmcpu={11}\r\nwidgetbars={12}\r\nguard={13}\r\n" +
                "guardbattery={14}\r\nguardapps={15}\r\nminram={16}\r\nwidgetx={17}\r\nwidgety={18}\r\n",
                Animation ? 1 : 0, Sound ? 1 : 0, RestartExplorer ? 1 : 0, AutoFlush ? 1 : 0, AutoAt, Cooldown, Hotkey ? 1 : 0, Volume, IdleOpacity, AutoUpdate ? 1 : 0,
                GpuReset ? 1 : 0, CalmCpu ? 1 : 0, WidgetBars ? 1 : 0, Guard ? 1 : 0, GuardBattery ? 1 : 0, string.Join(",", GuardApps), MinRam, WidgetX, WidgetY) +
                string.Concat(Actions.Select((a, i) => "key." + a + "=" + Keys_[i] + "\r\n")));
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
    public static string Last() { if (Live.Demo) return "2026-11-27 21:25  " + Tx.F("demo.last", "18.6", "12.1"); try { return File.ReadAllText(LastFile).Trim(); } catch (Exception) { return Tx.T("last.none"); } }

    // cooldown: seconds after a flush starts before the next of the same kind may run (settings.ini cooldown=, 10-600, default 60).
    // 1.3: one clock per kind (flush-ram.at ...); All starts all three and waits for all three.
    public static int Cooldown = 60;
    static string StartFile(string kind) { return Path.Combine(App.Dir, "flush-" + kind + ".at"); }
    public static string[] Parts(string kind) { return kind == "all" || kind == null ? new[] { "ram", "cpu", "gpu" } : new[] { kind }; }
    public static void MarkFlush(string kind) {
        try { Directory.CreateDirectory(App.Dir); foreach (string k in Parts(kind)) File.WriteAllText(StartFile(k), DateTime.Now.Ticks.ToString()); } catch (Exception) { }
    }
    public static int CooldownLeft(string kind) {
        int left = 0;
        foreach (string k in Parts(kind))
            try { left = Math.Max(left, CooldownLeft(long.Parse(File.ReadAllText(StartFile(k)).Trim()), DateTime.Now, Cooldown)); } catch (Exception) { }
        return left;
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

// Updates (Nader 2026-11-23, "like my other tools"): reads the latest GitHub release, compares it with this version, and on request
// downloads NJ-Flush.exe over HTTPS, checks its SHA-256 against the digest GitHub publishes, swaps it in and restarts the widget.
static class Updater {
    public const string Version = "1.3.0", Repo = "NJ-Burrito/NJ-Flush";
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
        } catch (Exception e) { i.Error = Tx.F("upd.unreachable", e.Message); }
        return i;
    }

    // a release reply -> tag, exe URL, its SHA-256 (from GitHub's digest) and whether it is newer than `current`
    public static Info Parse(string json, string current) {
        var i = new Info();
        try {
            var tag = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            var asset = System.Text.RegularExpressions.Regex.Match(json, "\"name\"\\s*:\\s*\"NJ-Flush\\.exe\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success || !asset.Success) { i.Error = Tx.T("upd.no_exe"); return i; }
            i.Tag = tag.Groups[1].Value; i.Url = asset.Groups[1].Value;
            var sha = System.Text.RegularExpressions.Regex.Match(asset.Value, "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"");
            if (sha.Success) i.Sha = sha.Groups[1].Value.ToLowerInvariant();
            i.Newer = new Version(i.Tag.TrimStart('v', 'V')) > new Version(current);
        } catch (Exception e) { i.Error = Tx.F("upd.unreadable", e.Message); }
        return i;
    }
    public static bool ShaMatches(byte[] data, string sha) {
        if (data == null || sha == null || data.Length < 2 || data[0] != 'M' || data[1] != 'Z') return false;
        using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant() == sha;
    }

    // returns null when the new version is in place and the widget restart is queued; otherwise the reason it stopped
    public static string Apply(Info i) {
        if (i == null || !i.Newer || i.Url == null) return Tx.T("upd.no_newer");
        if (i.Sha == null) return Tx.T("upd.no_sha");
        string tmp = App.Exe + ".new";
        try {
            Directory.CreateDirectory(App.Dir);
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
            using (var wc = new System.Net.WebClient()) { wc.Headers.Add("User-Agent", "NJ-Flush/" + Version); wc.DownloadFile(i.Url, tmp); }
            if (!ShaMatches(File.ReadAllBytes(tmp), i.Sha)) { File.Delete(tmp); return Tx.T("upd.bad_sha"); }
            string old = App.Exe + ".old";
            try { File.Delete(old); } catch (Exception) { }
            if (File.Exists(App.Exe)) File.Move(App.Exe, old);  // a running exe can be renamed, not overwritten
            File.Move(tmp, App.Exe);
        } catch (Exception e) { try { File.Delete(tmp); } catch (Exception) { } return Tx.F("upd.failed", e.Message); }
        foreach (var p in Process.GetProcessesByName("NJ-Flush"))   // the old widget holds the one-widget lock: stop it (the caller exits itself)
            if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); } catch (Exception) { }
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + App.Exe + "\" /widget") { CreateNoWindow = true, UseShellExecute = false });
        return null;
    }

    // the menu / settings action: check, ask, install; exits the caller's app when the swap is done
    public static void Interactive(IWin32Window owner) {
        Info i = null;
        Cursor.Current = Cursors.WaitCursor; i = Check(); Cursor.Current = Cursors.Default;
        string title = Tx.T("upd.title");
        if (i.Error != null) { Tx.Box(owner, i.Error, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!i.Newer) { Tx.Box(owner, Tx.F("upd.latest", Version), title, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (Tx.Box(owner, Tx.F("upd.available", i.Tag.TrimStart('v'), Version), title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string err = Apply(i);
        if (err != null) { Tx.Box(owner, err, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Application.Exit();
    }
}