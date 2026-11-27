// NJ-Flush: the mirror scene's private drawing kit.
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
    // private drawing kit for the mirror (nested, so its names never meet another scene's helpers)
    static class MK {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public static string F(double d) { return d.ToString("0.##", Inv); }
        public static T Fz<T>(T f) where T : W.Freezable { f.Freeze(); return f; }
        public static WM.BitmapCache Cache() { return new WM.BitmapCache(1.6); }
        // stops are "#AARRGGBB|offset" or plain hex (then spread evenly)
        static void AddStops(WM.GradientBrush g, string[] s) {
            for (int i = 0; i < s.Length; i++) {
                string hex = s[i]; double off = s.Length == 1 ? 0 : i / (double)(s.Length - 1);
                int p = hex.IndexOf('|');
                if (p >= 0) { off = double.Parse(hex.Substring(p + 1), Inv); hex = hex.Substring(0, p); }
                g.GradientStops.Add(new WM.GradientStop(C(hex), off));
            }
        }
        public static WM.LinearGradientBrush LG(double x1, double y1, double x2, double y2, params string[] s) {
            var g = new WM.LinearGradientBrush { StartPoint = new W.Point(x1, y1), EndPoint = new W.Point(x2, y2) }; AddStops(g, s); g.Freeze(); return g;
        }
        public static WM.RadialGradientBrush RG(double ox, double oy, params string[] s) {
            var g = new WM.RadialGradientBrush { GradientOrigin = new W.Point(ox, oy) }; AddStops(g, s); g.Freeze(); return g;
        }
        public static WS.Rectangle Rc(double w, double h, double r, WM.Brush f) { return new WS.Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = f }; }
        public static WS.Ellipse El(double w, double h, WM.Brush f) { return new WS.Ellipse { Width = w, Height = h, Fill = f }; }
        public static WS.Path Pa(string d, WM.Brush f) { return new WS.Path { Data = Fz(WM.Geometry.Parse(d)), Fill = f }; }
        public static double Ease(double s) { s = Math.Max(0, Math.Min(1, s)); return s < 0.5 ? 2 * s * s : 1 - 2 * (1 - s) * (1 - s); }
        // a branching frost line, appended to one path string
        public static void Fern(System.Text.StringBuilder sb, double x, double y, double ang, double len, int depth) {
            double x2 = x + Math.Cos(ang) * len, y2 = y + Math.Sin(ang) * len;
            sb.Append("M").Append(F(x)).Append(",").Append(F(y)).Append(" L").Append(F(x2)).Append(",").Append(F(y2)).Append(" ");
            if (depth <= 0) return;
            foreach (double t in new[] { 0.4, 0.72 }) {
                double bx = x + (x2 - x) * t, by = y + (y2 - y) * t;
                Fern(sb, bx, by, ang + 0.85, len * 0.42, depth - 1);
                Fern(sb, bx, by, ang - 0.85, len * 0.42, depth - 1);
            }
        }
        // discrete (flicker) keyframes, repeated for repeatMs; the last pair closes the period
        public static WA.DoubleAnimationUsingKeyFrames Disc(int repeatMs, params double[] tv) {
            var k = new WA.DoubleAnimationUsingKeyFrames { RepeatBehavior = new WA.RepeatBehavior(TimeSpan.FromMilliseconds(repeatMs)) };
            for (int i = 0; i < tv.Length; i += 2) k.KeyFrames.Add(new WA.DiscreteDoubleKeyFrame(tv[i + 1], WA.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(tv[i]))));
            return k;
        }
    }
}
