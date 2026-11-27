// NJ-Flush: the shower scene's drawing vocabulary and parts (the RAM stick, the CPU, the graphics card, grime, bubbles, drips).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

static partial class Scenes {
    class ShBub { public WC.Canvas C; public WM.ScaleTransform S; public WM.TranslateTransform T; public double D; }
    class ShMark { public WC.Canvas Root; public WS.Rectangle Lit; public WC.TextBlock Label; public WS.Ellipse Amber, Lamp, Glow; public WS.Path Check; public WM.ScaleTransform Pop; public double Cx; }

    // ---------- drawing vocabulary ----------
    static string ShF(double d) { return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
    // gradient from (offset, colour) pairs; colours may carry alpha (#AARRGGBB)
    static WM.LinearGradientBrush ShLin(double x1, double y1, double x2, double y2, params object[] st) {
        var g = new WM.LinearGradientBrush { StartPoint = new W.Point(x1, y1), EndPoint = new W.Point(x2, y2) };
        for (int i = 0; i + 1 < st.Length; i += 2) g.GradientStops.Add(new WM.GradientStop(C((string)st[i + 1]), Convert.ToDouble(st[i])));
        return g;
    }
    static WM.RadialGradientBrush ShRad(double ox, double oy, params object[] st) {
        var g = new WM.RadialGradientBrush { GradientOrigin = new W.Point(ox, oy) };
        for (int i = 0; i + 1 < st.Length; i += 2) g.GradientStops.Add(new WM.GradientStop(C((string)st[i + 1]), Convert.ToDouble(st[i])));
        return g;
    }
    static WS.Rectangle ShR(double x, double y, double w, double h, double r, WM.Brush f) {
        return At(new WS.Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = f }, x, y);
    }
    static WS.Ellipse ShO(double x, double y, double w, double h, WM.Brush f) { return At(new WS.Ellipse { Width = w, Height = h, Fill = f }, x, y); }
    static WS.Path ShP(string d, WM.Brush f) { return new WS.Path { Data = WM.Geometry.Parse(d), Fill = f }; }
    static WS.Path ShS(string d, WM.Brush stroke, double th) {
        return new WS.Path { Data = WM.Geometry.Parse(d), Stroke = stroke, StrokeThickness = th, StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round };
    }
    static void ShAdd(WC.Canvas c, params W.UIElement[] es) { foreach (var e in es) c.Children.Add(e); }
    static T ShCache<T>(T e) where T : W.UIElement { e.CacheMode = new WM.BitmapCache(1.5); return e; }
    static WM.Brush ShChromeV() { return ShLin(0, 0, 0, 1, 0.0, "#FFFFFF", 0.3, "#CBD5E1", 0.65, "#64748B", 1.0, "#A3B1C2"); }
    static WM.Brush ShChromeH() { return ShLin(0, 0, 1, 0, 0.0, "#475569", 0.2, "#E2E8F0", 0.38, "#FFFFFF", 0.6, "#94A3B8", 0.85, "#CBD5E1", 1.0, "#475569"); }
    static WM.Brush ShGold() { return ShLin(0, 0, 1, 0, 0.0, "#FEF3C7", 0.5, "#FACC15", 1.0, "#A16207"); }
    static WM.Brush ShShadow() { return ShRad(0.5, 0.5, 0.0, "#88000000", 1.0, "#00000000"); }
    static WA.KeyTime ShKT(double ms) { return WA.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)); }

    // a glossy soap bubble: translucent blue body, bright specular dot, thin rim
    static ShBub ShBubble(double d) {
        var c = new WC.Canvas { Width = d, Height = d, RenderTransformOrigin = new W.Point(0.5, 0.5) };
        var s = new WM.ScaleTransform(0, 0); var t = new WM.TranslateTransform(); var tg = new WM.TransformGroup(); tg.Children.Add(s); tg.Children.Add(t); c.RenderTransform = tg;
        var body = ShO(0, 0, d, d, ShRad(0.32, 0.28, 0.0, "#FFFFFFFF", 0.28, "#E6EAF8FF", 0.75, "#8CA5DCF5", 1.0, "#D96EC0EE"));
        body.Stroke = Br("#FFFFFF", 170); body.StrokeThickness = 0.8;
        c.Children.Add(body);
        c.Children.Add(ShO(d * 0.2, d * 0.14, d * 0.34, d * 0.22, Br("#FFFFFF", 240)));
        return new ShBub { C = c, S = s, T = t, D = d };
    }
    // a droplet thrown on an arc: up fast, down faster; it leaves the scene faded
    static void ShBurst(WC.Canvas fx, double cx, double cy, int n, double spread, double peak, int ms, int delay, Random r) {
        for (int i = 0; i < n; i++) {
            double d = 3 + r.NextDouble() * 3.5;
            var tt = new WM.TranslateTransform();
            var e = new WS.Ellipse { Width = d, Height = d * 1.25, Opacity = 0, RenderTransform = tt, Fill = ShRad(0.35, 0.3, 0.0, "#FFFFFFFF", 1.0, "#E038BDF8") };
            fx.Children.Add(At(e, cx - d / 2, cy - d / 2));
            double ang = n == 1 ? 0 : (i / (double)(n - 1)) * 2 - 1;
            double dx = ang * spread * (0.7 + r.NextDouble() * 0.5), pk = peak * (0.6 + r.NextDouble() * 0.5);
            int dl = delay + r.Next(0, 60);
            var ky = new WA.DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(dl) };
            ky.KeyFrames.Add(new WA.EasingDoubleKeyFrame(-pk, ShKT(ms * 0.42), QOut));
            ky.KeyFrames.Add(new WA.EasingDoubleKeyFrame(16, ShKT(ms), QIn));
            tt.BeginAnimation(WM.TranslateTransform.YProperty, ky);
            tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(dl) });
            var ko = K(0, 1, ms * 0.7, 1, ms, 0); ko.BeginTime = TimeSpan.FromMilliseconds(dl);
            e.BeginAnimation(W.UIElement.OpacityProperty, ko);
        }
    }
    // a flat ring that spreads on a surface
    static void ShRipple(WC.Canvas fx, double cx, double cy, double w, int delay) {
        var st = new WM.ScaleTransform(0.2, 0.2, w / 2, w * 0.18);
        var e = new WS.Ellipse { Width = w, Height = w * 0.36, Opacity = 0, RenderTransform = st, Stroke = Br("#E0F7FF", 210), StrokeThickness = 1.6 };
        fx.Children.Add(At(e, cx - w / 2, cy - w * 0.18));
        S2(st, A(0.2, 1, 420, QOut, delay));
        var ko = K(0, 0.95, 420, 0); ko.BeginTime = TimeSpan.FromMilliseconds(delay);
        e.BeginAnimation(W.UIElement.OpacityProperty, ko);
    }
    // a hanging bead that swells, then falls and lands
    static void ShDrip(WC.Canvas fx, double x, double y0, double fall, int fallMs) {
        var tt = new WM.TranslateTransform(); var st = new WM.ScaleTransform(0, 0, 0, 0);
        var g = new WM.TransformGroup(); g.Children.Add(st); g.Children.Add(tt);
        var d = new WS.Path { Data = WM.Geometry.Parse("M0,-7 C0,-7 5,-1 5,3 A5,5 0 1 1 -5,3 C-5,-1 0,-7 0,-7 Z"), Fill = ShRad(0.35, 0.4, 0.0, "#FFFFFFFF", 0.5, "#B3BAE6FD", 1.0, "#E038BDF8"), RenderTransform = g };
        fx.Children.Add(At(d, x, y0));
        S2(st, A(0, 1, 130, Back));
        tt.BeginAnimation(WM.TranslateTransform.YProperty, A(0, fall, fallMs, QIn, 150));
        d.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, 150 + fallMs - 10, 1, 150 + fallMs, 0));
    }
    static string ShSpiral(double rot) {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i <= 26; i++) {
            double a = i * 0.30 + rot, r = 4 + i * 1.6;
            sb.Append(i == 0 ? "M" : " L").Append(ShF(Math.Cos(a) * r)).Append(",").Append(ShF(Math.Sin(a) * r));
        }
        return sb.ToString();
    }

    // ---------- the three parts, each 100 x 70 local, standing on y = 66 ----------
    static WC.Canvas ShRam() {
        var c = new WC.Canvas { Width = 100, Height = 70 };
        ShAdd(c, ShR(1, 10, 98, 47, 4, ShLin(0, 0, 0.15, 1, 0.0, "#7BEBA8", 0.14, "#34D17A", 0.58, "#16A34A", 1.0, "#0F5132")),
                 ShR(3, 11, 94, 1.6, 0.8, Br("#FFFFFF", 150)), ShR(1, 55, 98, 2, 0, Br("#052E16", 150)));
        for (int k = 0; k < 16; k++) { if (k == 9) continue; ShAdd(c, ShR(4.5 + k * 5.7, 55, 3.6, 10.5, 0.8, ShGold())); }
        double[] cx = { 7, 27, 63, 83 };
        foreach (double x in cx) ShAdd(c, ShR(x, 17, 14, 22, 1.8, ShLin(0, 0, 1, 1, 0.0, "#4B5563", 0.4, "#1F2937", 1.0, "#030712")),
                                          ShR(x + 1, 17.8, 12, 1.3, 0.6, Br("#FFFFFF", 95)), ShO(x + 2, 34, 2, 2, Br("#9CA3AF")));
        ShAdd(c, ShR(46, 25, 3.5, 6, 0.8, Br("#D6B26A")), ShR(51, 25, 3.5, 6, 0.8, Br("#D6B26A")), ShR(46, 40, 8.5, 3, 0.8, Br("#1F2937")),
                 ShR(44, 46, 14, 1.2, 0.6, Br("#FFFFFF", 60)), ShO(4, 22, 3, 3, Br("#FACC15")), ShO(93, 22, 3, 3, Br("#FACC15")));
        return c;
    }
    static WC.Canvas ShCpu() {
        var c = new WC.Canvas { Width = 100, Height = 70 };
        ShAdd(c, ShR(16, 0, 68, 62, 6, ShLin(0, 0, 1, 1, 0.0, "#3B5BC0", 0.45, "#1E3A8A", 1.0, "#0F1D4A")),
                 ShR(18, 1.5, 64, 1.5, 0.7, Br("#FFFFFF", 110)), ShR(16, 56, 68, 6, 3, Br("#0A1233", 170)));
        // little capacitors around the heat spreader
        for (int k = 0; k < 4; k++) ShAdd(c, ShR(22 + k * 4.5, 5, 3, 5, 0.6, Br("#D6B26A")), ShR(61 + k * 4.5, 5, 3, 5, 0.6, Br("#D6B26A")), ShR(22 + k * 4.5, 53, 3, 4, 0.6, Br("#9CA3AF")), ShR(61 + k * 4.5, 53, 3, 4, 0.6, Br("#9CA3AF")));
        ShAdd(c, ShR(27, 11, 46, 40, 4, ShLin(0, 0, 1, 1, 0.0, "#FFFFFF", 0.3, "#CBD5E1", 0.58, "#8EA0B5", 0.82, "#E2E8F0", 1.0, "#64748B")),
                 ShR(31, 15, 38, 32, 3, ShLin(1, 1, 0, 0, 0.0, "#94A3B8", 0.5, "#E2E8F0", 1.0, "#FFFFFF")),
                 ShR(33, 17, 34, 1.5, 0.7, Br("#FFFFFF", 200)));
        for (int k = 0; k < 3; k++) ShAdd(c, ShR(37, 24 + k * 6, 26 - k * 4, 1.6, 0.8, Br("#334155", 130)), ShR(37, 25.6 + k * 6, 26 - k * 4, 1, 0.5, Br("#FFFFFF", 170)));
        ShAdd(c, ShP("M16,0 L30,0 L16,14 Z", ShGold()));
        for (int k = 0; k < 14; k++) ShAdd(c, ShR(19.5 + k * 4.4, 60, 2.8, 6, 0.7, ShGold()));
        return c;
    }
    static WC.Canvas ShGpu() {
        var c = new WC.Canvas { Width = 100, Height = 70 };
        for (int k = 0; k < 3; k++) ShAdd(c, ShR(32 + k * 7, 4, 4.5, 8, 1.5, ShLin(0, 0, 0, 1, 0.0, "#FCD34D", 0.5, "#EA8A1A", 1.0, "#92400E")));
        ShAdd(c, ShR(0, 9, 5, 50, 1.5, ShChromeH()), ShR(79, 5, 16, 7, 1.5, Br("#111827")));
        ShAdd(c, ShP("M8,17 L19,10 L94,10 L98,16 L98,57 L8,57 Z", ShLin(0, 0, 0.2, 1, 0.0, "#6B7C93", 0.14, "#475569", 0.6, "#1E293B", 1.0, "#0A101D")),
                 ShR(20, 10.5, 72, 1.5, 0.7, Br("#FFFFFF", 150)), ShP("M54,10 L66,10 L58,57 L46,57 Z", Br("#22D3EE", 70)),
                 ShR(8, 55, 90, 2, 0, Br("#000000", 90)));
        double[] fxs = { 30, 72 };
        foreach (double fcx in fxs) {
            const double fcy = 34;
            ShAdd(c, ShO(fcx - 18, fcy - 18, 36, 36, ShRad(0.5, 0.5, 0.0, "#0B1220", 0.8, "#1E293B", 1.0, "#334155")));
            var gg = new WM.GeometryGroup { FillRule = WM.FillRule.Nonzero };
            for (int k = 0; k < 7; k++) { var pg = WM.Geometry.Parse("M0,-2 C5,-3 11,-8 9.5,-14 C3,-15.5 -3,-8 0,-2 Z").Clone(); pg.Transform = new WM.RotateTransform(k * (360.0 / 7)); gg.Children.Add(pg); }
            ShAdd(c, At(new WS.Path { Data = gg, Fill = ShLin(0, 0, 1, 1, 0.0, "#CBD5E1", 0.5, "#7C8DA3", 1.0, "#3B4A5E") }, fcx, fcy),
                     ShO(fcx - 18, fcy - 18, 36, 36, null), ShO(fcx - 5, fcy - 5, 10, 10, ShRad(0.35, 0.3, 0.0, "#FFFFFF", 0.5, "#94A3B8", 1.0, "#334155")));
            var ring = (WS.Ellipse)c.Children[c.Children.Count - 2]; ring.Stroke = Br("#94A3B8", 170); ring.StrokeThickness = 1.6;
        }
        ShAdd(c, ShO(11, 14, 3.5, 3.5, Br("#94A3B8")), ShO(11, 49, 3.5, 3.5, Br("#94A3B8")));
        for (int k = 0; k < 14; k++) { if (k == 8) continue; ShAdd(c, ShR(18 + k * 4.5, 57, 3, 8, 0.7, ShGold())); }
        return c;
    }
    // soot, grease and a dust film on one part
    static WC.Canvas ShGrime(Random r, double fx0, double fy0, double fw, double fh) {
        var c = new WC.Canvas { Width = 100, Height = 70 };
        c.Children.Add(ShR(fx0, fy0, fw, fh, 5, ShLin(0, 0, 0, 1, 0.0, "#6E5A3C", 1.0, "#3B2A18")));
        ((WS.Rectangle)c.Children[0]).Opacity = 0.34;
        for (int k = 0; k < 7; k++) {
            double d = 14 + r.Next(0, 18);
            c.Children.Add(ShO(fx0 + r.NextDouble() * (fw - d * 0.6) - d * 0.2, fy0 + r.NextDouble() * (fh - d * 0.5) - d * 0.2, d, d * 0.78, ShRad(0.5, 0.5, 0.0, "#E02B1A0C", 0.6, "#9A3B2A14", 1.0, "#003B2A14")));
        }
        for (int k = 0; k < 4; k++) { double d = 2 + r.Next(0, 3); c.Children.Add(ShO(fx0 + r.NextDouble() * fw, fy0 + r.NextDouble() * fh, d, d, Br("#2B1A0C", 210))); }
        for (int k = 0; k < 2; k++) c.Children.Add(ShR(fx0 + 6 + r.NextDouble() * (fw - 12), fy0 + fh * 0.4, 2.2, 10 + r.Next(0, 6), 1.1, ShLin(0, 0, 0, 1, 0.0, "#B0452E1A", 1.0, "#00452E1A")));
        return c;
    }
}
