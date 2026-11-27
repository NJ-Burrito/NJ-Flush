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
