// NJ-Flush: the toilet scene's timing, motion helpers and the swirl, splash and set builders.
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
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

static partial class Overlay {
    // Nader 2026-11-23: "it took time" -> every duration and cue runs at 60% of the designed length (make_sfx.py SPEED matches)
    public const double Speed = 0.6;
    static TimeSpan Ms(double ms) { return TimeSpan.FromMilliseconds(ms * Speed); }
    static WA.DoubleAnimation A(double? from, double to, int ms, WA.IEasingFunction e) {
        var a = new WA.DoubleAnimation { To = to, Duration = Ms(ms), EasingFunction = e };
        if (from.HasValue) a.From = from.Value;
        return a;
    }
    // keyframes as (ms, value) pairs, eased in-out
    static WA.DoubleAnimationUsingKeyFrames K(params double[] tv) {
        var k = new WA.DoubleAnimationUsingKeyFrames();
        for (int i = 0; i < tv.Length; i += 2) k.KeyFrames.Add(new WA.EasingDoubleKeyFrame(tv[i + 1], WA.KeyTime.FromTimeSpan(Ms(tv[i])), QInOut));
        return k;
    }
    static WA.DoubleAnimationUsingKeyFrames KD(double delayMs, params double[] tv) { var k = K(tv); k.BeginTime = Ms(delayMs); return k; }
    static void S2(WM.ScaleTransform s, WA.AnimationTimeline a) { s.BeginAnimation(WM.ScaleTransform.ScaleXProperty, a); s.BeginAnimation(WM.ScaleTransform.ScaleYProperty, a); }
    static WM.Color C(string hex) { return (WM.Color)WM.ColorConverter.ConvertFromString(hex); }
    static WM.Color Alpha(string hex, byte a) { var c = C(hex); c.A = a; return c; }
    static WM.SolidColorBrush Brs(string hex, byte a) { return new WM.SolidColorBrush(Alpha(hex, a)); }
    static void Tint(WM.GradientStop s, string hex, int ms) { s.BeginAnimation(WM.GradientStop.ColorProperty, new WA.ColorAnimation(C(hex), Ms(ms))); }
    static void Shake(WM.TranslateTransform t, double amp, int times) {
        t.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-amp, amp, Ms(45)) {
            AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(times), FillBehavior = WA.FillBehavior.Stop });
    }
    static WM.Brush Grad(W.Point a, W.Point b, params WM.Color[] cs) {
        var g = new WM.LinearGradientBrush { StartPoint = a, EndPoint = b };
        for (int i = 0; i < cs.Length; i++) g.GradientStops.Add(new WM.GradientStop(cs[i], i / (double)(cs.Length - 1)));
        return g;
    }
    static string F(double d) { return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
    static T At<T>(T e, double x, double y) where T : W.UIElement { WC.Canvas.SetLeft(e, x); WC.Canvas.SetTop(e, y); return e; }
    static WS.Path Pth(string d, WM.Brush fill) { return new WS.Path { Data = WM.Geometry.Parse(d), Fill = fill }; }
    static WS.Path Ln(string d, WM.Brush stroke, double th) {
        return new WS.Path { Data = WM.Geometry.Parse(d), Stroke = stroke, StrokeThickness = th, StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round };
    }
    static WM.RadialGradientBrush Rad(double ox, double oy, params object[] st) {
        var g = new WM.RadialGradientBrush { GradientOrigin = new W.Point(ox, oy) };
        for (int i = 0; i + 1 < st.Length; i += 2) g.GradientStops.Add(new WM.GradientStop(C((string)st[i + 1]), Convert.ToDouble(st[i])));
        return g;
    }

    // tapered swirl arms (filled crescents, bright at the centre, fading out) and three fine spiral threads between them
    static void AddArms(WC.Canvas swirl) {
        foreach (int i in Enumerable.Range(0, 8)) {
            bool big = i % 2 == 0;
            swirl.Children.Add(new WS.Path {
                Data = WM.Geometry.Parse(big ? "M86,108 C88,76 118,52 162,60 C122,64 98,82 86,108 Z" : "M86,108 C88,92 104,80 126,82 C106,87 94,96 86,108 Z"),
                Fill = Grad(new W.Point(0, 1), new W.Point(1, 0), Alpha("#FFFFFF", (byte)(big ? 235 : 190)), Alpha("#E0FBFF", 20)),
                RenderTransform = new WM.RotateTransform(i * 45, 86, 108)
            });
        }
        for (int a = 0; a < 3; a++) {
            var sb = new StringBuilder();
            for (int i = 0; i <= 28; i++) {
                double ang = i * 0.3 + a * 2.094, r = 8 + i * 3.1;
                sb.Append(i == 0 ? "M" : " L").Append(F(86 + Math.Cos(ang) * r)).Append(",").Append(F(108 + Math.Sin(ang) * r * 1.1));
            }
            swirl.Children.Add(Ln(sb.ToString(), Brs("#E0F7FF", 150), 1.8));
        }
    }

    // the wall tiles (subway bond), dado diamonds and floor lines, drawn once into cached groups
    static void BuildSet(Func<string, object> n) {
        var tiles = (WC.Canvas)n("Tiles"); var rnd = new Random(11);
        var grout = new StringBuilder(); var hi = new StringBuilder(); var gloss = new StringBuilder(); var lite = new StringBuilder(); var dark = new StringBuilder();
        const double tw = 53, th = 31;
        for (int r = 0; r < 12; r++) {
            double y = 8 + r * th, off = (r % 2) * tw / 2;
            if (r > 0) { grout.Append("M8," + F(y) + " L432," + F(y) + " "); hi.Append("M8," + F(y + 1.3) + " L432," + F(y + 1.3) + " "); }
            for (int k = 0; k < 9; k++) {
                double x = 8 + off + k * tw;
                if (x <= 10 || x >= 430) continue;
                grout.Append("M" + F(x) + "," + F(y) + " L" + F(x) + "," + F(y + th) + " "); hi.Append("M" + F(x + 1.3) + "," + F(y + 1.3) + " L" + F(x + 1.3) + "," + F(y + th) + " ");
            }
        }
        for (int k = 0; k < 44; k++) {
            int r = rnd.Next(0, 12); double off = (r % 2) * tw / 2, x = 8 + off + rnd.Next(-1, 8) * tw, y = 8 + r * th;
            if (x < 8 || x + tw > 433) continue;
            (rnd.Next(0, 2) == 0 ? lite : dark).Append("M" + F(x) + "," + F(y) + " h" + F(tw) + " v" + F(th) + " h" + F(-tw) + " Z ");
            if (k % 3 == 0) gloss.Append("M" + F(x + 8) + "," + F(y + th - 2) + " L" + F(x + 22) + "," + F(y + 3) + " L" + F(x + 31) + "," + F(y + 3) + " L" + F(x + 17) + "," + F(y + th - 2) + " Z ");
        }
        tiles.Children.Add(Pth(lite.ToString(), Brs("#FFFFFF", 12)));
        tiles.Children.Add(Pth(dark.ToString(), Brs("#000000", 16)));
        tiles.Children.Add(Pth(gloss.ToString(), Brs("#FFFFFF", 20)));
        tiles.Children.Add(Ln(hi.ToString(), Brs("#FFFFFF", 34), 1));
        tiles.Children.Add(Ln(grout.ToString(), Brs("#071420", 150), 1.4));

        var dado = (WC.Canvas)n("Dado"); var dia = new StringBuilder();
        for (int k = 0; k < 20; k++) { double x = 14 + k * 21.5; dia.Append("M" + F(x) + ",314.5 l5,-4.5 l5,4.5 l-5,4.5 Z "); }
        dado.Children.Add(Pth(dia.ToString(), Brs("#FFFFFF", 80)));

        var fl = (WC.Canvas)n("FloorLines"); var fsb = new StringBuilder("M8,404 L432,404 M8,419 L432,419 ");
        for (int k = -8; k <= 8; k++) fsb.Append("M" + F(220 + k * 30) + ",389 L" + F(220 + k * 72) + ",432 ");
        fl.Children.Add(Ln(fsb.ToString(), Brs("#06101C", 150), 1.2));
        fl.Children.Add(Ln(fsb.ToString().Replace("M8,404 L432,404 M8,419 L432,419 ", "M8,405.2 L432,405.2 M8,420.2 L432,420.2 "), Brs("#FFFFFF", 22), 1));
    }

    class Bub { public WC.Canvas Body; public WM.TranslateTransform T; public WM.ScaleTransform S; public WS.Ellipse Ring; public WM.ScaleTransform RS; public double X; }
}
