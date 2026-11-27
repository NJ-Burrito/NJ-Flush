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
