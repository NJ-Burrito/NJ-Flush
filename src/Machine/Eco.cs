using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Eco {
    [StructLayout(LayoutKind.Sequential)] struct Throttle { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GetProcessInformation(IntPtr h, int cls, ref Throttle t, int size);
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr h, int cls, ref Throttle t, int size);
    [DllImport("kernel32.dll")] static extern uint GetPriorityClass(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetPriorityClass(IntPtr h, uint c);
    const int PowerThrottling = 4; const uint ExecutionSpeed = 1, Idle = 0x40, Normal = 0x20, BelowNormal = 0x4000;
    const uint QueryLimited = 0x1000, SetInfo = 0x0200;

    // efficiency mode = EcoQoS on (what Task Manager's leaf shows); measured on Nader's PC 2026-11-27: 12 Opera processes, Mattermost,
    // Steam and VS Code had it, with Idle priority
    public static bool IsOn(int pid) {
        if (Live.Demo) return Live.DemoEco.Contains(pid);
        IntPtr h = OpenProcess(QueryLimited, false, pid); if (h == IntPtr.Zero) return false;
        try { var t = new Throttle { Version = 1 }; return GetProcessInformation(h, PowerThrottling, ref t, 12) && On(t.ControlMask, t.StateMask); }
        finally { CloseHandle(h); }
    }
    public static bool On(uint control, uint state) { return (control & ExecutionSpeed) != 0 && (state & ExecutionSpeed) != 0; }

    public static bool Set(int pid, bool on) {
        IntPtr h = OpenProcess(QueryLimited | SetInfo, false, pid); if (h == IntPtr.Zero) return false;
        try {
            // off = EcoQoS explicitly off (Windows does not put it back on its own); on = EcoQoS + Idle, as Task Manager does
            var t = new Throttle { Version = 1, ControlMask = ExecutionSpeed, StateMask = on ? ExecutionSpeed : 0 };
            bool ok = SetProcessInformation(h, PowerThrottling, ref t, 12);
            uint pc = GetPriorityClass(h);
            if (on) SetPriorityClass(h, Idle); else if (pc == Idle || pc == BelowNormal) SetPriorityClass(h, Normal);
            return ok;
        } finally { CloseHandle(h); }
    }
}
