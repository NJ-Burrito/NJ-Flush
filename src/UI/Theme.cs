// NJ-Flush design system, shared by the panel (WPF) and the widget (WinForms): one palette, one type scale, one spacing scale, one set of radii.
//   Colour   dark navy surfaces (Bg > Sidebar / Surface > Raised), 1 px hairlines, text Fg / Muted / Dim; one identity colour per resource
//            (RAM cyan, CPU violet, GPU green, guard blue); yellow, orange and red appear only when a resource is under load.
//   Type     Thmanyah Sans only. Caption 11, Small 12, Body 13, Strong 14, Title 16, Page 22, Display 36 (WPF px; the widget uses points).
//   Space    a 4 px grid: S1 4, S2 8, S3 12, S4 16, S5 24, S6 32. Radii: small 6 (chips, keys), medium 8 (buttons, fields), large 12 (cards).
using System;
using System.Linq;
using D = System.Drawing;
using WM = System.Windows.Media;

static class Theme {
    // surfaces
    public const string Bg = "#0F1723", Sidebar = "#0B121C", Surface = "#141F2E", Raised = "#1B2A3E", Tile = "#121B29", Line = "#223147", LineStrong = "#33465F";
    // text
    public const string Fg = "#E6EDF3", Muted = "#9AA8B6", Dim = "#7D8DA0", Ink = "#062A33";
    // identity: RAM, CPU, GPU, guard, settings (neutral)
    public static readonly string[] Ident = { "#22D3EE", "#A78BFA", "#4ADE80", "#60A5FA", "#9AA8B6" };
    // pressure: only for load
    public const string Warn = "#FACC15", Hot = "#FB923C", Crit = "#F87171";
    // type scale (px)
    public const double Caption = 11, Small = 12, Body = 13, Strong = 14, Title = 16, Page = 22, Display = 36;
    // spacing (px, 4 grid)
    public const double S1 = 4, S2 = 8, S3 = 12, S4 = 16, S5 = 24, S6 = 32;
    // radii
    public const double RSm = 6, RMd = 8, RLg = 12;
    // layout
    public const double Content = 640, NavW = 200, Hit = 44;

    // pressure colour for a load percentage (RAM tiers: 80 / 90 / 96), or null for the resource's own colour
    public static string Load(double pct) { return pct >= 96 ? Crit : pct >= 90 ? Hot : pct >= 80 ? Warn : null; }

    // WPF
    public static WM.Color Col(string hex) { return (WM.Color)WM.ColorConverter.ConvertFromString(hex); }
    public static WM.Color Tint(string hex, byte alpha) { var c = Col(hex); return WM.Color.FromArgb(alpha, c.R, c.G, c.B); }
    public static WM.SolidColorBrush Brush(string hex) { var b = new WM.SolidColorBrush(Col(hex)); b.Freeze(); return b; }
    public static WM.SolidColorBrush Brush(WM.Color c) { var b = new WM.SolidColorBrush(c); b.Freeze(); return b; }

    // GDI (the widget)
    public static D.Color Gdi(string hex) { var c = Col(hex); return D.Color.FromArgb(c.A, c.R, c.G, c.B); }
    public static D.Color Gdi(string hex, int alpha) { var c = Col(hex); return D.Color.FromArgb(alpha, c.R, c.G, c.B); }
}
