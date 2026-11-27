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
