// NJ-Flush 1.3 scene: CPU, the exhaust fan. Beats and sound cues: design/1.3-scenes.md (cue times are fixed: the soundtrack is cut to them).
// Art: all hand-authored vector drawing in WPF (gradients, paths, transforms); no third-party assets, no raster images, no effects.
// Light comes from the top left everywhere. Static parts are BitmapCache'd; only transforms and opacities animate.
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

    // =====================================================================================================================
    // CPU: the exhaust fan. A tiled bathroom wall with a porcelain plate, a round chrome-rimmed fan behind a grille, a pull
    // cord, a thermometer, a warning lamp, and steam below. Four tiers: light, medium, heavy, clogged.
    // =====================================================================================================================
    static Scene Fan(int v, string measure) {
        var sc = new Scene { Width = 380, Height = 370, Accent = "#A78BFA", Len = CpuLen[v], Sound = "scene-cpu-" + v + ".wav",
                             Pill = Tx.F("pill.cpu", Tx.Upper(Tx.SceneName("cpu", v)), measure),
                             Busy = Tx.T("busy.cpu." + v) };
        var art = new WC.Canvas(); sc.Width = 380; sc.Height = 370;
        double cx = 214, cy = 150, R = 112;
        double heat = new[] { 0.4, 0.7, 0.85, 0.9 }[v];
        // cruise period of the blades (ms per turn), spin-up (degrees, ms) and the steam swirl (degrees) per tier
        int[] perOf = { 1400, 440, 380, 360 };
        double[] swirlOf = { 25, 70, 150, 210 };
        int per = perOf[v]; double swirl = swirlOf[v];

        // ---------------- the tiled wall (static, cached) ----------------
        var wall = FanStatic(1.3); wall.Clip = new WM.RectangleGeometry(new W.Rect(4, 6, 372, 350), 22, 22);
        wall.Children.Add(FanRect(4, 6, 372, 350, 0, FanLin(0, 0, 0.7, 1, "#F2193043", "#F20E1B2A", "#F2091320")));
        wall.Children.Add(FanRect(4, 6, 372, 350, 0, FanRad(0.3, 0.2, "#6638607D", "#00000000")));
        var tileSb = new StringBuilder();
        for (int k = 1; k < 8; k++) tileSb.Append("M" + FanF(4 + k * 46.5) + ",6 L" + FanF(4 + k * 46.5) + ",356 ");
        for (int k = 1; k < 8; k++) tileSb.Append("M4," + FanF(6 + k * 43.75) + " L376," + FanF(6 + k * 43.75) + " ");
        var tileGeo = WM.Geometry.Parse(tileSb.ToString()); tileGeo.Freeze();
        wall.Children.Add(new WS.Path { Data = tileGeo, Stroke = Br("#050B12", 220), StrokeThickness = 2.4, IsHitTestVisible = false });
        wall.Children.Add(new WS.Path { Data = tileGeo, Stroke = Br("#9CC4DC", 56), StrokeThickness = 1.4, RenderTransform = new WM.TranslateTransform(1.5, 1.5), IsHitTestVisible = false });
        wall.Children.Add(FanRect(4, 6, 372, 350, 0, FanRad(0.5, 0.45, "#00000000@0.55", "#AA000000")));
        wall.Children.Add(FanRect(4, 340, 372, 16, 0, FanLin(0, 0, 0, 1, "#FF2C445B", "#FF132130")));
        wall.Children.Add(FanRect(4, 340, 372, 1.5, 0, Br("#FFFFFF", 70)));
        wall.Children.Add(FanRect(4, 6, 372, 350, 22, null, FanLin(0, 0, 1, 1, "#66FFFFFF", "#10FFFFFF", "#33FFFFFF"), 3));
        Add(sc, art, wall);

        // warm halo behind the plate (hotter tiers), and the glow where the steam sits
        var halo = FanOval(cx - 154, cy - 154, 308, 308, FanRad(0.5, 0.5, "#00FB923C@0", "#00FB923C@0.55", "#80FB923C@0.8", "#00FB923C@1"));
        halo.Opacity = new[] { 0, 0.35, 0.8, 1.0 }[v]; Add(sc, art, halo);
        var glow = new WS.Ellipse { Width = 340, Height = 120, Fill = new WM.RadialGradientBrush(Alpha("#FB923C", (byte)(60 + 100 * heat)), Alpha("#FB923C", 0)), IsHitTestVisible = false };
        Add(sc, art, At(glow, cx - 170, 262));

        // ---------------- thermometer: back plate, glass tube, ticks (static), mercury and bulb (animated) ----------------
        double tx = 22, ty = 40, th = 220;
        var thermo = FanStatic(1.3);
        FanSoftRect(thermo, 12, 34, 52, 274, 16, 4, 2.5, 18);
        thermo.Children.Add(FanRect(6, 26, 52, 274, 16, FanLin(0, 0, 1, 1, "#FF34465C", "#FF1E2B3C", "#FF121B28"), FanLin(0, 0, 1, 1, "#88FFFFFF", "#18FFFFFF"), 1.5));
        var tk = new StringBuilder();
        for (int k = 0; k <= 10; k++) { double yy = ty + 16 + k * 17.6; double len = k % 5 == 0 ? 9 : 5; tk.Append("M" + FanF(tx - 4 - len) + "," + FanF(yy) + " L" + FanF(tx - 4) + "," + FanF(yy) + " "); }
        thermo.Children.Add(FanPath(tk.ToString(), null, Br("#CBD5E1", 200), 1.6));
        thermo.Children.Add(FanRect(tx, ty, 22, th, 11, FanLin(0, 0, 1, 0, "#FFB6C3D0", "#FFFFFFFF", "#FFE2E8F0", "#FF9AA9B8"), FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF7C8B9B"), 2));
        thermo.Children.Add(FanRect(tx + 6, ty + 10, 10, th - 24, 5, FanLin(0, 0, 1, 0, "#FF0B1220", "#FF263449")));
        Add(sc, art, thermo);
        var merc = Br("#F87171"); double lvl = heat;
        var mercScale = new WM.ScaleTransform(1, lvl, 5, th - 24);
        var mercCv = new WC.Canvas { RenderTransform = mercScale, IsHitTestVisible = false };
        mercCv.Children.Add(FanRect(0, 0, 10, th - 24, 5, merc));
        mercCv.Children.Add(FanRect(0, 0, 10, th - 24, 5, FanLin(0, 0, 1, 0, "#66000000", "#00000000@0.35", "#00000000@0.7", "#55000000")));
        mercCv.Children.Add(FanRect(2, 0, 2.6, th - 24, 1.3, Br("#FFFFFF", 150)));
        Add(sc, art, At(mercCv, tx + 6, ty + 10));
        var bulbGlow = new WS.Ellipse { Width = 74, Height = 74, Fill = new WM.RadialGradientBrush(Alpha("#F87171", 160), Alpha("#F87171", 0)), Opacity = 0, IsHitTestVisible = false };
        Add(sc, art, At(bulbGlow, tx - 26, ty + th - 33));
        var bulb = new WS.Ellipse { Width = 36, Height = 36, Fill = merc, IsHitTestVisible = false }; Add(sc, art, At(bulb, tx - 7, ty + th - 14));
        var bulbShade = FanStatic(1.3);
        bulbShade.Children.Add(FanOval(tx - 7, ty + th - 14, 36, 36, FanRad(0.35, 0.3, "#00000000@0.55", "#77000000")));
        bulbShade.Children.Add(FanOval(tx - 7, ty + th - 14, 36, 36, null, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF64748B"), 2.5));
        bulbShade.Children.Add(FanOval(tx + 1, ty + th - 8, 12, 8, FanLin(0, 0, 1, 1, "#E6FFFFFF", "#00FFFFFF")));
        Add(sc, art, bulbShade);
        Action<double, int, int> temp = (to, ms, delay) => mercScale.BeginAnimation(WM.ScaleTransform.ScaleYProperty, A(null, to, ms, QInOut, delay));

        // ---------------- the plate: shadow on the wall, then the plate as one shaking, squashing piece ----------------
        var shadow = FanStatic(1.2);
        FanSoftRect(shadow, 92, 32, 268, 268, 26, 7, 3.2, 17);
        Add(sc, art, shadow);

        var plate = new WC.Canvas(); var plateMove = new WM.TranslateTransform(); var plateScale = new WM.ScaleTransform(1, 1, cx, cy);
        var pg = new WM.TransformGroup(); pg.Children.Add(plateScale); pg.Children.Add(plateMove); plate.RenderTransform = pg;
        // porcelain body, bevel, chrome housing ring, recessed well, screws, grommet (static)
        var body = FanStatic(1.5);
        body.Children.Add(FanRect(cx - 134, cy - 134, 268, 268, 26, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FFF1F5F9", "#FFDCE4EA", "#FFB1BFCB")));
        body.Children.Add(FanRect(cx - 133, cy - 133, 266, 266, 25, null, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF8A9AAB"), 2.5));
        body.Children.Add(FanRect(cx - 122, cy - 122, 244, 244, 17, null, FanLin(0, 0, 1, 1, "#88708396", "#99FFFFFF"), 1.6));
        body.Children.Add(FanRect(cx - 100, cy - 131, 200, 3, 1.5, Br("#FFFFFF", 230)));
        body.Children.Add(FanOval(cx - R + 6, cy - R + 8, 2 * R, 2 * R, FanRad(0.5, 0.5, "#00000000@0.9", "#55000000")));        // contact shadow of the ring on the plate
        body.Children.Add(FanOval(cx - R, cy - R, 2 * R, 2 * R, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FFCBD5E1", "#FF6B7C90", "#FFE2E8F0")));
        body.Children.Add(FanOval(cx - R + 6, cy - R + 6, 2 * R - 12, 2 * R - 12, FanLin(0, 0, 1, 1, "#FF5E6E82", "#FFA9B6C4", "#FFF1F5F9")));
        body.Children.Add(FanOval(cx - R + 13, cy - R + 13, 2 * R - 26, 2 * R - 26, FanRad(0.42, 0.4, "#FF1E2F4B", "#FF0C1424", "#FF05080F")));
        body.Children.Add(FanOval(cx - R + 13, cy - R + 13, 2 * R - 26, 2 * R - 26, null, FanLin(0, 0, 1, 1, "#DD000000", "#00000000@0.55", "#66FFFFFF"), 5));
        foreach (var sp in new[] { new[] { cx + 112, cy - 112 }, new[] { cx - 112, cy + 112 } }) {
            body.Children.Add(FanOval(sp[0] - 5.5, sp[1] - 5.5, 11, 11, FanRad(0.35, 0.3, "#FFFFFFFF", "#FFA3B1BF", "#FF64748B"), Br("#64748B", 170), 0.8));
            body.Children.Add(FanPath("M" + FanF(sp[0] - 3.5) + "," + FanF(sp[1] + 1.5) + " L" + FanF(sp[0] + 3.5) + "," + FanF(sp[1] - 1.5), null, Br("#475569", 220), 1.4));
        }
        body.Children.Add(FanOval(cx + 112 - 8, cy + 130 - 8, 16, 16, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF94A3B8", "#FF475569")));
        body.Children.Add(FanOval(cx + 112 - 4, cy + 130 - 4, 8, 8, FanRad(0.4, 0.4, "#FF334155", "#FF020617")));
        plate.Children.Add(body);

        // blades (rotating): five curved blades with a leading-edge highlight, a hub with screws
        var blades = new WC.Canvas { IsHitTestVisible = false }; var rot = new WM.RotateTransform(0); blades.RenderTransform = rot;
        var bladeFill = FanLin(0, 0, 1, 1, "#FFF1F5F9", "#FFB8C4D2", "#FF6A7B90");
        var bladeData = WM.Geometry.Parse("M0,0 C14,-18 54,-34 86,-22 C92,-8 70,4 0,0 Z"); bladeData.Freeze();
        var edgeData = WM.Geometry.Parse("M12,-11 C30,-24 56,-30 82,-23"); edgeData.Freeze();
        for (int i = 0; i < 5; i++) {
            var bc = new WC.Canvas { RenderTransform = new WM.RotateTransform(i * 72) };
            bc.Children.Add(new WS.Path { Data = bladeData, Fill = bladeFill, Stroke = Br("#3B4B61", 150), StrokeThickness = 0.8 });
            bc.Children.Add(new WS.Path { Data = edgeData, Stroke = Br("#FFFFFF", 200), StrokeThickness = 1.8, StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round });
            blades.Children.Add(bc);
        }
        blades.Children.Add(FanOval(-19, -19, 38, 38, FanRad(0.35, 0.3, "#FFFFFFFF", "#FFB6C2D0", "#FF5B6B80"), Br("#475569", 150), 1));
        blades.Children.Add(FanOval(-10, -10, 20, 20, FanRad(0.35, 0.3, "#FFF8FAFC", "#FF94A3B8", "#FF475569")));
        for (int i = 0; i < 3; i++) { double a = i * 2 * Math.PI / 3; blades.Children.Add(FanOval(Math.Cos(a) * 14.5 - 1.8, Math.Sin(a) * 14.5 - 1.8, 3.6, 3.6, Br("#334155"))); }
        plate.Children.Add(At(blades, cx, cy));
        // motion blur disc, only visible when the blades are fast
        var blurDisc = FanOval(cx - 98, cy - 98, 196, 196, FanRad(0.5, 0.5, "#10E2E8F0@0", "#38CBD5E1@0.5", "#70F1F5F9@0.85", "#00F1F5F9@1")); blurDisc.Opacity = 0;
        plate.Children.Add(blurDisc);

        // grille on top of the blades: rings and spokes, embossed (static)
        var grille = FanStatic(1.5);
        var gg = new StringBuilder();
        foreach (double r in new[] { 32.0, 56, 80, 99 }) gg.Append("M" + FanF(cx - r) + "," + FanF(cy) + " A" + FanF(r) + "," + FanF(r) + " 0 1 1 " + FanF(cx + r) + "," + FanF(cy) + " A" + FanF(r) + "," + FanF(r) + " 0 1 1 " + FanF(cx - r) + "," + FanF(cy) + " ");
        for (int k = 0; k < 8; k++) { double a = k * Math.PI / 4 + Math.PI / 8; gg.Append("M" + FanF(cx + Math.Cos(a) * 20) + "," + FanF(cy + Math.Sin(a) * 20) + " L" + FanF(cx + Math.Cos(a) * 99) + "," + FanF(cy + Math.Sin(a) * 99) + " "); }
        var grData = WM.Geometry.Parse(gg.ToString()); grData.Freeze();
        grille.Children.Add(new WS.Path { Data = grData, Stroke = Br("#020617", 150), StrokeThickness = 2.4, RenderTransform = new WM.TranslateTransform(1.3, 1.6) });
        grille.Children.Add(new WS.Path { Data = grData, Stroke = FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FFCBD5E1", "#FF64748B"), StrokeThickness = 1.7 });
        grille.Children.Add(FanPath("M" + FanF(cx - 90) + "," + FanF(cy - 32) + " A96,96 0 0 1 " + FanF(cx - 32) + "," + FanF(cy - 90), null, Br("#FFFFFF", 80), 5));     // glass sheen
        grille.Children.Add(FanPath("M" + FanF(cx - 78) + "," + FanF(cy - 12) + " A80,80 0 0 1 " + FanF(cx - 62) + "," + FanF(cy - 50), null, Br("#FFFFFF", 55), 2.5));
        plate.Children.Add(grille);

        // overlays on the plate: heat tint, cool wash, frost
        var heatTint = FanRect(cx - 134, cy - 134, 268, 268, 26, FanRad(0.5, 0.5, "#00FB923C@0", "#00FB923C@0.6", "#6AFB923C@1")); heatTint.Opacity = new[] { 0, 0.35, 0.6, 0.8 }[v];
        plate.Children.Add(heatTint);
        var coolWash = FanRect(cx - 134, cy - 134, 268, 268, 26, FanLin(0, 0, 1, 1, "#6638BDF8", "#1A38BDF8", "#4438BDF8")); coolWash.Opacity = 0;
        plate.Children.Add(coolWash);
        var frost = new WC.Canvas { Opacity = 0, IsHitTestVisible = false };
        frost.Children.Add(FanRect(cx - 134, cy - 134, 268, 268, 26, FanRad(0.5, 0.5, "#107DD3FC@0", "#207DD3FC@0.5", "#C8E0F7FF@1")));
        frost.Children.Add(FanPath(FanFrost(cx - 128, cy - 128, 1, 1) + FanFrost(cx + 128, cy - 128, -1, 1) + FanFrost(cx - 128, cy + 128, 1, -1) + FanFrost(cx + 128, cy + 128, -1, -1), null, Br("#FFFFFF", 235), 1.5));
        frost.Children.Add(FanOval(cx - 130, cy - 130, 90, 60, FanRad(0.5, 0.5, "#AAFFFFFF", "#00FFFFFF")));
        frost.Children.Add(FanOval(cx + 40, cy + 74, 90, 58, FanRad(0.5, 0.5, "#AAFFFFFF", "#00FFFFFF")));
        plate.Children.Add(frost);
        var rimSheen = FanOval(cx - R + 2, cy - R + 2, 2 * R - 4, 2 * R - 4, null, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF7DD3FC", "#00FFFFFF"), 4); rimSheen.Opacity = 0;
        plate.Children.Add(rimSheen);

        // warning lamp: chrome bezel, dark glass, lit dome (blinks), specular, glow on the plate
        double lx = cx - 112, ly = cy - 112;
        var redGlow = FanOval(lx - 36, ly - 36, 72, 72, FanRad(0.5, 0.5, "#B0EF4444", "#00EF4444")); redGlow.Opacity = 0;
        var greenGlow = FanOval(lx - 36, ly - 36, 72, 72, FanRad(0.5, 0.5, "#9022C55E", "#0022C55E")); greenGlow.Opacity = 0;
        plate.Children.Add(redGlow); plate.Children.Add(greenGlow);
        plate.Children.Add(FanOval(lx - 14, ly - 14, 28, 28, FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FFCBD5E1", "#FF64748B", "#FFE2E8F0")));
        plate.Children.Add(FanOval(lx - 10, ly - 10, 20, 20, FanRad(0.5, 0.5, "#FF1E293B", "#FF020617")));
        var lampBrush = Br("#334155");
        var lamp = FanOval(lx - 9, ly - 9, 18, 18, lampBrush);
        plate.Children.Add(lamp);
        plate.Children.Add(FanOval(lx - 9, ly - 9, 18, 18, FanRad(0.35, 0.3, "#00000000@0.5", "#88000000")));
        plate.Children.Add(FanOval(lx - 6, ly - 7.5, 8, 4.5, Br("#FFFFFF", 200)));
        Add(sc, art, plate);

        // ---------------- pull cord: bead chain and a brass pull, hangs from the grommet, sways ----------------
        var cord = new WC.Canvas { IsHitTestVisible = false }; var cordY = new WM.TranslateTransform(); var sway = new WM.RotateTransform(0, 0, 0);
        var cg = new WM.TransformGroup(); cg.Children.Add(sway); cg.Children.Add(cordY); cord.RenderTransform = cg;
        cord.Children.Add(new WS.Line { X1 = 0, Y1 = 0, X2 = 0, Y2 = 58, Stroke = FanLin(0, 0, 1, 0, "#FFE2E8F0", "#FF94A3B8", "#FF475569"), StrokeThickness = 3, StrokeDashArray = new WM.DoubleCollection { 0.15, 0.95 }, StrokeDashCap = WM.PenLineCap.Round });
        cord.Children.Add(new WS.Line { X1 = -0.7, Y1 = 0, X2 = -0.7, Y2 = 58, Stroke = Br("#FFFFFF", 170), StrokeThickness = 0.9, StrokeDashArray = new WM.DoubleCollection { 0.15, 1.9 }, StrokeDashCap = WM.PenLineCap.Round });
        cord.Children.Add(FanOval(-3 + 4, 78, 15, 6, FanRad(0.5, 0.5, "#66000000", "#00000000")));
        cord.Children.Add(At(FanPath("M0,0 C-10,12 -11,28 0,34 C11,28 10,12 0,0 Z", FanLin(0, 0, 1, 1, "#FFFEF3C7", "#FFFBBF24", "#FFB45309"), Br("#78350F", 160), 0.8), 0, 54));
        cord.Children.Add(FanOval(-5, 66, 4.5, 8, Br("#FFFFFF", 190)));
        cord.Children.Add(FanOval(-4, 58, 8, 8, FanLin(0, 0, 1, 1, "#FFFEF3C7", "#FFB45309")));
        Add(sc, art, At(cord, cx + 112, cy + 132));
        sway.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-1.2, 1.2, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
        Action<double> pullBy = depth => {
            Tug(cordY, depth);
            sway.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 150, -6 - depth / 4, 420, 6 + depth / 4, 700, -3.5, 1000, 1.8, 1400, 0));
        };

        Action pull = () => pullBy(16);
        // ---------------- steam: soft volumetric puffs under the plate, swirling into the fan ----------------
        string[] steamIn = { "#A8FFFFFF", "#A8FFFAF2", "#A8FFF1DE", "#A8FFE8D0" }, steamMid = { "#50DCE6F0", "#50FDE3C4", "#58FDCB98", "#60FDB97E" };
        var puffBrush = FanRad(0.46, 0.4, steamIn[v] + "@0", steamMid[v] + "@0.5", steamMid[v].Substring(0, 1) + "00" + steamMid[v].Substring(3) + "@1");
        int puffsN = new[] { 3, 6, 8, 10 }[v];
        var puffs = new List<Tuple<WC.Canvas, WM.TranslateTransform, WM.ScaleTransform, WM.RotateTransform, double, double>>();
        var rnd = new Random(7 + v);
        double[][] blobs = { new[] { 0.0, 16, 48 }, new[] { 24.0, 0, 58 }, new[] { 52.0, 14, 44 }, new[] { 16.0, 28, 46 }, new[] { 42.0, 30, 38 } };
        for (int i = 0; i < puffsN; i++) {
            var wrap = new WC.Canvas { IsHitTestVisible = false }; var wr = new WM.RotateTransform(0); wrap.RenderTransform = wr;
            var p = new WC.Canvas { IsHitTestVisible = false }; var tt = new WM.TranslateTransform(); var ss = new WM.ScaleTransform(1, 1, 48, 36);
            var tg = new WM.TransformGroup(); tg.Children.Add(ss); tg.Children.Add(tt); p.RenderTransform = tg;
            foreach (var b in blobs) p.Children.Add(At(new WS.Ellipse { Width = b[2], Height = b[2], Fill = puffBrush, IsHitTestVisible = false }, b[0], b[1]));
            double x0 = 40 + (i % 5) * 62 + rnd.Next(0, 16), y0 = 262 + (i / 5) * 28 - rnd.Next(0, 10);
            p.Opacity = 0.62 + 0.35 * heat;
            wrap.Children.Add(At(p, x0 - cx, y0 - cy)); Add(sc, art, At(wrap, cx, cy));
            tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -9, TimeSpan.FromMilliseconds(900 + i * 70)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-5, 5, TimeSpan.FromMilliseconds(1400 + i * 90)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            S2(ss, new WA.DoubleAnimation(0.95, 1.1, TimeSpan.FromMilliseconds(1100 + i * 60)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            puffs.Add(Tuple.Create(p, tt, ss, wr, x0 - cx, y0 - cy));
        }
        // embers rising off the hot tiers
        int emN = new[] { 0, 0, 5, 9 }[v];
        var embers = new List<WS.Ellipse>();
        for (int i = 0; i < emN; i++) {
            var e = FanOval(0, 0, 5, 5, FanRad(0.5, 0.5, "#FFFFF1B8", "#FFFB923C", "#00EF4444")); e.Opacity = 0;
            var t = new WM.TranslateTransform(); e.RenderTransform = t; int dur = 1300 + rnd.Next(0, 900), begin = rnd.Next(0, 900);
            Add(sc, art, At(e, 50 + rnd.Next(0, 280), 244 + rnd.Next(0, 40)));
            t.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -96, TimeSpan.FromMilliseconds(dur)) { RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(begin), EasingFunction = QOut });
            t.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-7, 7, TimeSpan.FromMilliseconds(dur / 2)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(begin), EasingFunction = QInOut });
            var ka = K(0, 0, dur * 0.2, 0.95, dur, 0); ka.RepeatBehavior = WA.RepeatBehavior.Forever; ka.BeginTime = TimeSpan.FromMilliseconds(begin);
            e.BeginAnimation(W.UIElement.OpacityProperty, ka);
            embers.Add(e);
        }
        Action cooled = () => { foreach (var e in embers) e.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 500, QOut)); };

        Action<int, int> suck = (gap, ms) => {
            for (int i = 0; i < puffs.Count; i++) {
                var pf = puffs[i]; int d = i * gap;
                // x eases out, y eases in: an arc; the wrapper turns as the puff closes in: a spiral into the hub (L7)
                pf.Item2.BeginAnimation(WM.TranslateTransform.XProperty, A(null, -pf.Item5 - 48, ms, COut, d));
                pf.Item2.BeginAnimation(WM.TranslateTransform.YProperty, A(null, -pf.Item6 - 36, ms, QIn, d));
                S2(pf.Item3, A(null, 0.22, ms, QIn, d));
                pf.Item4.BeginAnimation(WM.RotateTransform.AngleProperty, A(0, swirl, ms, QInOut, d));
                pf.Item1.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, ms, QIn, d));
            }
            Fade(glow, 0, 1400, 300);
        };
        // cooling: the air turns cold: wash over the plate, pulse ring, heat fades
        var coolPulse = FanOval(cx - R, cy - R, 2 * R, 2 * R, null, Br("#7DD3FC", 200), 4); coolPulse.Opacity = 0; coolPulse.RenderTransform = new WM.ScaleTransform(0.92, 0.92, R, R);
        Add(sc, art, coolPulse);
        Action cool = () => {
            Tint(merc, "#38BDF8", 900);
            Fade(coolWash, new[] { 0.5, 0.7, 0.85, 0.45 }[v], 700); Fade(heatTint, 0, 1100); Fade(halo, 0, 1200); cooled();
            coolPulse.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 120, 0.9, 700, 0));
            S2((WM.ScaleTransform)coolPulse.RenderTransform, A(0.92, 1.22, 700, QOut));
        };
        Action<double, int> spinUpTo = (deg, ms) => {
            double s = (360.0 / per) * ms / deg;
            rot.BeginAnimation(WM.RotateTransform.AngleProperty, A(0, deg, ms, FanSpinIn(s)));
            Fade(blurDisc, new[] { 0.22, 0.5, 0.6, 0.65 }[v], ms); Fade(blades, new[] { 0.9, 0.7, 0.6, 0.55 }[v], ms);
        };
        Action<double> spinFast = per0 => {      // a spinner may be linear (L6)
            rot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(rot.Angle, rot.Angle + 360, TimeSpan.FromMilliseconds(per0)) { RepeatBehavior = WA.RepeatBehavior.Forever, IsCumulative = true });
            double amp = new[] { 0, 0.4, 0.8, 1.2 }[v];
            if (amp > 0) plateMove.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-amp, amp, TimeSpan.FromMilliseconds(33)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever });
        };
        Action<int> spinDown = ms => {           // starts at the cruise speed, so no pop; ends at rest (follow-through)
            plateMove.BeginAnimation(WM.TranslateTransform.XProperty, null);
            rot.BeginAnimation(WM.RotateTransform.AngleProperty, A(null, rot.Angle + 360.0 / per * ms / 2, ms, QOut));
            Fade(blurDisc, 0, ms); Fade(blades, 1, ms);
        };
        Action<int, int> glints = (count, delay) => {
            for (int k = 0; k < count; k++) {
                double a = (-60 + k * 85) * Math.PI / 180; var s = Star(9, "#E0F2FE");
                Add(sc, art, At(s, cx + Math.Cos(a) * (R - 4), cy + Math.Sin(a) * (R - 4))); Glint(s, delay + k * 140);
            }
            rimSheen.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 200, 0.85, 800, 0));
        };
        Action<int, int> blink = (times, period) => {
            lamp.BeginAnimation(W.UIElement.OpacityProperty, K(FanBlink(times, period, 1, 0.3, 1)));
            redGlow.BeginAnimation(W.UIElement.OpacityProperty, K(FanBlink(times, period, 0.95, 0.05, 0.55)));
        };
        Action ledOn = () => { Tint(lampBrush, "#22C55E", 220); Fade(greenGlow, 0.5, 260); };

        if (v == 0) {
            sc.At(400, () => { pull(); ledOn(); });
            sc.At(650, () => spinUpTo(540, 1000));
            sc.At(1650, () => spinFast(per));
            sc.At(900, () => suck(120, 700));
            sc.At(1500, () => { temp(0.2, 600, 0); cool(); });
            sc.At(2000, () => spinDown(900));
            sc.At(2300, () => glints(1, 0));
        } else if (v == 1) {
            sc.At(450, () => { pull(); ledOn(); });
            sc.At(700, () => spinUpTo(900, 1400));
            sc.At(2100, () => spinFast(per));
            sc.At(1300, () => suck(110, 900));
            sc.At(1600, () => { temp(0.3, 900, 0); cool(); });
            sc.At(2700, () => spinDown(1100));
            sc.At(3000, () => glints(3, 0));
        } else if (v == 2) {
            sc.At(200, () => { lampBrush.Color = C("#EF4444"); blink(3, 300); });
            sc.At(900, () => {                     // first tug: the fan sputters and jerks back
                pullBy(16);
                rot.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 250, 0, 520, 120, 700, 90, 900, 105));
                Shake(plateMove, 3, 4);
            });
            sc.At(1700, () => pullBy(24));        // second, harder tug: turbo
            sc.At(1950, () => spinUpTo(900, 1000));
            sc.At(2950, () => spinFast(per));
            sc.At(2300, () => suck(90, 800));
            sc.At(2600, () => {
                lampBrush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null); lamp.BeginAnimation(W.UIElement.OpacityProperty, null); lamp.Opacity = 1;
                Tint(lampBrush, "#22C55E", 300); Fade(redGlow, 0, 300); Fade(greenGlow, 0.55, 300);
                temp(0.35, 1000, 0); cool();
            });
            sc.At(3700, () => spinDown(1100));
            sc.At(4100, () => glints(4, 0));
        } else {
            sc.At(200, () => {
                lampBrush.Color = C("#EF4444"); blink(9, 380);
                temp(1.0, 900, 0); bulbGlow.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(400)) { AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(4) });
                halo.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            });
            sc.At(1100, () => {                    // jammed: the blades only shudder
                pullBy(18);
                rot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-8, 8, TimeSpan.FromMilliseconds(50)) { AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(4), BeginTime = TimeSpan.FromMilliseconds(250) });
                Shake(plateMove, 4, 5);
            });
            sc.At(1350, () => {                    // the clunk: a few sparks off the hub
                for (int k = 0; k < 3; k++) { var s = Star(7, "#FDE68A"); Add(sc, art, At(s, cx - 20 + k * 20, cy - 8 + (k % 2) * 16)); Glint(s, k * 70); }
            });
            sc.At(1850, () => S2(plateScale, A(null, 1.025, 150, QOut)));     // anticipation: the plate swells a hair before the bang (L2)
            sc.At(2000, () => {                    // the bang: squash and spring back, an impact star and a shock ring
                S2(plateScale, K(0, 1.025, 70, 0.955, 240, 1.02, 420, 1));
                var hit = Star(24, "#FDE68A"); Add(sc, art, At(hit, cx + 120, cy - 120)); Glint(hit, 0);
                var ring = FanOval(cx - 110, cy - 110, 220, 220, null, Br("#FDE68A", 230), 3.5); ring.Opacity = 0; ring.RenderTransform = new WM.ScaleTransform(0.7, 0.7, 110, 110);
                Add(sc, art, ring);
                ring.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 60, 0.9, 520, 0)); S2((WM.ScaleTransform)ring.RenderTransform, A(0.7, 1.28, 520, COut));
                for (int k = 0; k < 5; k++) {
                    double a = (-80 + k * 35) * Math.PI / 180; var sp = Star(5, "#FDE68A"); sp.Opacity = 1;
                    var t = new WM.TranslateTransform(); var g = new WM.TransformGroup(); g.Children.Add(sp.RenderTransform); g.Children.Add(t); sp.RenderTransform = g;
                    Add(sc, art, At(sp, cx + 120, cy - 120));
                    t.BeginAnimation(WM.TranslateTransform.XProperty, A(0, Math.Cos(a) * 46, 400, COut)); t.BeginAnimation(WM.TranslateTransform.YProperty, A(0, Math.Sin(a) * 46 + 14, 400, QOut));
                    sp.BeginAnimation(W.UIElement.OpacityProperty, A(1, 0, 400, QIn, 100));
                }
            });
            sc.At(2400, () => spinUpTo(900, 900));
            sc.At(3300, () => spinFast(per));
            sc.At(3000, () => suck(70, 700));
            sc.At(3600, () => {                    // ice burst
                lampBrush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null); lamp.BeginAnimation(W.UIElement.OpacityProperty, null); lamp.Opacity = 1; Tint(lampBrush, "#334155", 300);
                redGlow.BeginAnimation(W.UIElement.OpacityProperty, null); Fade(redGlow, 0, 300);
                halo.BeginAnimation(W.UIElement.OpacityProperty, null);
                Fade(frost, 0.75, 500); Fade(bulbGlow, 0, 400);
                temp(0.2, 1200, 0); cool();
                var flash = FanOval(cx - 150, cy - 150, 300, 300, FanRad(0.5, 0.5, "#E6FFFFFF", "#667DD3FC", "#007DD3FC")); flash.Opacity = 0; Add(sc, art, flash);
                flash.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 90, 0.9, 520, 0));
                for (int k = 0; k < 8; k++) {
                    double a = k * Math.PI / 4;
                    var sh = FanPath("M0,-15 L5,0 L0,15 L-5,0 Z", FanLin(0, 0, 1, 1, "#FFFFFFFF", "#FF7DD3FC", "#FF0EA5E9"), Br("#E0F2FE", 200), 0.8);
                    var rt = new WM.RotateTransform(a * 180 / Math.PI + 90); var t = new WM.TranslateTransform(); var g = new WM.TransformGroup(); g.Children.Add(rt); g.Children.Add(t); sh.RenderTransform = g;
                    Add(sc, art, At(sh, cx, cy));
                    t.BeginAnimation(WM.TranslateTransform.XProperty, A(0, Math.Cos(a) * 150, 700, COut));
                    t.BeginAnimation(WM.TranslateTransform.YProperty, A(0, Math.Sin(a) * 150 - 20, 700, QOut));   // a slight lift: an arc, not a line
                    rt.BeginAnimation(WM.RotateTransform.AngleProperty, A(null, rt.Angle + (k % 2 == 0 ? 140 : -140), 700, QOut));     // tumbling in flight
                    sh.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, 350, 1, 700, 0));
                }
            });
            sc.At(5300, () => spinDown(1100));
            sc.At(5800, () => glints(4, 0));
        }
        sc.Root = art;
        return sc;
    }

}
