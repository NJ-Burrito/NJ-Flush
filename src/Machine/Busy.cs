using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
