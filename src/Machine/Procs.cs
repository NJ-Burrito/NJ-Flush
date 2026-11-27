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
