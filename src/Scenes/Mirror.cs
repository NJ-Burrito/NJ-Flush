// NJ-Flush 1.3 scene: GPU, the mirror. Beats and sound cues: design/1.3-scenes.md (cue times are fixed: the soundtrack is cut to them).
// Art: all hand-authored vector drawing in WPF (no third-party assets, no raster images). Light comes from the top left throughout:
// tiled wall with a soft shadow behind a bevelled chrome frame, a glass with a monitor reflection, sheen and edge shading, condensation
// made of soft blobs and glossy drops, a squeegee with rubber, aluminium channel and grip, a spray bottle, a frosted-over Frozen state.
// Performance: static parts are BitmapCache'd, only transforms, opacity and clip rects animate, no effects, about 300 elements at most.
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
    // GPU: the mirror. Frame, glass with a monitor reflection, condensation or frost, squeegee, drips, spray bottle, glint.
    // =====================================================================================================================
    static Scene Mirror(int v, string measure) {
        var sc = new Scene { Width = 340, Height = 352, Accent = "#4ADE80", Len = GpuLen[v], Sound = "scene-gpu-" + v + ".wav",
                             Pill = Tx.F("pill.gpu", Tx.Upper(Tx.SceneName("gpu", v)), measure),
                             Busy = Tx.T("busy.gpu") };
        var art = new WC.Canvas();
        const double gx = 60, gy = 22, gw = 220, gh = 252;
        var rnd = new Random(11 + v * 7);
        string[] barCols = { "#4ADE80", "#22D3EE", "#FBBF24", "#F87171" };   // the monitor's load bars: the heavier the tier, the higher and hotter
        double[][] bars = { new[] { .25, .35, .2, .3, .22 }, new[] { .5, .62, .45, .7, .55 }, new[] { .8, .92, .75, .96, .85 }, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 } };

        // ---------- wall: subway tiles in running bond, a soft light from the top left, the mirror's shadow ----------
        var wall = new WC.Canvas { Width = 340, Height = 352, CacheMode = MK.Cache(), Clip = MK.Fz(new WM.RectangleGeometry(new W.Rect(0, 0, 340, 352), 24, 24)) };
        wall.Children.Add(MK.Rc(340, 352, 24, MK.LG(0, 0, 1, 1, "#2B5368", "#193645", "#0E202C")));
        for (int i = 0; i < 16; i++) {
            int r = rnd.Next(0, 11), c = rnd.Next(0, 10);
            wall.Children.Add(At(MK.Rc(30, 28, 3, Br("#FFFFFF", (byte)rnd.Next(8, 22))), c * 34 + (r % 2) * 17 + 2, r * 32 + 2));
        }
        var tl = new System.Text.StringBuilder(); var th = new System.Text.StringBuilder();
        for (int r = 0; r <= 11; r++) {
            double y = r * 32;
            tl.Append("M0," + MK.F(y) + " L340," + MK.F(y) + " "); th.Append("M0," + MK.F(y + 1.3) + " L340," + MK.F(y + 1.3) + " ");
            for (int c = 0; c <= 10; c++) {
                double x = c * 34 + (r % 2) * 17;
                tl.Append("M" + MK.F(x) + "," + MK.F(y) + " L" + MK.F(x) + "," + MK.F(y + 32) + " ");
                th.Append("M" + MK.F(x + 1.3) + "," + MK.F(y + 1.3) + " L" + MK.F(x + 1.3) + "," + MK.F(y + 32) + " ");
            }
        }
        wall.Children.Add(new WS.Path { Data = MK.Fz(WM.Geometry.Parse(tl.ToString())), Stroke = Br("#07121B", 215), StrokeThickness = 1.7 });
        wall.Children.Add(new WS.Path { Data = MK.Fz(WM.Geometry.Parse(th.ToString())), Stroke = Br("#FFFFFF", 24), StrokeThickness = 1 });
        wall.Children.Add(At(MK.El(380, 320, MK.RG(.5, .5, "#36CFFAFE|0", "#00CFFAFE|1")), -110, -110));
        wall.Children.Add(At(MK.Rc(340, 130, 0, MK.LG(0, 0, 0, 1, "#00000000", "#77000000")), 0, 222));
        for (int i = 0; i < 4; i++) { double gr = 2 + i * 4; wall.Children.Add(At(MK.Rc(gw + 20 + 2 * gr, gh + 20 + 2 * gr, 22 + gr, Br("#000000", 26)), gx - 10 + 5 - gr, gy - 10 + 9 - gr)); }
        wall.Children.Add(At(new WS.Rectangle { Width = 338, Height = 350, RadiusX = 23, RadiusY = 23, Stroke = MK.LG(0, 0, 1, 1, "#66FFFFFF|0", "#00FFFFFF|0.5", "#33000000|1"), StrokeThickness = 2 }, 1, 1));
        art.Children.Add(wall);

        // ---------- frame: chrome, bevel, dark hairline, rim light, bolts ----------
        var frame = new WC.Canvas { Width = 340, Height = 352, CacheMode = MK.Cache() };
        double fx = gx - 10, fy = gy - 10, fw = gw + 20, fh = gh + 20;
        frame.Children.Add(At(MK.Rc(fw, fh, 22, MK.LG(0, 0, 1, 1, "#FFFFFF|0", "#DCE5EE|0.25", "#8A98A9|0.52", "#C9D4E0|0.78", "#F4F7FA|1")), fx, fy));
        frame.Children.Add(At(MK.Rc(fw - 6, fh - 6, 19, MK.LG(0, 0, 1, 1, "#6B7B8D|0", "#B8C5D3|0.35", "#F1F5F9|0.7", "#8E9CAD|1")), fx + 3, fy + 3));
        frame.Children.Add(At(MK.Rc(gw + 6, gh + 6, 16, MK.LG(0, 0, 1, 1, "#010409|0", "#1E293B|1")), gx - 3, gy - 3));
        frame.Children.Add(At(new WS.Rectangle { Width = fw - 2, Height = fh - 2, RadiusX = 21, RadiusY = 21, StrokeThickness = 1.5,
            Stroke = MK.LG(0, 0, 1, 1, "#F0FFFFFF|0", "#00FFFFFF|0.5", "#55FFFFFF|1") }, fx + 1, fy + 1));
        frame.Children.Add(At(MK.Rc(90, 2.6, 1.3, Br("#FFFFFF", 235)), gx + 14, fy + 2.2));
        frame.Children.Add(At(MK.Rc(2.4, 46, 1.2, Br("#FFFFFF", 220)), fx + 2.2, gy + 16));
        foreach (var b in new[] { new[] { fx + 5, fy + 5 }, new[] { fx + fw - 12, fy + 5 }, new[] { fx + 5, fy + fh - 12 }, new[] { fx + fw - 12, fy + fh - 12 } })
            frame.Children.Add(At(MK.El(7, 7, MK.RG(.35, .3, "#FFFFFF|0", "#94A3B8|0.6", "#475569|1")), b[0], b[1]));
        art.Children.Add(frame);

        // ---------- shelf below the mirror: chrome shelf, tumbler with two toothbrushes, a soap pump ----------
        var shelf = new WC.Canvas { Width = 340, Height = 352, CacheMode = MK.Cache() };
        shelf.Children.Add(At(MK.Rc(300, 24, 8, MK.LG(0, 0, 0, 1, "#70000000", "#00000000")), 20, 306));
        shelf.Children.Add(MK.Pa("M44,306 L62,306 L44,328 Z", MK.LG(0, 0, 1, 1, "#E2E8F0", "#64748B")));
        shelf.Children.Add(MK.Pa("M278,306 L296,306 L296,328 Z", MK.LG(0, 0, 1, 1, "#E2E8F0", "#64748B")));
        shelf.Children.Add(At(MK.Rc(300, 8, 3, MK.LG(0, 0, 0, 1, "#F1F5F9", "#B6C2CF")), 20, 291));
        shelf.Children.Add(At(MK.Rc(308, 11, 5, MK.LG(0, 0, 0, 1, "#FFFFFF|0", "#D5DEE8|0.35", "#8593A4|0.8", "#5B6A7C|1")), 16, 297));
        shelf.Children.Add(At(MK.Rc(290, 1.5, 0.7, Br("#FFFFFF", 220)), 26, 298.4));
        shelf.Children.Add(At(MK.El(40, 8, MK.RG(.5, .5, "#88000000|0", "#00000000|1")), 19, 288));
        shelf.Children.Add(At(MK.El(36, 8, MK.RG(.5, .5, "#88000000|0", "#00000000|1")), 289, 288));
        var tb1 = MK.Rc(4, 44, 2, MK.LG(0, 0, 1, 0, "#FBCFE8", "#F472B6", "#BE185D")); tb1.RenderTransform = new WM.RotateTransform(-13, 2, 44);
        var tb2 = MK.Rc(4, 44, 2, MK.LG(0, 0, 1, 0, "#BBF7D0", "#4ADE80", "#15803D")); tb2.RenderTransform = new WM.RotateTransform(11, 2, 44);
        shelf.Children.Add(At(tb1, 31, 244)); shelf.Children.Add(At(tb2, 39, 246));
        shelf.Children.Add(At(MK.Rc(6, 10, 2, MK.LG(0, 0, 1, 0, "#E0F2FE", "#38BDF8")), 29.6, 238));
        var cup = MK.Pa("M23,264 L51,264 L47,290 L27,290 Z", MK.LG(0, 0, 1, 0, "#A8F0FBFF|0", "#48BDE7F7|0.4", "#88A5D8EE|1")); cup.Stroke = Br("#FFFFFF", 150); cup.StrokeThickness = 1;
        shelf.Children.Add(cup);
        shelf.Children.Add(MK.Pa("M25,278 L49,278 L47,290 L27,290 Z", Br("#4D38BDF8")));
        shelf.Children.Add(At(new WS.Ellipse { Width = 28, Height = 6, Stroke = Br("#FFFFFF", 190), StrokeThickness = 1 }, 23, 261));
        shelf.Children.Add(At(MK.Rc(2.6, 20, 1.3, Br("#FFFFFF", 190)), 28, 267));
        shelf.Children.Add(At(MK.Rc(24, 32, 7, MK.LG(0, 0, 1, 0, "#D1FAE5", "#34D399", "#047857")), 294, 258));
        shelf.Children.Add(At(MK.Rc(14, 12, 3, Br("#FFFFFF", 205)), 299, 268));
        shelf.Children.Add(At(MK.Rc(2.6, 22, 1.3, Br("#FFFFFF", 175)), 297.5, 262));
        shelf.Children.Add(At(MK.Rc(12, 8, 2, MK.LG(0, 0, 1, 0, "#F1F5F9", "#94A3B8")), 300, 251));
        shelf.Children.Add(At(MK.Rc(5, 6, 1, Br("#94A3B8")), 303.5, 246));
        shelf.Children.Add(MK.Pa("M300,240 L322,240 L322,245 L310,245 L310,247 L300,247 Z", MK.LG(0, 0, 0, 1, "#F8FAFC", "#94A3B8")));
        art.Children.Add(shelf);

        // ---------- the glass ----------
        var glass = new WC.Canvas { Width = gw, Height = gh, ClipToBounds = true, Background = Br("#020617") };
        glass.Clip = MK.Fz(new WM.RectangleGeometry(new W.Rect(0, 0, gw, gh), 14, 14));
        art.Children.Add(At(glass, gx, gy));

        // the screen: everything the glass shows; it collapses to a line and a dot on power-off (Frozen)
        var screen = new WC.Canvas { Width = gw, Height = gh };
        var scrScale = new WM.ScaleTransform(1, 1, gw / 2, gh / 2); var scrJit = new WM.TranslateTransform();
        var sg = new WM.TransformGroup(); sg.Children.Add(scrScale); sg.Children.Add(scrJit); screen.RenderTransform = sg;
        glass.Children.Add(screen);
        var scrBg = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache() };
        scrBg.Children.Add(MK.Rc(gw, gh, 0, MK.LG(0, 0, 0.6, 1, "#14899F|0", "#0C3F5C|0.35", "#071B30|0.7", "#020B18|1")));
        scrBg.Children.Add(At(MK.El(46, 46, MK.RG(.5, .5, "#99FDE68A|0", "#33FDE68A|0.5", "#00FDE68A|1")), 150, 18));
        scrBg.Children.Add(At(MK.El(26, 26, MK.RG(.5, .5, "#66FFFFFF|0", "#00FFFFFF|1")), 188, 76));
        scrBg.Children.Add(At(MK.El(36, 36, MK.RG(.5, .5, "#5522D3EE|0", "#0022D3EE|1")), 16, 38));
        scrBg.Children.Add(At(MK.Rc(gw, 74, 0, MK.LG(0, 0, 0, 1, "#00020617", "#C0020617")), 0, gh - 74));
        scrBg.Children.Add(At(MK.Rc(gw, 1.5, 0, Br("#38BDF8", 50)), 0, gh - 60));
        screen.Children.Add(scrBg);
        var halo = MK.El(200, 160, MK.RG(.5, .5, "#6622D3EE|0", "#2222D3EE|0.5", "#0022D3EE|1")); screen.Children.Add(At(halo, 10, 52));
        halo.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(1800)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = new WA.SineEase { EasingMode = WA.EasingMode.EaseInOut } });
        var scrFg = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache() };
        var bezel = MK.Rc(108, 70, 8, MK.LG(0, 0, 0, 1, "#2D5078", "#0F2340")); bezel.Stroke = MK.LG(0, 0, 1, 1, "#CFFAFE", "#3B6E8F"); bezel.StrokeThickness = 1.4;
        scrFg.Children.Add(At(bezel, 56, 92));
        scrFg.Children.Add(At(MK.Rc(96, 56, 4, MK.LG(0, 0, 0, 1, "#0F5F7C", "#0A3148")), 62, 98));
        // the load bars: hot for the variant while it is dirty; once the glass is clean they settle to calm green bars
        var hotBars = new WC.Canvas(); var calmBars = new WC.Canvas { Opacity = v == 0 ? 1 : 0 };
        for (int i = 0; i < 5; i++) {
            double hh = 38 * bars[v][i], ch = 38 * bars[0][i];
            hotBars.Children.Add(At(MK.Rc(12, hh, 2, MK.LG(0, 0, 0, 1, barCols[v], "#AA" + barCols[v].Substring(1))), 68 + i * 17.5, 150 - hh));
            calmBars.Children.Add(At(MK.Rc(12, ch, 2, MK.LG(0, 0, 0, 1, barCols[0], "#AA" + barCols[0].Substring(1))), 68 + i * 17.5, 150 - ch));
        }
        if (v == 0) hotBars.Opacity = 0;
        scrFg.Children.Add(hotBars); scrFg.Children.Add(calmBars);
        sc.At(GpuLen[v] - 900, () => { if (v > 0) { Fade(hotBars, 0, 500); Fade(calmBars, 1, 500); } });
        scrFg.Children.Add(At(MK.Rc(88, 1.5, 0.7, Br("#FFFFFF", 90)), 66, 150.5));
        scrFg.Children.Add(MK.Pa("M64,100 L112,100 L84,152 L64,152 Z", MK.LG(0, 0, 1, 1, "#34FFFFFF", "#00FFFFFF")));
        scrFg.Children.Add(At(MK.Rc(14, 16, 2, MK.LG(1, 0, 0, 0, "#3C5A7A", "#17263C")), 103, 161));
        scrFg.Children.Add(At(MK.El(56, 10, MK.LG(0, 0, 0, 1, "#3C5A7A", "#101B2E")), 82, 173));
        for (int i = 0; i < 3; i++) {
            double[] sd = { 36, 96, 160 }, sw = { 28, 9, 20 };
            scrFg.Children.Add(At(new WS.Rectangle { Width = sw[i], Height = 420, Fill = MK.LG(0, 0, 1, 0, "#00FFFFFF", "#30FFFFFF", "#00FFFFFF"), RenderTransform = new WM.RotateTransform(26) }, sd[i], -70));
        }
        screen.Children.Add(scrFg);
        var white = new WS.Rectangle { Width = gw, Height = gh, Fill = Br("#F8FAFC"), Opacity = 0 };
        screen.Children.Add(white);

        // glitch (Frozen): two alternating sets of coloured blocks, tears, colour-split ghosts of the monitor, scanlines
        var glitchHost = new WC.Canvas { Opacity = 0 };
        WC.Canvas gA = new WC.Canvas(), gB = new WC.Canvas();
        if (v == 3) {
            string[] cols = { "#F472B6", "#22D3EE", "#A3E635", "#FACC15", "#818CF8" };
            foreach (var g in new[] { gA, gB }) {
                for (int i = 0; i < 12; i++) g.Children.Add(At(MK.Rc(14 + rnd.Next(0, 56), 5 + rnd.Next(0, 14), 0, Br(cols[i % cols.Length], 205)), rnd.Next(0, (int)gw - 40), rnd.Next(0, (int)gh - 20)));
                for (int i = 0; i < 2; i++) g.Children.Add(At(MK.Rc(gw, 6 + rnd.Next(0, 6), 0, MK.LG(0, 0, 1, 0, "#AAF472B6", "#8822D3EE")), 0, rnd.Next(10, (int)gh - 20)));
            }
            gA.Children.Add(At(new WS.Rectangle { Width = 108, Height = 70, RadiusX = 8, RadiusY = 8, Stroke = Br("#F472B6", 190), StrokeThickness = 2.5 }, 50, 92));
            gB.Children.Add(At(new WS.Rectangle { Width = 108, Height = 70, RadiusX = 8, RadiusY = 8, Stroke = Br("#22D3EE", 190), StrokeThickness = 2.5 }, 62, 92));
            var sbs = new System.Text.StringBuilder(); for (int y = 0; y < (int)gh; y += 5) sbs.Append("M0," + y + " L220," + y + " ");
            glitchHost.Children.Add(new WS.Path { Data = MK.Fz(WM.Geometry.Parse(sbs.ToString())), Stroke = Br("#FFFFFF", 22), StrokeThickness = 1 });
            glitchHost.Children.Add(gA); glitchHost.Children.Add(gB);
            screen.Children.Add(glitchHost);
        }

        // ---------- condensation (Smudge, Steamy, Fogged) or frost (Frozen): soft blobs, glossy drops; each layer is wiped by its own clip ----------
        var dropBr = MK.RG(.35, .3, "#FFFFFFFF|0", "#99CBD5E1|0.5", "#66475569|1");
        var clipA = new WM.RectangleGeometry(new W.Rect(0, 0, gw, gh)); var clipB = new WM.RectangleGeometry(new W.Rect(0, 0, gw, gh));
        WC.Canvas fog = null, haze = null;
        Func<double, int, int, bool, WM.RectangleGeometry, WC.Canvas> mist = (op, blobs, drops, sheet, clip) => {
            var host = new WC.Canvas { Width = gw, Height = gh, Clip = clip, Opacity = op };
            var inner = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache() };
            if (sheet) inner.Children.Add(MK.Rc(gw, gh, 0, MK.LG(0, 0, 0, 1, "#E9F0F7", "#D3DFEA", "#BCCBDB", "#D8E3EE")));
            var lite = MK.RG(.5, .5, "#B0FFFFFF|0", "#00FFFFFF|1"); var dark = MK.RG(.5, .5, "#6E7F93A8|0", "#007F93A8|1");
            for (int i = 0; i < blobs; i++) { double s = 60 + rnd.Next(0, 90); inner.Children.Add(At(MK.El(s, s * 0.8, i % 3 != 2 ? lite : dark), rnd.Next(-30, (int)gw - 30), rnd.Next(-30, (int)gh - 30))); }
            for (int i = 0; i < drops; i++) { double d = 2.5 + rnd.NextDouble() * 3.5; inner.Children.Add(At(MK.El(d, d * 1.18, dropBr), rnd.Next(4, (int)gw - 8), rnd.Next(4, (int)gh - 8))); }
            host.Children.Add(inner);
            return host;
        };
        if (v == 0) {   // a smudge, not a sheet: soft patches and a finger-wipe arc
            fog = new WC.Canvas { Width = gw, Height = gh, Clip = clipA };
            var inner = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache() };
            var patch = MK.RG(.5, .5, "#C8E2E8F0|0", "#88DDE7F0|0.5", "#00DDE7F0|1");
            inner.Children.Add(At(MK.El(130, 104, patch), 14, 62)); inner.Children.Add(At(MK.El(116, 96, patch), 78, 100)); inner.Children.Add(At(MK.El(104, 72, patch), 40, 146));
            foreach (var w in new[] { new[] { 20.0, 22 }, new[] { 12.0, 34 }, new[] { 5.0, 60 } }) {
                var arc = new WS.Path { Data = MK.Fz(WM.Geometry.Parse("M36,128 C66,92 112,158 164,112")), Stroke = Br("#E2E8F0", (byte)w[1]), StrokeThickness = w[0], StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round };
                inner.Children.Add(arc);
            }
            for (int i = 0; i < 16; i++) { double d = 2.5 + rnd.NextDouble() * 3; inner.Children.Add(At(MK.El(d, d * 1.18, dropBr), 26 + rnd.Next(0, 150), 76 + rnd.Next(0, 110))); }
            fog.Children.Add(inner);
        } else if (v == 1) fog = mist(0.8, 9, 30, true, clipA);
        else if (v == 2) { haze = mist(0.4, 6, 0, true, clipB); glass.Children.Add(haze); fog = mist(0.93, 11, 40, true, clipA); }
        if (v == 1 || v == 2) {   // slow rising wisps keep the fog alive until the blade clears it
            var wl = MK.RG(.5, .5, "#80FFFFFF|0", "#00FFFFFF|1");
            for (int i = 0; i < 3; i++) {
                var wt = new WM.TranslateTransform(); var we = MK.El(130, 74, wl); we.RenderTransform = wt; fog.Children.Add(At(we, 10 + i * 70, 70 + i * 55));
                wt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -26, TimeSpan.FromMilliseconds(2200 + i * 300)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = new WA.SineEase { EasingMode = WA.EasingMode.EaseInOut } });
            }
        }
        // the black of the switched-off screen (Frozen); the frost lies on top of it
        var black = new WS.Rectangle { Width = gw, Height = gh, Fill = Br("#000000"), Opacity = 0 };
        glass.Children.Add(black);
        WC.Canvas iceRim = null;
        if (v == 3) {
            fog = new WC.Canvas { Width = gw, Height = gh, Clip = clipA, Opacity = 0 };
            var inner = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache() };
            inner.Children.Add(MK.Rc(gw, gh, 0, MK.RG(.5, .5, "#5CE0F2FF|0", "#A0D6EEFB|0.55", "#EBF1FAFF|1")));
            double[][] corners = { new[] { 0.0, 0 }, new[] { gw, 0 }, new[] { gw, gh }, new[] { 0.0, gh } };
            var bloom = MK.RG(.5, .5, "#D8FFFFFF|0", "#00FFFFFF|1");
            var fs = new System.Text.StringBuilder();
            for (int c = 0; c < 4; c++) {
                inner.Children.Add(At(MK.El(170, 170, bloom), corners[c][0] - 85, corners[c][1] - 85));
                for (int k = 0; k < 4; k++) MK.Fern(fs, corners[c][0], corners[c][1], c * Math.PI / 2 + (k + 0.5) * (Math.PI / 2 / 4), 85 + rnd.Next(0, 40), 2);
            }
            var fd = MK.Fz(WM.Geometry.Parse(fs.ToString()));
            inner.Children.Add(new WS.Path { Data = fd, Stroke = Br("#9BD4F5", 90), StrokeThickness = 2.6 });
            inner.Children.Add(new WS.Path { Data = fd, Stroke = Br("#FFFFFF", 215), StrokeThickness = 1.1 });
            for (int i = 0; i < 16; i++) { double d = 1.5 + rnd.NextDouble() * 2; inner.Children.Add(At(MK.El(d, d, Br("#FFFFFF", 235)), rnd.Next(4, (int)gw - 6), rnd.Next(4, (int)gh - 6))); }
            fog.Children.Add(inner);
            // ice along the top of the frame
            iceRim = new WC.Canvas { Width = 340, Height = 352, Opacity = 0, CacheMode = MK.Cache() };
            iceRim.Children.Add(At(MK.Rc(fw, 5, 2.5, MK.LG(0, 0, 1, 0, "#F2FFFFFF", "#AEDBF5", "#F2FFFFFF")), fx, fy));
            for (int i = 0; i < 8; i++) {
                double ix = gx + 14 + i * (gw - 28) / 7 + rnd.Next(-4, 5), len = 12 + rnd.Next(0, 18);
                iceRim.Children.Add(MK.Pa("M" + MK.F(ix) + "," + MK.F(fy + 3) + " L" + MK.F(ix + 6) + "," + MK.F(fy + 3) + " L" + MK.F(ix + 3) + "," + MK.F(fy + 3 + len) + " Z", MK.LG(0, 0, 0, 1, "#F5FFFFFF", "#AACDEBFA")));
            }
        }
        glass.Children.Add(fog);
        // condensation drops already running (Fogged): each leaves a wet trail
        if (v == 2) for (int i = 0; i < 6; i++) {
            double dx = 18 + i * 34, dy = 8 + rnd.Next(0, 50); int dur = 1800 + i * 160, bg = i * 230;
            var trail = MK.Rc(2.6, 230, 1.3, MK.LG(0, 0, 0, 1, "#00061B2B", "#99061B2B")); var ts = new WM.ScaleTransform(1, 0); trail.RenderTransform = ts;
            var tt = new WM.TranslateTransform(); var bead = MK.El(6, 8, dropBr); bead.RenderTransform = tt;
            fog.Children.Add(At(trail, dx + 1.7, dy)); fog.Children.Add(At(bead, dx, dy));
            tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, 230, TimeSpan.FromMilliseconds(dur)) { RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QIn, BeginTime = TimeSpan.FromMilliseconds(bg) });
            ts.BeginAnimation(WM.ScaleTransform.ScaleYProperty, new WA.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(dur)) { RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QIn, BeginTime = TimeSpan.FromMilliseconds(bg) });
        }
        // wet beads that each spritz leaves (Fogged); they sit in the fog and go with it
        var wet = new List<WS.Ellipse>();
        if (v == 2) for (int i = 0; i < 15; i++) { double d = 5 + rnd.NextDouble() * 4; var e = MK.El(d, d * 1.2, dropBr); e.Opacity = 0; fog.Children.Add(At(e, 96 + rnd.Next(0, 100), 14 + rnd.Next(0, 90))); wet.Add(e); }

        // drips behind the blade (Steamy): a bead forms, drops, runs down leaving a wet trail
        var dripSets = new List<Tuple<WC.Canvas, WM.TranslateTransform, WM.ScaleTransform, WM.ScaleTransform>>();
        if (v == 1) {
            double[] dxs = { 8, 31, 89, 164 }, dys = { 34, 60, 28, 52 };
            for (int i = 0; i < 4; i++) {
                var dc = new WC.Canvas { Opacity = 0 };
                var trail = MK.Rc(2.4, 165, 1.2, MK.LG(0, 0, 0, 1, "#00E0F2FE", "#66E0F2FE")); var ts = new WM.ScaleTransform(1, 0); trail.RenderTransform = ts;
                dc.Children.Add(At(trail, -1.2, 0));
                var bt = new WM.TranslateTransform(); var bs = new WM.ScaleTransform(0.2, 0.2, 0, 3); var bg = new WM.TransformGroup(); bg.Children.Add(bs); bg.Children.Add(bt);
                var bc = new WC.Canvas { RenderTransform = bg };
                bc.Children.Add(MK.Pa("M0,-5 C0,-5 4.2,1 4.2,3.6 A4.2,4.2 0 1 1 -4.2,3.6 C-4.2,1 0,-5 0,-5 Z", MK.RG(.35, .3, "#FFFFFFFF|0", "#CCBAE6FD|0.55", "#6638BDF8|1")));
                bc.Children.Add(At(MK.El(1.8, 2.6, Br("#FFFFFF", 235)), -2.6, 0));
                dc.Children.Add(bc);
                glass.Children.Add(At(dc, dxs[i], dys[i]));
                dripSets.Add(Tuple.Create(dc, bt, ts, bs));
            }
        }
        // finish: sheen from the top left, edge shading, a hairline of light (static, cached)
        var finish = new WC.Canvas { Width = gw, Height = gh, CacheMode = MK.Cache(), IsHitTestVisible = false };
        finish.Children.Add(MK.Rc(gw, gh, 0, MK.RG(.5, .5, "#00020617|0", "#00020617|0.8", "#40020617|1")));
        finish.Children.Add(MK.Pa("M0,0 L120,0 L0,152 Z", MK.LG(0, 0, 1, 1, "#42FFFFFF", "#00FFFFFF")));
        finish.Children.Add(MK.Pa("M142,0 L158,0 L0,200 L0,182 Z", Br("#12FFFFFF")));
        finish.Children.Add(At(new WS.Rectangle { Width = gw - 2, Height = gh - 2, RadiusX = 13, RadiusY = 13, Stroke = MK.LG(0, 0, 1, 1, "#66FFFFFF", "#00FFFFFF", "#22FFFFFF"), StrokeThickness = 1 }, 1, 1));
        glass.Children.Add(finish);
        // CRT glow on power-off / power-on (Frozen)
        var crt = MK.El(gw + 60, 54, MK.RG(.5, .5, "#FFE6FBFF|0", "#66A5F3FC|0.45", "#00A5F3FC|1")); crt.Opacity = 0;
        var crtT = new WM.ScaleTransform(1, 1, (gw + 60) / 2, 27); crt.RenderTransform = crtT;
        if (v == 3) glass.Children.Add(At(crt, -30, gh / 2 - 27));
        // glint across the clean glass: a broad band and a thin one
        var glX = new WM.TranslateTransform(-150, 0); var gg = new WM.TransformGroup(); gg.Children.Add(new WM.RotateTransform(28)); gg.Children.Add(glX);
        var glint = new WC.Canvas { RenderTransform = gg, Opacity = 0, IsHitTestVisible = false };
        glint.Children.Add(new WS.Rectangle { Width = 34, Height = 560, Fill = MK.LG(0, 0, 1, 0, "#00FFFFFF", "#CCFFFFFF", "#00FFFFFF") });
        glint.Children.Add(At(new WS.Rectangle { Width = 8, Height = 560, Fill = MK.LG(0, 0, 1, 0, "#00FFFFFF", "#99FFFFFF", "#00FFFFFF") }, 46, 0));
        glass.Children.Add(At(glint, 0, -100));
        if (iceRim != null) art.Children.Add(iceRim);

        // ---------- squeegee: black rubber blade, aluminium channel, neck, yellow ribbed grip; origin = the blade's leading edge ----------
        var sq = new WC.Canvas { Opacity = 0 };
        var sqTilt = new WM.RotateTransform(0, 0, gh / 2); var sqFlip = new WM.ScaleTransform(1, 1); var sqX = new WM.TranslateTransform(10, 0);
        var sqG = new WM.TransformGroup(); sqG.Children.Add(sqTilt); sqG.Children.Add(sqFlip); sqG.Children.Add(sqX); sq.RenderTransform = sqG;
        var sqArt = new WC.Canvas { CacheMode = MK.Cache() };
        double mid = gh / 2;
        sqArt.Children.Add(At(MK.Rc(16, gh + 14, 0, MK.LG(0, 0, 1, 0, "#44000000", "#00000000")), 1, -7));
        sqArt.Children.Add(At(MK.Rc(6, gh + 12, 2, MK.LG(0, 0, 1, 0, "#4B5563", "#111827", "#1F2937")), -6, -6));
        sqArt.Children.Add(At(MK.Rc(11, gh + 20, 4, MK.LG(0, 0, 1, 0, "#F8FAFC", "#CBD5E1", "#64748B", "#E2E8F0")), -17, -10));
        sqArt.Children.Add(At(MK.Rc(1.5, gh + 6, 0.7, Br("#FFFFFF", 210)), -14.5, -3));
        sqArt.Children.Add(At(MK.Rc(14, 36, 5, MK.LG(0, 0, 0, 1, "#E2E8F0", "#94A3B8", "#64748B")), -30, mid - 18));
        sqArt.Children.Add(At(MK.Rc(64, 22, 11, MK.LG(0, 0, 0, 1, "#FEF3C7|0", "#FBBF24|0.35", "#D97706|0.75", "#92400E|1")), -92, mid - 11));
        for (int i = 0; i < 6; i++) sqArt.Children.Add(At(MK.Rc(3, 15, 1.5, Br("#78350F", 120)), -80 + i * 8.5, mid - 7.5));
        sqArt.Children.Add(At(MK.Rc(46, 3, 1.5, Br("#FFFFFF", 175)), -74, mid - 8.5));
        sqArt.Children.Add(At(MK.El(7, 7, Br("#451A03", 200)), -89, mid - 3.5));
        sq.Children.Add(sqArt);
        // the wet bead the blade pushes ahead of it
        var beadC = new WC.Canvas { Opacity = 0 };
        beadC.Children.Add(At(MK.Rc(4.5, gh, 2, MK.LG(0, 0, 1, 0, "#CCE0F2FE", "#66A5F3FC", "#00A5F3FC")), 0, 0));
        for (int i = 0; i < 6; i++) beadC.Children.Add(At(MK.El(7, 7, MK.RG(.35, .3, "#FFFFFFFF|0", "#AACFEAFB|0.6", "#4438BDF8|1")), 0.5, 18 + i * 40 + rnd.Next(0, 12)));
        sq.Children.Add(beadC);
        art.Children.Add(At(sq, gx, gy));

        // ---------- motion vocabulary of this scene ----------
        // the blade's leading edge runs a -> b over ms; the fog's clip edge runs with it, so the glass clears exactly where the rubber passes
        Action<WM.RectangleGeometry, int, bool, double, double> sweep = (clip, ms, ltr, a, b) => {
            sqX.BeginAnimation(WM.TranslateTransform.XProperty, A(a, b, ms, QInOut));
            sqTilt.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, ms * 0.15, 4, ms * 0.85, 4, ms, 0));
            beadC.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 90, 1, ms - 90, 1, ms, 0));
            var r0 = ltr ? new W.Rect(a, 0, gw + 8 - a, gh) : new W.Rect(-30, 0, a + 30, gh);
            var r1 = ltr ? new W.Rect(b, 0, 0, gh) : new W.Rect(-30, 0, b + 30, gh);
            clip.BeginAnimation(WM.RectangleGeometry.RectProperty, new WA.RectAnimation(r0, r1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = QInOut });
        };
        Action fadeIn = () => Fade(sq, 1, 200);
        Action<int> windBack = ms => {   // anticipation: 18 px back, away from the direction of travel
            sqX.BeginAnimation(WM.TranslateTransform.XProperty, A(10, -8, ms, QInOut));
            sqTilt.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, ms, -4));
        };
        Action exitRight = () => { Fade(sq, 0, 240); sqX.BeginAnimation(WM.TranslateTransform.XProperty, A(null, gw + 30, 260, COut)); sqTilt.BeginAnimation(WM.RotateTransform.AngleProperty, A(null, 0, 260, QOut)); };
        var starA = Star(11, "#FFFFFF"); var starB = Star(7, "#CFFAFE");
        art.Children.Add(At(starA, gx + gw - 28, gy + 36)); art.Children.Add(At(starB, gx + 36, gy + gh - 62));
        Action<int> doGlint = ms => {
            glint.Opacity = 1; glX.BeginAnimation(WM.TranslateTransform.XProperty, A(-150, gw + 170, ms, QInOut));
            Glint(starA, (int)(ms * 0.62)); Glint(starB, (int)(ms * 0.78));
        };

        // ---------- the beats (times are the contract; see design/1.3-scenes.md) ----------
        if (v == 0) {
            sc.At(350, fadeIn);
            sc.At(450, () => windBack(250));
            sc.At(700, () => sweep(clipA, 900, true, -8, gw + 8));
            sc.At(1600, exitRight);
            sc.At(1700, () => doGlint(600));
        } else if (v == 1) {
            sc.At(350, fadeIn);
            sc.At(450, () => windBack(250));
            sc.At(720, () => sweep(clipA, 1300, true, -8, gw + 8));
            double[] rel = { 220, 420, 620, 860 };
            for (int i = 0; i < 4; i++) {
                int ii = i;
                sc.At(720 + (int)rel[i], () => {   // the bead forms behind the blade, lets go and runs down
                    var d = dripSets[ii];
                    d.Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 110, 1, 1000, 1, 1250, 0));
                    S2(d.Item4, A(0.2, 1, 220, Back));
                    d.Item2.BeginAnimation(WM.TranslateTransform.YProperty, A(0, 165, 1000, QIn, 170));
                    d.Item3.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(0, 1, 1000, QIn, 170));
                });
            }
            sc.At(2150, exitRight);
            sc.At(2200, () => doGlint(650));
        } else if (v == 2) {
            // spray bottle, top right: slides in, three spritzes (trigger squeeze, recoil, a cone of mist), slides out
            var bottle = new WC.Canvas { Opacity = 0 };
            var bRot = new WM.RotateTransform(-10, 25, 48); var bTr = new WM.TranslateTransform(40, 0); var bGrp = new WM.TransformGroup(); bGrp.Children.Add(bRot); bGrp.Children.Add(bTr); bottle.RenderTransform = bGrp;
            var bArt = new WC.Canvas { CacheMode = MK.Cache() };
            bArt.Children.Add(At(MK.El(44, 8, MK.RG(.5, .5, "#66000000|0", "#00000000|1")), 5, 90));
            bArt.Children.Add(MK.Pa("M6,32 C6,23 14,19 20,17 L32,17 C38,19 44,23 44,32 L44,80 C44,86 40,91 34,91 L16,91 C10,91 6,86 6,80 Z", MK.LG(0, 0, 1, 0, "#C7E5FF|0", "#60A5FA|0.45", "#1D4ED8|1")));
            bArt.Children.Add(MK.Pa("M6,56 L44,56 L44,80 C44,86 40,91 34,91 L16,91 C10,91 6,86 6,80 Z", MK.LG(0, 0, 1, 0, "#6693C5FD|0", "#664F8EF7|0.5", "#661E40AF|1")));
            bArt.Children.Add(At(MK.Rc(3, 44, 1.5, Br("#FFFFFF", 150)), 10, 30));
            bArt.Children.Add(At(MK.Rc(26, 26, 5, Br("#FFFFFF", 235)), 12, 46));
            bArt.Children.Add(MK.Pa("M25,52 L27,58 L33,60 L27,62 L25,68 L23,62 L17,60 L23,58 Z", Br("#22D3EE")));
            bArt.Children.Add(At(MK.Rc(16, 10, 2, MK.LG(0, 0, 0, 1, "#F1F5F9", "#94A3B8")), 18, 8));
            bArt.Children.Add(MK.Pa("M16,0 L44,0 L44,13 L16,13 C7,13 2,11 2,7 C2,3 7,0 16,0 Z", MK.LG(0, 0, 0, 1, "#FFFFFF|0", "#E2E8F0|0.5", "#94A3B8|1")));
            bArt.Children.Add(At(MK.Rc(3, 6, 1.5, Br("#1E293B")), 0.5, 4));
            bArt.Children.Add(At(MK.Rc(24, 2, 1, Br("#FFFFFF", 220)), 18, 2));
            bottle.Children.Add(bArt);
            var trigR = new WM.RotateTransform(0, 4, 0);
            var trig = MK.Rc(8, 24, 4, MK.LG(1, 0, 0, 0, "#F8FAFC", "#94A3B8")); trig.RenderTransform = trigR; bottle.Children.Add(At(trig, 12, 12));
            art.Children.Add(At(bottle, 292, 36));
            var mistBr = MK.RG(.5, .5, "#E6F0F9FF|0", "#88E0F2FE|0.5", "#00E0F2FE|1");
            sc.At(300, () => { Fade(bottle, 1, 200); bTr.BeginAnimation(WM.TranslateTransform.XProperty, A(40, 0, 260, Back)); });
            int[] spr = { 450, 650, 850 };
            for (int s = 0; s < 3; s++) {
                int si = s;
                sc.At(spr[s], () => {
                    trigR.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 50, -22, 170, 0));
                    bTr.BeginAnimation(WM.TranslateTransform.XProperty, K(0, 0, 50, 5, 200, 0));
                    for (int k = 0; k < 4; k++) {
                        double sz = 26 + k * 9; int dl = k * 35;
                        var tr = new WM.TranslateTransform(); var scl = new WM.ScaleTransform(0.4, 0.4, sz / 2, sz * 0.35);
                        var gt = new WM.TransformGroup(); gt.Children.Add(scl); gt.Children.Add(tr);
                        var e = MK.El(sz, sz * 0.7, mistBr); e.RenderTransform = gt; e.Opacity = 0; e.IsHitTestVisible = false;
                        art.Children.Add(At(e, 284 - sz / 2, 52 - sz * 0.35));
                        var op = K(0, 0, 70, 0.85, 480, 0); op.BeginTime = TimeSpan.FromMilliseconds(dl);
                        e.BeginAnimation(W.UIElement.OpacityProperty, op);
                        tr.BeginAnimation(WM.TranslateTransform.XProperty, A(0, -(46 + k * 24), 520, COut, dl));
                        tr.BeginAnimation(WM.TranslateTransform.YProperty, A(0, 8 + k * 11, 520, QIn, dl));
                        S2(scl, A(0.4, 1.3, 520, COut, dl));
                    }
                    for (int k = 0; k < 5; k++) {   // the mist lands: fat wet beads on the glass
                        var e = wet[si * 5 + k]; var o = K(0, 0, 160, 1); o.BeginTime = TimeSpan.FromMilliseconds(170 + k * 40);
                        e.BeginAnimation(W.UIElement.OpacityProperty, o);
                    }
                });
            }
            sc.At(960, () => { Fade(bottle, 0, 200); bTr.BeginAnimation(WM.TranslateTransform.XProperty, A(null, 40, 220, QIn)); });
            sc.At(930, () => { fadeIn(); windBack(170); });
            sc.At(1100, () => sweep(clipA, 1100, true, -8, gw + 8));
            sc.At(2200, () => {   // turn the squeegee over in the hand and wind back to the right
                sqFlip.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 120, -1));
                sqX.BeginAnimation(WM.TranslateTransform.XProperty, A(null, gw + 22, 140, QInOut));
                sqTilt.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 140, -4));
            });
            sc.At(2350, () => sweep(clipB, 1000, false, gw + 22, -12));
            sc.At(3300, () => Fade(sq, 0, 250));
            sc.At(3350, () => doGlint(650));
        } else {
            sc.At(200, () => {   // glitch: coloured blocks flicker, the picture jitters, frost creeps over the glass and the frame
                glitchHost.Opacity = 1;
                gA.BeginAnimation(W.UIElement.OpacityProperty, MK.Disc(1300, 0, 1, 90, 0, 170, 1, 260, 0, 330, 1));
                gB.BeginAnimation(W.UIElement.OpacityProperty, MK.Disc(1300, 0, 0, 90, 1, 170, 0, 260, 1, 330, 0));
                scrJit.BeginAnimation(WM.TranslateTransform.XProperty, MK.Disc(1300, 0, 0, 50, -4, 110, 3, 160, -2, 230, 5, 280, 0, 340, 0));
                fog.BeginAnimation(W.UIElement.OpacityProperty, A(0, 0.92, 1250, QOut));
                iceRim.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 1250, QOut));
            });
            sc.At(1500, () => {   // power off: white line, then a dot, then black
                gA.BeginAnimation(W.UIElement.OpacityProperty, null); gB.BeginAnimation(W.UIElement.OpacityProperty, null); glitchHost.Opacity = 0;
                scrJit.BeginAnimation(WM.TranslateTransform.XProperty, null);
                white.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 120));
                scrScale.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(1, 0.02, 180, CIn));
                scrScale.BeginAnimation(WM.ScaleTransform.ScaleXProperty, A(1, 0.0, 160, CIn, 200));
                black.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 120, null, 340));
                crt.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 60, 1, 300, 0.9, 520, 0));
                crtT.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(1, 0.14, 180, CIn));
                crtT.BeginAnimation(WM.ScaleTransform.ScaleXProperty, A(1, 0.05, 160, CIn, 200));
            });
            sc.At(1700, () => { fadeIn(); windBack(300); });
            sc.At(2000, () => sweep(clipA, 1200, true, -8, gw + 8));
            for (int i = 0; i < 9; i++) {   // ice chips fly off the blade, each from where the blade is at that moment
                int ci = i; int ct = 2040 + i * 120;
                double bx = gx + (-8 + (gw + 16) * MK.Ease((ct - 2000) / 1200.0)) + 4, by = gy + 20 + rnd.Next(0, (int)gh - 40), cw = 3 + rnd.Next(0, 4);
                double dx = -(10 + rnd.Next(0, 22)), up = -(12 + rnd.Next(0, 12));
                var chip = MK.El(cw, cw * 0.7, MK.RG(.35, .3, "#FFFFFFFF|0", "#CCBAE6FD|1")); chip.Opacity = 0; chip.IsHitTestVisible = false;
                var ct2 = new WM.TranslateTransform(); chip.RenderTransform = ct2; art.Children.Add(At(chip, bx, by));
                sc.At(ct, () => {
                    chip.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 40, 1, 330, 1, 460, 0));
                    ct2.BeginAnimation(WM.TranslateTransform.XProperty, A(0, dx, 460, COut));
                    ct2.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 170, up, 460, 16));
                });
            }
            sc.At(3300, () => Fade(sq, 0, 200));
            sc.At(3400, () => {   // power on: dot -> line -> full, the clean reflection is back, the ice on the frame melts away
                black.BeginAnimation(W.UIElement.OpacityProperty, A(1, 0, 80));
                scrScale.BeginAnimation(WM.ScaleTransform.ScaleXProperty, A(0, 1, 180, COut));
                scrScale.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(0.02, 1, 220, Back, 180));
                white.BeginAnimation(W.UIElement.OpacityProperty, A(1, 0, 260, null, 260));
                crt.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 80, 0.9, 420, 0));
                crtT.BeginAnimation(WM.ScaleTransform.ScaleXProperty, A(0.05, 1, 180, COut));
                crtT.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(0.14, 1, 220, COut, 180));
                iceRim.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 700, QInOut));
            });
            sc.At(4300, () => doGlint(650));
        }
        sc.Root = art;
        return sc;
    }
}
