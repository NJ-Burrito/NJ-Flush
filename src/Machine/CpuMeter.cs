using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class CpuMeter {
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    long pIdle, pTotal;
    public CpuMeter() { Sample(); }
    public double Sample() {
        long i, k, u; if (!GetSystemTimes(out i, out k, out u)) return 0;
        long total = k + u, dt = total - pTotal, di = i - pIdle;  // kernel time includes idle time
        pIdle = i; pTotal = total;
        return Pct(dt, di);
    }
    public static double Pct(long dTotal, long dIdle) { return dTotal <= 0 ? 0 : Math.Max(0, Math.Min(100, (dTotal - dIdle) * 100.0 / dTotal)); }
    public static string Model() {
        if (Live.Demo) return "Intel Core i7-12700";
        try {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                return System.Text.RegularExpressions.Regex.Replace(((string)k.GetValue("ProcessorNameString", "")).Replace("(R)", "").Replace("(TM)", ""), @"\s+", " ").Trim();
        } catch (Exception) { return "CPU"; }
    }
}
