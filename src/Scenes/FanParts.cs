// NJ-Flush: the fan scene's helpers (easing, gradients, shapes, frost).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

static partial class Scenes {
    // ---------- private helpers of the fan scene ----------
    // a cubic easing p*t^3 + q*t^2 + r*t, used so a spin-up ends at the same speed the cruise starts with (no pop at the hand-over)
    class FanPoly : WA.EasingFunctionBase {
        double p, q, r;
        public FanPoly() { }
        public FanPoly(double p, double q, double r) { this.p = p; this.q = q; this.r = r; }
        protected override double EaseInCore(double t) { return p * t * t * t + q * t * t + r * t; }
        protected override W.Freezable CreateInstanceCore() { return new FanPoly(p, q, r); }
    }
    static FanPoly FanSpinIn(double s) { return new FanPoly(s - 2, 3 - s, 0); }          // starts at rest, ends with speed s x the average
    static FanPoly FanSpinOut(double s) { return new FanPoly(s - 2, 3 - 2 * s, s); }     // starts with speed s x the average, ends at rest

    static string FanF(double d) { return d.ToString("0.##", CultureInfo.InvariantCulture); }

    // a gradient stop is "#AARRGGBB" or "#AARRGGBB@offset"; without offsets the stops are spread evenly
    static void FanStops(WM.GradientBrush g, string[] stops) {
        for (int i = 0; i < stops.Length; i++) {
            string[] pr = stops[i].Split('@'); double off = pr.Length > 1 ? double.Parse(pr[1], CultureInfo.InvariantCulture) : (stops.Length == 1 ? 0 : i / (double)(stops.Length - 1));
            g.GradientStops.Add(new WM.GradientStop(C(pr[0]), off));
        }
    }
    static WM.LinearGradientBrush FanLin(double x1, double y1, double x2, double y2, params string[] stops) {
        var g = new WM.LinearGradientBrush { StartPoint = new W.Point(x1, y1), EndPoint = new W.Point(x2, y2) }; FanStops(g, stops); g.Freeze(); return g;
    }
    static WM.RadialGradientBrush FanRad(double ox, double oy, params string[] stops) {
        var g = new WM.RadialGradientBrush { GradientOrigin = new W.Point(ox, oy) }; FanStops(g, stops); g.Freeze(); return g;
    }
    static WM.Brush FanBr(string argb) { var b = new WM.SolidColorBrush(C(argb)); b.Freeze(); return b; }
    static WC.Canvas FanStatic(double scale) { return new WC.Canvas { CacheMode = new WM.BitmapCache(scale), IsHitTestVisible = false }; }
    static WS.Rectangle FanRect(double x, double y, double w, double h, double r, WM.Brush fill, WM.Brush stroke = null, double st = 0) {
        return At(new WS.Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill, Stroke = stroke, StrokeThickness = st, IsHitTestVisible = false }, x, y);
    }
    static WS.Ellipse FanOval(double x, double y, double w, double h, WM.Brush fill, WM.Brush stroke = null, double st = 0) {
        return At(new WS.Ellipse { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = st, IsHitTestVisible = false }, x, y);
    }
    static WS.Path FanPath(string data, WM.Brush fill, WM.Brush stroke = null, double st = 0) {
        var p = new WS.Path { Data = WM.Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = st, IsHitTestVisible = false };
        if (stroke != null) { p.StrokeStartLineCap = WM.PenLineCap.Round; p.StrokeEndLineCap = WM.PenLineCap.Round; p.StrokeLineJoin = WM.PenLineJoin.Round; }
        return p;
    }
    // soft contact shadow: stacked rounded rects, each a little bigger and faint
    static void FanSoftRect(WC.Canvas c, double x, double y, double w, double h, double r, int layers, double grow, byte alpha) {
        for (int k = 0; k < layers; k++) c.Children.Add(FanRect(x - k * grow, y - k * grow, w + 2 * k * grow, h + 2 * k * grow, r + k * grow, Br("#000000", alpha)));
    }
    // keys for a blink: hi, lo, hi, lo ... and a rest value at the end
    static double[] FanBlink(int times, int period, double hi, double lo, double rest) {
        var l = new List<double>();
        for (int i = 0; i < times; i++) { l.Add(i * period); l.Add(hi); l.Add(i * period + period / 2.0); l.Add(lo); }
        l.Add(times * period); l.Add(rest); return l.ToArray();
    }
    // frost crystals growing from a corner
    static string FanFrost(double ox, double oy, double dx, double dy) {
        var sb = new StringBuilder();
        sb.Append("M" + FanF(ox) + "," + FanF(oy) + " L" + FanF(ox + dx * 78) + "," + FanF(oy + dy * 78) + " ");
        foreach (double t in new[] { 14.0, 30, 46, 62 }) {
            double px = ox + dx * t, py = oy + dy * t, len = 24 - t / 4;
            sb.Append("M" + FanF(px) + "," + FanF(py) + " L" + FanF(px + dx * len) + "," + FanF(py) + " ");
            sb.Append("M" + FanF(px) + "," + FanF(py) + " L" + FanF(px) + "," + FanF(py + dy * len) + " ");
            sb.Append("M" + FanF(px + dx * len * 0.55) + "," + FanF(py - dy * 3) + " L" + FanF(px + dx * len * 0.55 + dx * 5) + "," + FanF(py - dy * 8) + " ");
        }
        return sb.ToString();
    }
}
