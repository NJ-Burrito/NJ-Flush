// NJ-Flush 1.3 scene: All, the shower. Beats and sound cues: design/1.3-scenes.md (cue times are fixed: the soundtrack is cut to them).
// All art is hand-authored vector drawing in code (no third-party assets, no raster images, no emoji): light from the top left,
// multi-stop porcelain/chrome gradients, soft contact shadows, glossy foam. Static groups are BitmapCache'd; only transforms and
// opacity animate; no blur or drop-shadow effects anywhere.
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
    // =====================================================================================================================
    // All: the shower stall. A tiled niche with a chrome pipe, a cross-handle valve and a rain-shower head; a porcelain shelf
    // holding the three parts (RAM stick, CPU, graphics card) on little trays; a floor drain. Under each part an enamel signal
    // plate with a lamp: amber and pulsing while the real flush runs, cyan with a check when that part is done.
    // =====================================================================================================================

    static Scene Shower(int v, Flush job, string measure) {
        var sc = new Scene { Width = 400, Height = 400, Accent = "#22D3EE", Len = AllLen[v], Sound = "scene-all-" + v + ".wav",
                             Pill = Tx.F("pill.all", Tx.Upper(Tx.SceneName("all", v)), measure),
                             Busy = Tx.T("busy.all." + v) };
        var rnd = new Random(5 + v);
        var art = new WC.Canvas();
        double[] pcx = { 120, 220, 320 };
        string[] parts = { "ram", "cpu", "gpu" }, labels = { "RAM", "CPU", "GPU" };
        // the niche: a soft shadow ring, the room (clipped to a rounded rectangle), a chrome-dark frame
        for (int i = 0; i < 4; i++) art.Children.Add(ShR(8 - i * 3, 12 - i * 3, 384 + i * 6, 384 + i * 6, 26 + i * 3, Br("#000000", (byte)(30 - i * 7))));
        var room = new WC.Canvas { Width = 400, Height = 400, Clip = new WM.RectangleGeometry(new W.Rect(8, 8, 384, 384), 26, 26) };
        art.Children.Add(room);

        // ---- wall: glossy tiles ----
        var wall = new WC.Canvas { Width = 400, Height = 400 };
        wall.Children.Add(ShR(8, 8, 384, 342, 0, ShLin(0, 0, 0.3, 1, 0.0, "#3E5B7B", 0.55, "#2B4059", 1.0, "#1A293C")));
        var grout = new System.Text.StringBuilder(); var hi = new System.Text.StringBuilder(); var gloss = new System.Text.StringBuilder();
        for (int k = 1; k < 8; k++) { double x = 8 + k * 48; grout.Append("M" + ShF(x) + ",8 L" + ShF(x) + ",348 "); hi.Append("M" + ShF(x + 1.2) + ",8 L" + ShF(x + 1.2) + ",348 "); }
        for (int k = 1; k < 10; k++) { double y = 8 + k * 34; grout.Append("M8," + ShF(y) + " L392," + ShF(y) + " "); hi.Append("M8," + ShF(y + 1.2) + " L392," + ShF(y + 1.2) + " "); }
        for (int k = 0; k < 14; k++) { double x = 8 + rnd.Next(0, 8) * 48, y = 8 + rnd.Next(0, 10) * 34; gloss.Append("M" + ShF(x + 8) + "," + ShF(y + 30) + " L" + ShF(x + 24) + "," + ShF(y + 4) + " L" + ShF(x + 33) + "," + ShF(y + 4) + " L" + ShF(x + 17) + "," + ShF(y + 30) + " Z "); }
        ShAdd(wall, ShP(gloss.ToString(), Br("#FFFFFF", 17)), ShS(hi.ToString(), Br("#FFFFFF", 30), 1), ShS(grout.ToString(), Br("#08111F", 170), 1.4),
                    ShO(-70, -110, 440, 340, ShRad(0.5, 0.5, 0.0, "#34FFFFFF", 1.0, "#00FFFFFF")),
                    ShR(8, 8, 384, 70, 0, ShLin(0, 0, 0, 1, 0.0, "#66000000", 1.0, "#00000000")),
                    ShR(8, 338, 384, 11, 0, ShLin(0, 0, 0, 1, 0.0, "#B0A5F3FC", 0.35, "#B022B5D3", 1.0, "#B00E5E78")));
        room.Children.Add(ShCache(wall));

        // ---- floor and drain ----
        var floor = new WC.Canvas { Width = 400, Height = 400 };
        floor.Children.Add(ShR(8, 349, 384, 43, 0, ShLin(0, 0, 0, 1, 0.0, "#2D4057", 1.0, "#0B1421")));
        var fl = new System.Text.StringBuilder("M8,364 L392,364 M8,379 L392,379 ");
        for (int k = -6; k <= 6; k++) fl.Append("M" + ShF(200 + k * 28) + ",349 L" + ShF(200 + k * 66) + ",392 ");
        ShAdd(floor, ShS(fl.ToString(), Br("#06101C", 160), 1.2), ShO(30, 352, 230, 34, ShRad(0.5, 0.5, 0.0, "#2AFFFFFF", 1.0, "#00FFFFFF")),
                     ShO(146, 352, 108, 40, ShShadow()),
                     ShO(158, 359, 84, 26, ShLin(0, 0, 0, 1, 0.0, "#F8FAFC", 0.5, "#94A3B8", 1.0, "#3B4A5E")),
                     ShO(165, 363, 70, 18, ShLin(0, 0, 0, 1, 0.0, "#050B14", 1.0, "#1E293B")));
        var slots = new System.Text.StringBuilder();
        for (int k = 0; k < 7; k++) slots.Append("M" + ShF(177 + k * 7.7) + ",366.5 L" + ShF(177 + k * 7.7) + ",377 ");
        ShAdd(floor, ShS(slots.ToString(), Br("#64748B"), 2.2));
        room.Children.Add(ShCache(floor));
        // the puddle on the floor (Rinse, Shower, Scrub): grows while the water runs, drains away at the end
        var puddleS = new WM.ScaleTransform(0, 0, 150, 14);
        var puddle = new WS.Ellipse { Width = 300, Height = 28, Opacity = 0.9, RenderTransform = puddleS, Fill = ShRad(0.4, 0.4, 0.0, "#7038BDF8", 0.7, "#3838BDF8", 1.0, "#0038BDF8") };
        room.Children.Add(At(puddle, 50, 354));

        // ---- steam (Shower, Scrub, Flood) ----
        WC.Canvas mist = null; var mistT = new WM.TranslateTransform();
        if (v >= 1) {
            mist = new WC.Canvas { Opacity = 0, RenderTransform = mistT };
            double[][] ms = { new double[] { 40, 36, 170, 90 }, new double[] { 170, 40, 190, 100 }, new double[] { 90, 120, 190, 80 }, new double[] { 240, 110, 150, 90 } };
            foreach (var m in ms) mist.Children.Add(ShO(m[0], m[1], m[2], m[3], ShRad(0.5, 0.5, 0.0, "#4CFFFFFF", 1.0, "#00FFFFFF")));
            mistT.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -9, TimeSpan.FromMilliseconds(1700)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            room.Children.Add(mist);
        }

        // ---- the pipe, the valve's escutcheon ----
        var pipe = new WC.Canvas { Width = 400, Height = 400 };
        ShAdd(pipe, ShO(14, 322, 40, 30, ShShadow()), ShR(28, 8, 12, 342, 0, ShChromeH()), ShR(34, 30, 172, 12, 0, ShLin(0, 0, 0, 1, 0.0, "#F8FAFC", 0.35, "#CBD5E1", 0.7, "#64748B", 1.0, "#94A3B8")),
                    ShO(22, 25, 24, 22, ShRad(0.35, 0.3, 0.0, "#FFFFFF", 0.5, "#CBD5E1", 1.0, "#64748B")),
                    ShR(24, 330, 20, 9, 3, ShChromeH()), ShR(25, 98, 18, 6, 2, ShChromeH()), ShR(25, 262, 18, 6, 2, ShChromeH()),
                    ShO(14, 170, 40, 40, ShLin(0, 0, 1, 1, 0.0, "#FFFFFF", 0.4, "#CBD5E1", 1.0, "#475569")),
                    ShO(18, 174, 32, 32, ShLin(1, 1, 0, 0, 0.0, "#64748B", 0.5, "#E2E8F0", 1.0, "#FFFFFF")));
        room.Children.Add(ShCache(pipe));
        var valve = new WC.Canvas(); var valveRot = new WM.RotateTransform(0); valve.RenderTransform = valveRot;
        ShAdd(valve, ShR(-27, -5, 54, 10, 5, ShLin(0, 0, 0, 1, 0.0, "#FFFFFF", 0.5, "#CBD5E1", 1.0, "#64748B")), ShR(-5, -27, 10, 54, 5, ShLin(0, 0, 1, 0, 0.0, "#FFFFFF", 0.5, "#CBD5E1", 1.0, "#64748B")),
                     ShO(-10, -10, 20, 20, ShRad(0.35, 0.3, 0.0, "#FECACA", 0.55, "#EF4444", 1.0, "#7F1D1D")), ShO(-6, -7, 7, 4, Br("#FFFFFF", 190)));
        room.Children.Add(At(valve, 34, 190));

        // ---- shelf, trays, the three parts ----
        var shelf = new WC.Canvas { Width = 400, Height = 400 };
        ShAdd(shelf, ShR(58, 296, 328, 24, 0, ShLin(0, 0, 0, 1, 0.0, "#66000000", 1.0, "#00000000")),
                     ShR(56, 286, 332, 14, 4, ShLin(0, 0, 0, 1, 0.0, "#FFFFFF", 0.3, "#E8EEF3", 1.0, "#9AA9B7")), ShR(60, 287, 324, 2, 1, Br("#FFFFFF")),
                     ShP("M62,299 L74,299 L74,314 Z", ShChromeV()), ShP("M382,299 L370,299 L370,314 Z", ShChromeV()));
        for (int i = 0; i < 3; i++) ShAdd(shelf, ShO(pcx[i] - 56, 281, 112, 14, ShShadow()),
            ShR(pcx[i] - 50, 279, 100, 8, 4, ShLin(0, 0, 0, 1, 0.0, "#FFFFFF", 0.4, "#E2E8F0", 1.0, "#94A3B8")), ShR(pcx[i] - 44, 280, 88, 1.4, 0.7, Br("#FFFFFF")));
        room.Children.Add(ShCache(shelf));
        var grime = new List<WC.Canvas>(); var wet = new List<WC.Canvas>();
        double[][] gbox = { new double[] { 2, 10, 96, 46 }, new double[] { 16, 0, 68, 60 }, new double[] { 8, 10, 90, 46 } };
        double grimeBase = v == 0 ? 0.55 : v == 1 ? 0.8 : 1;
        for (int i = 0; i < 3; i++) {
            var p = i == 0 ? ShRam() : i == 1 ? ShCpu() : ShGpu();
            room.Children.Add(At(ShCache(p), pcx[i] - 50, 214));
            var gr = ShGrime(rnd, gbox[i][0], gbox[i][1], gbox[i][2], gbox[i][3]); gr.Opacity = grimeBase;
            room.Children.Add(At(gr, pcx[i] - 50, 214)); grime.Add(gr);
            var w = new WC.Canvas { Width = 100, Height = 70, Opacity = 0 };
            for (int k = 0; k < 6; k++) { double d = 4 + rnd.Next(0, 3); w.Children.Add(ShO(8 + rnd.NextDouble() * 82, 14 + rnd.NextDouble() * 40, d, d * 1.3, ShRad(0.3, 0.25, 0.0, "#FFFFFFFF", 0.55, "#96BAE6FD", 1.0, "#5A38BDF8"))); }
            room.Children.Add(At(w, pcx[i] - 50, 214)); wet.Add(w);
        }

        // ---- the signal plates (honest progress): amber lamp pulsing = working, cyan check = done ----
        var marks = new List<ShMark>();
        for (int i = 0; i < 3; i++) {
            var m = new ShMark { Cx = pcx[i] };
            var pc = new WC.Canvas { Width = 88, Height = 26, RenderTransformOrigin = new W.Point(0.5, 0.5), RenderTransform = new WM.ScaleTransform(1, 1) };
            m.Pop = (WM.ScaleTransform)pc.RenderTransform;
            m.Glow = ShO(-26, -16, 140, 58, ShRad(0.5, 0.5, 0.0, "#A022D3EE", 1.0, "#0022D3EE")); m.Glow.Opacity = 0;
            m.Lit = ShR(2.2, 2.2, 83.6, 21.6, 11, ShLin(0, 0, 0, 1, 0.0, "#7BEFFF", 0.5, "#22D3EE", 1.0, "#0A93B3")); m.Lit.Opacity = 0;
            m.Lamp = ShO(8, 5, 16, 16, ShRad(0.35, 0.3, 0.0, "#FF55657A", 1.0, "#FF0B1220"));
            m.Amber = ShO(8, 5, 16, 16, ShRad(0.4, 0.35, 0.0, "#FFFFF1B0", 0.5, "#FFF59E0B", 1.0, "#FF92400E")); m.Amber.Opacity = 0;
            m.Check = ShS("M12,13.5 L15.2,16.6 L20.5,9.6", Br("#04323B"), 2.5); m.Check.Opacity = 0;
            m.Label = new WC.TextBlock { Text = labels[i], FontSize = 14, FontWeight = W.FontWeights.Bold, Foreground = Br("#8DA0B6"), FontFamily = new WM.FontFamily("Thmanyah Sans, Segoe UI") };
            ShAdd(pc, m.Glow, ShO(2, 14, 84, 16, ShShadow()), ShR(0, 0, 88, 26, 13, ShLin(0, 0, 0, 1, 0.0, "#F8FAFC", 0.45, "#94A3B8", 1.0, "#475569")),
                      ShR(2.2, 2.2, 83.6, 21.6, 11, ShLin(0, 0, 0, 1, 0.0, "#0A1424", 1.0, "#1C2D45")), m.Lit, ShR(10, 3.6, 70, 2.4, 1.2, Br("#FFFFFF", 38)),
                      ShO(5.5, 2.5, 21, 21, ShLin(0, 0, 0, 1, 0.0, "#F8FAFC", 1.0, "#475569")), m.Lamp, m.Amber, ShO(11, 7, 6, 3.5, Br("#FFFFFF", 150)), m.Check);
            pc.Children.Add(At(m.Label, 33, 2.5));
            m.Root = pc; room.Children.Add(At(pc, pcx[i] - 44, 308)); marks.Add(m);
        }

        // ---- the shower head (cached; it shudders a hair when the water starts) ----
        var headT = new WM.TranslateTransform();
        var head = new WC.Canvas { Width = 400, Height = 400, RenderTransform = headT };
        ShAdd(head, ShR(194, 42, 12, 20, 2, ShChromeH()), ShO(189, 53, 22, 17, ShRad(0.35, 0.3, 0.0, "#FFFFFF", 0.5, "#CBD5E1", 1.0, "#64748B")),
                    ShP("M140,84 C140,68 168,58 200,58 C232,58 260,68 260,84 Z", ShChromeH()), ShR(139, 80, 122, 6, 3, ShChromeH()),
                    ShO(140, 79, 120, 14, ShLin(0, 0, 0, 1, 0.0, "#A3B1C2", 1.0, "#1E293B")));
        for (int i = 0; i < 11; i++) head.Children.Add(ShO(149.5 + i * 9.6, 83, 5, 3.6, Br("#0B1220")));
        ShAdd(head, ShS("M152,72 C162,64 182,60 198,60", Br("#FFFFFF", 220), 2.6), ShO(150, 74, 4, 3, Br("#FFFFFF", 230)));
        room.Children.Add(ShCache(head));

        // ---- the water: a faint cone, flowing streams (two layers each), falling drops, splash hops ----
        var sprayG = new WC.Canvas { Opacity = 0 };
        int ns = v == 0 ? 7 : 11; double thick = v == 0 ? 0.8 : v == 3 ? 1.25 : 1;
        var cone = ShP("M148,88 L252,88 L374,246 L74,246 Z", ShLin(0, 0, 0, 1, 0.0, "#5538BDF8", 0.6, "#2638BDF8", 1.0, "#5A7DD3FC")); cone.Opacity = v == 0 ? 0.6 : 1;
        sprayG.Children.Add(cone);
        for (int i = 0; i < ns; i++) {
            double t = i / (double)(ns - 1), x0 = 152 + t * 96, x1 = (v == 0 ? 92 : 80) + t * (v == 0 ? 268 : 290), xm = x0 + (x1 - x0) * 0.62, ym = 88 + 150 * 0.4;
            string d = "M" + ShF(x0) + ",88 Q" + ShF(xm) + "," + ShF(ym) + " " + ShF(x1) + ",244";
            var wide = ShS(d, Br("#38BDF8", 95), 3.4 * thick); wide.StrokeDashArray = new WM.DoubleCollection { 9, 1.2 }; wide.StrokeLineJoin = WM.PenLineJoin.Round;
            wide.StrokeStartLineCap = WM.PenLineCap.Flat; wide.StrokeEndLineCap = WM.PenLineCap.Flat;
            var thin = ShS(d, Br("#FFFFFF", 235), 1.3 * thick); thin.StrokeDashArray = new WM.DoubleCollection { 5, 2.6 };
            thin.StrokeStartLineCap = WM.PenLineCap.Flat; thin.StrokeEndLineCap = WM.PenLineCap.Flat;
            wide.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, new WA.DoubleAnimation(0, -10.2, TimeSpan.FromMilliseconds(190 + rnd.Next(0, 30))) { RepeatBehavior = WA.RepeatBehavior.Forever });
            thin.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, new WA.DoubleAnimation(0, -15.2, TimeSpan.FromMilliseconds(200 + rnd.Next(0, 60))) { RepeatBehavior = WA.RepeatBehavior.Forever });
            sprayG.Children.Add(wide); sprayG.Children.Add(thin);
        }
        for (int i = 0; i < 9; i++) {
            var tt = new WM.TranslateTransform(); double sx = 150 + rnd.NextDouble() * 100, ex = 80 + rnd.NextDouble() * 290; int ms = 480 + rnd.Next(0, 160);
            var dr = new WS.Ellipse { Width = 3, Height = 5.5, RenderTransform = tt, Fill = ShRad(0.35, 0.3, 0.0, "#FFFFFFFF", 1.0, "#D038BDF8") };
            tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, 150, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)), EasingFunction = QIn });
            tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(0, ex - sx, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)) });
            sprayG.Children.Add(At(dr, sx, 92));
        }
        for (int i = 0; i < (v == 0 ? 7 : 11); i++) {
            var tt = new WM.TranslateTransform(); double hx = 90 + rnd.NextDouble() * 270, dir = rnd.NextDouble() < 0.5 ? -1 : 1; int ms = 330 + rnd.Next(0, 120);
            var hop = new WS.Ellipse { Width = 3.6, Height = 3.6, RenderTransform = tt, Fill = Br("#E8F8FF", 235) };
            var ky = new WA.DoubleAnimationUsingKeyFrames { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)) };
            ky.KeyFrames.Add(new WA.EasingDoubleKeyFrame(-15 - rnd.Next(0, 8), ShKT(ms * 0.42), QOut)); ky.KeyFrames.Add(new WA.EasingDoubleKeyFrame(3, ShKT(ms), QIn));
            tt.BeginAnimation(WM.TranslateTransform.YProperty, ky);
            tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(0, dir * (8 + rnd.Next(0, 10)), TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)) });
            sprayG.Children.Add(At(hop, hx, 236));
        }
        room.Children.Add(sprayG);
        // run-off from the shelf to the floor
        var runoff = new WC.Canvas { Opacity = 0 };
        foreach (double rx in new[] { 70.0, 171, 270, 373 }) {
            var a = new WS.Line { X1 = rx, Y1 = 300, X2 = rx, Y2 = 349, Stroke = Br("#38BDF8", 130), StrokeThickness = 3.4 * thick, StrokeDashArray = new WM.DoubleCollection { 2.2, 1.8 } };
            var b = new WS.Line { X1 = rx - 0.4, Y1 = 300, X2 = rx - 0.4, Y2 = 349, Stroke = Br("#FFFFFF", 230), StrokeThickness = 1.2 * thick, StrokeDashArray = new WM.DoubleCollection { 6, 4 } };
            a.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, new WA.DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(170)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            b.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, new WA.DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(250)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            runoff.Children.Add(a); runoff.Children.Add(b);
            var rt = new WM.ScaleTransform(0.3, 0.3, 14, 5);
            var rg = new WS.Ellipse { Width = 28, Height = 9.5, Stroke = Br("#E0F7FF", 200), StrokeThickness = 1.3, RenderTransform = rt };
            rt.BeginAnimation(WM.ScaleTransform.ScaleXProperty, new WA.DoubleAnimation(0.3, 1.2, TimeSpan.FromMilliseconds(420)) { RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QOut, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)) });
            rt.BeginAnimation(WM.ScaleTransform.ScaleYProperty, new WA.DoubleAnimation(0.3, 1.2, TimeSpan.FromMilliseconds(420)) { RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QOut, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)) });
            rg.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(420)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            runoff.Children.Add(At(rg, rx - 14, 349));
        }
        room.Children.Add(runoff);

        // ---- foam: glossy bubbles on each part ----
        var foam = new List<List<ShBub>>(); int fcount = v == 2 ? 10 : 9;
        for (int i = 0; i < 3; i++) {
            var fl2 = new List<ShBub>();
            for (int k = 0; k < fcount; k++) {
                var b = ShBubble(13 + rnd.Next(0, 13));
                room.Children.Add(At(b.C, pcx[i] - 50 + 4 + rnd.NextDouble() * (88 - b.D), 214 + 6 + rnd.NextDouble() * (52 - b.D)));
                fl2.Add(b);
            }
            foam.Add(fl2);
        }

        // ---- the effect layer: drops, ripples, wash, gleam ----
        var fx = new WC.Canvas();
        var sweepC = new WC.Canvas { Width = 400, Height = 400, Clip = new WM.RectangleGeometry(new W.Rect(56, 208, 332, 92)), IsHitTestVisible = false };
        var sweepT = new WM.TranslateTransform(30, 0);
        sweepC.Children.Add(At(new WS.Path { Data = WM.Geometry.Parse("M0,0 L34,0 L10,92 L-24,92 Z"), Fill = ShLin(0, 0, 1, 0, 0.0, "#00FFFFFF", 0.5, "#C0FFFFFF", 1.0, "#00FFFFFF"), RenderTransform = sweepT }, 0, 208));

        Action<bool> spray = on => {
            sprayG.BeginAnimation(W.UIElement.OpacityProperty, A(null, on ? 1 : 0, on ? 200 : 230, QInOut));
            Fade(runoff, on ? 1 : 0, on ? 260 : 320, on ? 170 : 0);
            foreach (var w in wet) Fade(w, on ? 1 : 0, on ? 450 : 1400, on ? 160 : 120);
            if (mist != null) Fade(mist, on ? 1 : 0, on ? 600 : 1300);
            if (v < 3) S2(puddleS, A(null, on ? 1 : puddleS.ScaleX, 1500, QOut));
            if (on) Shake(headT, 1.0, 5);
        };
        Action turnOn = () => valveRot.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, valveRot.Angle, 90, valveRot.Angle - 10, 360, valveRot.Angle + 90));
        Action turnOff = () => valveRot.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, valveRot.Angle, 90, valveRot.Angle + 8, 300, 0));
        Action<int, int> foamPart = (pi, gap) => { int k = 0; foreach (var b in foam[pi]) S2(b.S, A(0, 1, 380, Back, (k++) * gap)); };
        Action<int> foamUp = gap => { for (int i = 0; i < 3; i++) foamPart(i, gap); };
        Action foamOff = () => {   // the foam pops and slides down the parts
            int k = 0;
            foreach (var fl3 in foam) foreach (var b in fl3) {
                int dl = (k++ % 9) * 24 + rnd.Next(0, 40);
                S2(b.S, K(0, b.S.ScaleX, dl + 70, Math.Max(1.15, b.S.ScaleX * 1.15), dl + 300, 0));
                b.T.BeginAnimation(WM.TranslateTransform.YProperty, A(null, 22 + rnd.Next(0, 18), 300, QIn, dl));
            }
        };
        Action<int, int> washGrime = (ms, stagger) => { for (int i = 0; i < 3; i++) Fade(grime[i], 0, ms, i * stagger); };
        Action gleam = () => {
            sweepT.BeginAnimation(WM.TranslateTransform.XProperty, A(30, 400, 750, QInOut));
            for (int i = 0; i < 3; i++) { var s = Star(11, "#F0F9FF"); room.Children.Add(At(s, pcx[i] + 36, 230)); Glint(s, i * 150 + 80); }
        };
        // the drain: the water swirls away (a flattened spiral) with a few gurgle bubbles
        var swirl = new WC.Canvas { Opacity = 0 };
        for (int a = 0; a < 3; a++) swirl.Children.Add(ShS(ShSpiral(a * 2.094), Br("#E0F7FF", 190), 2.4));
        var swRot = new WM.RotateTransform(0); var swTg = new WM.TransformGroup(); swTg.Children.Add(swRot); swTg.Children.Add(new WM.ScaleTransform(1, 0.28)); swirl.RenderTransform = swTg;
        swRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(650)) { RepeatBehavior = WA.RepeatBehavior.Forever });
        Action<int> gurgle = ms => {
            Fade(swirl, 1, 120); Fade(swirl, 0, 260, ms - 260 + 120 > 0 ? ms - 260 : 0);
            S2(puddleS, A(null, 0, ms, CIn));
            for (int i = 0; i < 3; i++) {
                var b = ShBubble(9 + i * 3); room.Children.Add(At(b.C, 188 + i * 12, 366)); double dl = 120 + i * 190;
                S2(b.S, K(0, 0, dl, 0, dl + 160, 1, dl + 330, 1.25, dl + 400, 0));
                b.T.BeginAnimation(WM.TranslateTransform.YProperty, A(0, -22, 400, QOut, (int)dl));
            }
        };

        if (v == 0) {          // Rinse: a brisk burst of thin spray, two drips from the nozzle
            sc.At(300, turnOn); sc.At(600, () => spray(true));
            sc.At(900, () => washGrime(650, 90));
            sc.At(1300, () => spray(false));
            sc.At(1380, () => ShDrip(fx, 200, 90, 130, 220));
            sc.At(1600, () => { ShBurst(fx, 200, 226, 5, 12, 13, 260, 0, rnd); ShRipple(fx, 200, 226, 26, 0); });
            sc.At(1590, () => ShDrip(fx, 214, 90, 134, 260));
            sc.At(1850, () => { ShBurst(fx, 214, 226, 5, 12, 12, 260, 0, rnd); ShRipple(fx, 214, 226, 24, 0); });
            sc.At(1550, turnOff); sc.At(2000, gleam);
        } else if (v == 1) {   // Shower: full spray, soap foam, rinse, drain gurgle
            sc.At(300, turnOn); sc.At(550, () => spray(true));
            sc.At(900, () => foamUp(26)); sc.At(1800, foamOff); sc.At(1800, () => washGrime(800, 120));
            sc.At(2550, () => spray(false)); sc.At(2600, turnOff); sc.At(2700, () => gurgle(1000)); sc.At(3100, gleam);
        } else if (v == 2) {   // Scrub: grimy parts, the brush scrubs each, foam, rinse
            var brush = new WC.Canvas { Opacity = 0 }; var brot = new WM.RotateTransform(0); var btr = new WM.TranslateTransform(pcx[0] + 70, 170);
            var btg = new WM.TransformGroup(); btg.Children.Add(brot); btg.Children.Add(btr); brush.RenderTransform = btg;
            var handle = ShR(-6, -86, 12, 66, 6, ShLin(0, 0, 1, 0, 0.0, "#C98B3F", 0.35, "#F2C27D", 1.0, "#7C4A1E")); handle.RenderTransform = new WM.RotateTransform(16, 6, 66);
            ShAdd(brush, ShO(-26, 2, 52, 9, ShShadow()), handle, ShR(-31, -24, 62, 17, 8, ShLin(0, 0, 0, 1, 0.0, "#FDBA74", 0.45, "#D9772B", 1.0, "#7C2D12")), ShR(-24, -22, 48, 2.2, 1, Br("#FFFFFF", 150)));
            for (int k = 0; k < 11; k++) brush.Children.Add(ShR(-28 + k * 5.3, -8, 3.8, 11 + (k % 3), 1.5, ShLin(0, 0, 0, 1, 0.0, "#FFF7DA", 0.6, "#E9C98A", 1.0, "#A8793A")));
            room.Children.Add(brush);
            sc.At(300, turnOn); sc.At(550, () => spray(true));
            for (int i = 0; i < 3; i++) {
                int ii = i, t0 = 1000 + i * 700; double bx = pcx[i], by = 262;
                sc.At(t0 - 260, () => {
                    Fade(brush, 1, 140);
                    btr.BeginAnimation(WM.TranslateTransform.XProperty, A(null, bx, 260, COut));
                    btr.BeginAnimation(WM.TranslateTransform.YProperty, K(0, btr.Y, 90, by - 34, 260, by - 6));
                    brot.BeginAnimation(WM.RotateTransform.AngleProperty, A(null, 0, 260, QInOut));
                });
                sc.At(t0, () => {
                    btr.BeginAnimation(WM.TranslateTransform.XProperty, K(0, bx, 75, bx - 17, 150, bx + 17, 225, bx - 17, 300, bx + 17, 375, bx - 17, 450, bx + 17, 525, bx - 12, 600, bx));
                    btr.BeginAnimation(WM.TranslateTransform.YProperty, K(0, by - 6, 60, by, 540, by, 600, by - 8));
                    brot.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 75, -5, 150, 5, 225, -5, 300, 5, 375, -5, 450, 5, 525, -3, 600, 0));
                    Fade(grime[ii], 0, 600); foamPart(ii, 40);
                });
            }
            sc.At(3050, () => { Fade(brush, 0, 150); btr.BeginAnimation(WM.TranslateTransform.YProperty, A(null, 190, 220, CIn)); });
            sc.At(3200, foamOff);
            sc.At(3750, () => spray(false)); sc.At(3800, turnOff); sc.At(3900, () => gurgle(1000)); sc.At(4500, gleam);
        } else {               // Flood: the water fills the stall (the drain is clogged), the plunger frees it
            var water = new WC.Canvas { Width = 400, Height = 70 };
            var fs = new WM.ScaleTransform(1, 0, 0, 70); water.RenderTransform = fs;
            var crest = new System.Text.StringBuilder("M0,0 Q25,-8 50,0");
            for (int k = 1; k < 12; k++) crest.Append(" T" + (50 + k * 50) + ",0");
            string cs = crest.ToString();
            var back = ShP(cs + " L600,14 L0,14 Z", Br("#7A67E8F9", 125)); var bT = new WM.TranslateTransform(-100, 0); back.RenderTransform = bT;
            bT.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(0, -100, TimeSpan.FromMilliseconds(2100)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            var frontW = ShP(cs + " L600,16 L0,16 Z", ShLin(0, 0, 0, 1, 0.0, "#B8A5F3FC", 1.0, "#A022B5D3")); var fT = new WM.TranslateTransform(0, 0); frontW.RenderTransform = fT;
            fT.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-100, 0, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            var wline = ShS(cs, Br("#FFFFFF", 190), 1.4); var lT = new WM.TranslateTransform(0, 0); wline.RenderTransform = lT;
            lT.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-100, 0, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = WA.RepeatBehavior.Forever });
            ShAdd(water, At(back, 8, 0), ShR(8, 8, 384, 62, 0, ShLin(0, 0, 0, 1, 0.0, "#A04FD8EE", 0.5, "#B31E9BC9", 1.0, "#CC0A4F78")), At(frontW, 8, 2), At(wline, 8, 2),
                         ShR(8, 10, 384, 9, 0, ShLin(0, 0, 0, 1, 0.0, "#5AFFFFFF", 1.0, "#00FFFFFF")));
            // the clog, then the plunger
            var gunk = new WC.Canvas();
            ShAdd(gunk, ShO(184, 366, 34, 16, ShRad(0.4, 0.3, 0.0, "#5A4630", 1.0, "#241810")), ShO(196, 362, 26, 14, ShRad(0.4, 0.3, 0.0, "#6B5440", 1.0, "#2B1D12")), ShO(176, 371, 20, 10, ShRad(0.4, 0.3, 0.0, "#4A3826", 1.0, "#1C1209")),
                        ShS("M190,370 C186,362 182,360 176,362 M204,366 C210,358 214,358 220,360 M198,364 C198,356 200,352 204,350", Br("#2B1D12"), 1.5));
            room.Children.Add(gunk);
            // a short handle (owner review: a long one crossed the CPU and the shower head)
            var pl = new WC.Canvas(); var py = new WM.TranslateTransform(0, -420); pl.RenderTransform = py;
            pl.Children.Add(ShR(-5, -96, 10, 62, 0, ShLin(0, 0, 1, 0, 0.0, "#7A4B20", 0.35, "#E7B36B", 0.62, "#C98B3F", 1.0, "#5E3416")));
            pl.Children.Add(ShR(-8, -46, 16, 12, 3, ShChromeH()));
            var cup = new WC.Canvas(); var cupS = new WM.ScaleTransform(1, 1, 0, 0); cup.RenderTransform = cupS;
            ShAdd(cup, ShO(-34, -6, 68, 12, ShLin(0, 0, 0, 1, 0.0, "#DC2626", 1.0, "#450A0A")),
                       ShP("M-31,-2 C-33,-24 -18,-39 0,-39 C18,-39 33,-24 31,-2 Z", ShRad(0.3, 0.28, 0.0, "#FCA5A5", 0.45, "#E11D2E", 1.0, "#7F1D1D")),
                       ShO(-21, -31, 15, 6, Br("#FFFFFF", 150)), ShR(-30, -8, 60, 5, 2.5, ShLin(0, 0, 0, 1, 0.0, "#EF4444", 1.0, "#7F1D1D")));
            pl.Children.Add(cup);
            room.Children.Add(At(pl, 200, 372));
            room.Children.Add(At(water, 0, 322));
            // bubbles rising through the filling water
            var fb = new WC.Canvas { Opacity = 0 };
            for (int i = 0; i < 11; i++) {
                var b = ShBubble(7 + rnd.Next(0, 9)); b.S.ScaleX = 1; b.S.ScaleY = 1;
                var bt2 = new WM.TranslateTransform(); b.C.RenderTransform = bt2;
                bt2.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -(30 + rnd.Next(0, 24)), TimeSpan.FromMilliseconds(700 + rnd.Next(0, 500))) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 600)), EasingFunction = QOut });
                b.C.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 600)) });
                fb.Children.Add(At(b.C, 24 + rnd.NextDouble() * 350, 346 + rnd.NextDouble() * 34));
            }
            room.Children.Add(fb);
            // the geyser when the clog lets go
            var gey = new WC.Canvas { Opacity = 0 }; var geyS = new WM.ScaleTransform(1, 0, 0, 0); gey.RenderTransform = geyS;
            gey.Children.Add(ShP("M-16,0 C-18,-40 -9,-90 -5,-130 L5,-130 C9,-90 18,-40 16,0 Z", ShLin(0, 0, 1, 0, 0.0, "#A038BDF8", 0.45, "#F0FFFFFF", 1.0, "#A038BDF8")));
            room.Children.Add(At(gey, 200, 372));
            var wash = new WC.Canvas { Opacity = 0, Width = 400, Height = 90 }; var washT = new WM.TranslateTransform(0, 0); wash.RenderTransform = washT;
            ShAdd(wash, ShR(8, 0, 384, 90, 0, ShLin(0, 0, 0, 1, 0.0, "#00A5F3FC", 0.12, "#C0E0F7FF", 0.3, "#7A38BDF8", 1.0, "#2238BDF8")));
            room.Children.Add(At(wash, 0, 330));

            sc.At(200, turnOn); sc.At(450, () => spray(true));
            sc.At(700, () => { fs.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(0, 1, 1700, QOut)); Fade(fb, 1, 250); });
            sc.At(2450, () => { spray(false); Fade(fb, 0, 500); }); sc.At(2500, turnOff);
            sc.At(2700, () => py.BeginAnimation(WM.TranslateTransform.YProperty, A(-420, 0, 450, Back)));
            foreach (int t in new[] { 3200, 3700, 4200 }) {
                sc.At(t, () => {   // wind-up, squash on the plunge (the squish sounds 240 ms in), spring back
                    py.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 90, -12, 240, 18, 440, 0));
                    cupS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 90, 1.05, 240, 0.55, 440, 1));
                    cupS.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 90, 0.95, 240, 1.25, 440, 1));
                    ShBurst(fx, 200, 330, 7, 40, 30, 520, 230, rnd); ShRipple(fx, 200, 326, 70, 230);
                });
            }
            sc.At(4700, () => {
                py.BeginAnimation(WM.TranslateTransform.YProperty, A(null, -420, 480, CIn, 90));
                fs.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(null, 0, 1000, CIn));
                Fade(gunk, 0, 200);
                geyS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(0, 1, 220, QOut));
                gey.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, 280, 1, 620, 0));
                ShBurst(fx, 200, 330, 16, 150, 120, 800, 40, rnd);
                washT.BeginAnimation(WM.TranslateTransform.YProperty, A(0, -170, 700, QOut));
                wash.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, 420, 1, 760, 0));
                Fade(swirl, 1, 140); Fade(swirl, 0, 300, 900);
                washGrime(600, 70);
                for (int i = 0; i < 3; i++) Fade(wet[i], 1, 200);
            });
            sc.At(5800, gleam);
        }
        room.Children.Add(At(swirl, 200, 372));
        room.Children.Add(fx);
        room.Children.Add(sweepC);
        // the frame on top of the clipped room
        var frame = new WS.Rectangle { Width = 384, Height = 384, RadiusX = 26, RadiusY = 26, StrokeThickness = 3.5, Stroke = ShLin(0, 0, 1, 1, 0.0, "#E2E8F0", 0.4, "#64748B", 1.0, "#1E293B") };
        art.Children.Add(At(frame, 8, 8));
        art.Children.Add(At(new WS.Rectangle { Width = 378, Height = 378, RadiusX = 23, RadiusY = 23, StrokeThickness = 1, Stroke = Br("#FFFFFF", 50) }, 11, 11));

        // ---- the idle: each plate lights when its part is really done (a preview lights on a clock), 350 ms apart at least ----
        var lit = new HashSet<string>(); long last = -1000, holdStart = -1;
        Action<ShMark> lightUp = m => {
            m.Amber.BeginAnimation(W.UIElement.OpacityProperty, null); m.Amber.Opacity = 0;
            m.Lit.BeginAnimation(W.UIElement.OpacityProperty, A(null, 1, 140, QOut));
            m.Lamp.Fill = ShRad(0.35, 0.3, 0.0, "#FFFFFFFF", 1.0, "#FFA5F3FC");
            m.Check.Opacity = 1; m.Label.Foreground = Br("#04323B");
            m.Glow.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 140, 1, 600, 0.55));
            S2(m.Pop, K(0, 1, 120, 1.16, 320, 1));
            var s = Star(9, "#FFFFFF"); room.Children.Add(At(s, m.Cx - 36, 320)); Glint(s, 60);
        };
        sc.Hold = t => {
            if (holdStart < 0) {
                holdStart = t;
                for (int i = 0; i < 3; i++) marks[i].Amber.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.12, 0.95, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 160), EasingFunction = QInOut });
            }
            foreach (string p in new[] { "gpu", "cpu", "ram" }) {
                if (lit.Contains(p)) continue;
                bool done = t == long.MaxValue || (job == null ? t - holdStart >= 300 + lit.Count * 450 : job.IsDone(p));
                if (!done || (t != long.MaxValue && t - last < 350)) break;
                lit.Add(p); last = t;
                lightUp(marks[Array.IndexOf(parts, p)]);
                if (t != long.MaxValue) break;
            }
        };
        sc.Root = art;
        return sc;
    }

}
