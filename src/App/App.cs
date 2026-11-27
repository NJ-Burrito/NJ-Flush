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
