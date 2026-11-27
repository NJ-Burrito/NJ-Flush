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

// Overlay: four flush tiers by RAM in use when the flush starts (Nader 2026-11-23): 70 light, 80 medium, 90 heavy, 96 clogged.
// Motion built on nj-ui-motion-laws (L1 squash, L2 anticipation, L3 one motion at a time, L5 follow-through, L6 easing, L7 arcs,
// L8 secondary action, L10 exaggerated feedback, L26 peak-end) and nj-animation-direction (beats = pressure changes, holds never frozen).
// Sound (nj-real-sound): one premixed soundtrack per tier from real Mixkit recordings (make_sfx.py), started with the clock, so every
// sound sits on its cue. Change a cue time here -> change it in make_sfx.py.
// Windows "Show animations" off -> no motion, the tier and the result still show (reduced motion keeps the feedback).
static partial class Overlay {
    public static int FPS = 30;
    public static bool Solid;  // opaque backdrop: screen recordings never show the desktop behind
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr device, uint volume);  // this process's volume (SoundPlayer)
    // the art (tank, bowl, water, plunger, the bathroom niche): ToiletArt.cs
    static readonly string[] Names = { "LIGHT FLUSH", "MEDIUM FLUSH", "HEAVY FLUSH", "CLOGGED" };
    static readonly string[] Files = { "flush-light.wav", "flush-medium.wav", "flush-heavy.wav", "flush-clogged.wav" };
    static readonly string[] Accents = { "#22D3EE", "#2DD4BF", "#FB923C", "#F87171" };
    static readonly int[] PreviewPct = { 72, 84, 93, 97 };
    // water and button colours: { in, mid, out }
    static readonly string[] Clean = { "#CFFAFE", "#22D3EE", "#0E7490" }, Murky = { "#FFEDD5", "#FB923C", "#9A3412" }, Red = { "#FEE2E2", "#F87171", "#7F1D1D" };

    // ponytail: fixed thresholds (Nader 2026-11-23); under 70% still plays the light flush
    public static int TierOf(double pct) { return pct >= 96 ? 3 : pct >= 90 ? 2 : pct >= 80 ? 1 : 0; }

    static WA.EasingFunctionBase E(WA.EasingFunctionBase e, WA.EasingMode m) { e.EasingMode = m; return e; }
    static readonly WA.IEasingFunction QIn = E(new WA.QuadraticEase(), WA.EasingMode.EaseIn), QInOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseInOut),
        QOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseOut),
        CIn = E(new WA.CubicEase(), WA.EasingMode.EaseIn), COut = E(new WA.CubicEase(), WA.EasingMode.EaseOut),
        Back = new WA.BackEase { Amplitude = 0.5, EasingMode = WA.EasingMode.EaseOut };

    // preview: "light" | "medium" | "heavy" | "clogged" plays that tier without flushing anything
    public static void Run(string preview, Flush job) {
        var ms = new Mem.MemStatus(); Mem.Status(ref ms);
        double pct = ms.total > 0 ? (ms.total - ms.avail) * 100.0 / ms.total : 0;
        int tier = TierOf(pct);
        string result = null;
        Thread work = null;
        if (preview != null) {
            int i = Array.FindIndex(Names, x => x.StartsWith(preview.ToUpperInvariant()));
            tier = i >= 0 ? i : 1; pct = PreviewPct[tier];
            result = Tx.T("ov.preview");
        } else {
            // 1.2.5 (Nader): the Explorer restart happens while the overlay is up; the overlay stays until the whole flush is done
            work = new Thread(() => { result = job.Run(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        }

        // 1.2.2 (super light): animations tick at FPS, not WPF's default 60
        WA.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(WA.Timeline), new W.FrameworkPropertyMetadata { DefaultValue = FPS });
        var win = (W.Window)W.Markup.XamlReader.Parse(Xaml);
        Func<string, object> n = win.FindName;
        BuildSet(n);
        var root = (W.UIElement)n("Root");
        var pop = (WM.ScaleTransform)n("Pop"); var btn = (WM.ScaleTransform)n("BtnScale"); var shake = (WM.TranslateTransform)n("Shake");
        var drain = (WM.ScaleTransform)n("DrainScale"); var swScale = (WM.ScaleTransform)n("SwScale"); var swRot = (WM.RotateTransform)n("SwRot");
        var lvl = (WM.ScaleTransform)n("WaterLvl");
        var water = new[] { (WM.GradientStop)n("WaterIn"), (WM.GradientStop)n("WaterMid"), (WM.GradientStop)n("WaterOut") };
        var btnHi = (WM.GradientStop)n("BtnHi"); var btnLo = (WM.GradientStop)n("BtnLo");
        var rise = (W.UIElement)n("Rise"); var riseScale = (WM.ScaleTransform)n("RiseScale");
        var plunger = (W.UIElement)n("Plunger"); var plY = (WM.TranslateTransform)n("PlY"); var cup = (WM.ScaleTransform)n("Cup");
        var bubbles = (WC.Canvas)n("Bubbles"); var splash = (WC.Canvas)n("Splash");
        var ripples = (WC.Canvas)n("Ripples"); var caustics = (WC.Canvas)n("Caustics");
        var rim = (WS.Ellipse)n("RimWash"); var spark = (W.UIElement)n("Spark"); var sparkS = (WM.ScaleTransform)n("SparkS");
        var duckS = (WM.ScaleTransform)n("DuckS"); var duckR = (WM.RotateTransform)n("DuckR"); var duckT = (WM.TranslateTransform)n("DuckT");
        var glints = (WC.Canvas)n("Glints"); var puddle = (W.UIElement)n("Puddle"); var puddleS = (WM.ScaleTransform)n("PuddleS");
        var alarm = (W.UIElement)n("Alarm"); var flash = (W.UIElement)n("Flash"); var stink = (WC.Canvas)n("Stink");
        var tpRot = (WM.RotateTransform)n("TpRot"); var leafRot = (WM.RotateTransform)n("LeafRot");
        var msg = (WC.TextBlock)n("Msg"); var msgScale = (WM.ScaleTransform)n("MsgScale");
        var tierText = (WC.TextBlock)n("Tier"); var pill = (WC.Border)n("Pill");
        AddArms((WC.Canvas)n("Swirl"));
        if (Solid) {  // recordings: full screen, opaque backdrop and glow
            foreach (string k in new[] { "Dim", "GlowDisc" }) { var u = (W.UIElement)n(k); u.Visibility = W.Visibility.Visible; u.Opacity = 1; }
            win.SizeToContent = W.SizeToContent.Manual; win.WindowState = W.WindowState.Maximized;
        }
        bool render = Scenes.RenderDir != null;   // /render light|medium|heavy|clogged <dir>: off screen, no sound, frames + a sheet
        if (render) {
            win.WindowStartupLocation = W.WindowStartupLocation.Manual; win.Left = -32000; win.Top = 0; win.Topmost = false; win.ShowActivated = false;
            ((WC.Grid)root).Background = new WM.SolidColorBrush(C("#05080F"));
        }
        msg.Text = Tx.T("ov.ram");
        if (tier == 3) msg.Text = Tx.T("ov.pipes"); else if (job != null && job.Req.Deep) msg.Text = Tx.T("ov.deep");
        ((WC.Grid)root).FlowDirection = Tx.Flow;                                           // the pill and the lines read from the right in Arabic
        ((WC.Viewbox)n("Box")).FlowDirection = W.FlowDirection.LeftToRight;              // the drawing is never mirrored
        bool motion = W.SystemParameters.ClientAreaAnimation;

        string accent = Accents[tier];
        tierText.Text = Tx.F("pill.tier", Tx.TierPill(tier), pct);
        tierText.Foreground = new WM.SolidColorBrush(C(accent));
        pill.Background = new WM.SolidColorBrush(C("#E00B1220"));  // dark pill: readable on any desktop
        pill.BorderBrush = new WM.SolidColorBrush(Alpha(accent, 0x88));
        ((WM.GradientStop)n("Glow")).Color = Alpha(accent, 0x40);
        string[] btnCol = tier == 2 ? Murky : tier == 3 ? Red : Clean;
        btnHi.Color = C(btnCol[0]); btnLo.Color = C(btnCol[2]);
        Action<string[], int> tintWater = (cols, t) => { for (int i = 0; i < 3; i++) Tint(water[i], cols[i], t); };
        string dropCol = tier == 2 ? "#FDBA74" : tier == 3 ? "#FCA5A5" : "#7DD3FC";
        if (tier >= 2) {   // the floor puddle takes the colour of the water that spills
            ((WM.GradientStop)n("PudA")).Color = Alpha(tier == 2 ? "#FB923C" : "#DC2626", 0x90); ((WM.GradientStop)n("PudB")).Color = Alpha(tier == 2 ? "#FB923C" : "#B91C1C", 0x4C);
        }

        // ambient life: ripples on the surface, caustic light, the paper tail and the leaves in a breath of air (the bowl is never frozen)
        var impact = new System.Collections.Generic.List<Tuple<WS.Ellipse, WM.ScaleTransform>>();
        for (int i = 0; i < 3; i++) {
            var sc = new WM.ScaleTransform(0.2, 0.2, 86, 108);
            var e = new WS.Ellipse { Width = 172, Height = 216, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 150)), StrokeThickness = 2, Opacity = 0, RenderTransform = sc };
            ripples.Children.Add(e);
            if (!motion) continue;
            var begin = Ms(i * 900);
            sc.BeginAnimation(WM.ScaleTransform.ScaleXProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            sc.BeginAnimation(WM.ScaleTransform.ScaleYProperty, new WA.DoubleAnimation(0.2, 1, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = COut });
            e.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.55, 0, Ms(2700)) { BeginTime = begin, RepeatBehavior = WA.RepeatBehavior.Forever });
        }
        for (int i = 0; i < 3; i++) {   // impact rings (press contact, plunger contact), driven by cues
            var sc = new WM.ScaleTransform(0.1, 0.1, 86, 108);
            var e = new WS.Ellipse { Width = 172, Height = 216, Stroke = new WM.SolidColorBrush(Alpha("#FFFFFF", 220)), StrokeThickness = 2.6, Opacity = 0, RenderTransform = sc };
            ripples.Children.Add(e); impact.Add(Tuple.Create(e, sc));
        }
        string[] cau = { "M26,74 C46,58 70,90 94,74 S134,62 150,78", "M30,126 C54,112 76,140 100,124 S138,114 152,132", "M44,168 C62,154 82,178 104,164 S128,156 138,168", "M58,96 C74,86 88,104 104,94", "M22,150 C34,142 46,156 60,148" };
        for (int i = 0; i < cau.Length; i++) {
            var tt = new WM.TranslateTransform();
            var p = Ln(cau[i], Brs("#FFFFFF", (byte)(110 - i * 10)), 1.4 + (i % 2) * 0.6); p.RenderTransform = tt; caustics.Children.Add(p);
            if (motion) {
                tt.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-5, 6, Ms(1300 + i * 260)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
                tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(-2, 3, Ms(1700 + i * 190)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            }
        }
        if (motion) {
            tpRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-3.5, 3.5, Ms(1500)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            leafRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(-2.5, 2.5, Ms(2100)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
        }
        int ringIdx = 0;
        Action ring = () => {
            var r = impact[ringIdx++ % 3];
            S2(r.Item2, A(0.1, 1, 700, COut)); r.Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0.95, 700, 0));
        };

        // droplets (L8 secondary action) fly out on arcs (L7), thrown from the bowl: a pool of 16, used n at a time
        var drops = new System.Collections.Generic.List<Tuple<WS.Path, WM.TranslateTransform>>();
        for (int i = 0; i < 16; i++) {
            var t = new WM.TranslateTransform();
            var tf = new WM.TransformGroup(); double sz = 0.7 + i % 3 * 0.22; tf.Children.Add(new WM.ScaleTransform(sz, sz)); tf.Children.Add(t);
            var e = new WS.Path {
                Data = WM.Geometry.Parse("M5,0 C5,0 10,7 10,11 A5,5 0 1 1 0,11 C0,7 5,0 5,0 Z"), Opacity = 0, RenderTransform = tf,
                Fill = Rad(0.35, 0.6, 0.0, "#FFFFFF", 1.0, dropCol)
            };
            splash.Children.Add(e); drops.Add(Tuple.Create(e, t));
        }
        // the crown of water on a big burst
        var crownS = new WM.ScaleTransform(0.3, 0.1, 0, 6);
        var crown = Pth("M-50,6 C-44,-14 -38,-38 -32,-52 C-26,-36 -18,-22 -13,-6 C-11,-28 -6,-56 0,-76 C6,-56 11,-28 13,-6 C18,-22 26,-36 32,-52 C38,-38 44,-14 50,6 Z",
            Grad(new W.Point(0, 0), new W.Point(0, 1), Alpha("#FFFFFF", 0xF2), Alpha(dropCol, 0xB8)));
        crown.Opacity = 0; crown.RenderTransform = crownS; crown.Stroke = Brs("#FFFFFF", 150); crown.StrokeThickness = 1;
        splash.Children.Add(At(crown, 0, -12));
        Action<int, double, double> burst = (cnt, spread, up) => {
            for (int i = 0; i < cnt && i < drops.Count; i++) {
                double f = cnt < 2 ? 0 : (i / (double)(cnt - 1)) * 2 - 1;
                double dx = f * spread * (0.75 + 0.25 * ((i * 7) % 4) / 3.0), pk = up * (0.65 + 0.35 * ((i * 5) % 3) / 2.0);
                int dur = 640 + (i % 4) * 70;
                var y = new WA.DoubleAnimationUsingKeyFrames();
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(-pk, WA.KeyTime.FromTimeSpan(Ms(dur * 0.42)), COut));
                y.KeyFrames.Add(new WA.EasingDoubleKeyFrame(60, WA.KeyTime.FromTimeSpan(Ms(dur)), QIn));
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.YProperty, y);
                drops[i].Item2.BeginAnimation(WM.TranslateTransform.XProperty, A(0, dx, dur, COut));
                drops[i].Item1.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, dur * 0.7, 1, dur, 0));
            }
        };
        Action bigBurst = () => {
            burst(16, 135, 105);
            S2(crownS, K(0, 0.3, 150, 1.35, 300, 1.2));
            crownS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 0.1, 150, 1.7, 300, 1.4, 520, 0.3));
            crown.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 40, 0.6, 300, 0.5, 520, 0));
            flash.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 50, tier == 3 ? 0.42 : 0.3, 420, 0));
            Shake(shake, 3, 6);
        };

        // clogged: glossy bubbles rise through the red water and pop on the sound cues (eight pops, one failed-flush glug)
        var bubs = new System.Collections.Generic.List<Bub>();
        double[] bx = { 52, 108, 78, 124, 44, 96, 64, 114, 86 };
        for (int i = 0; i < 9; i++) {
            double d = i == 8 ? 34 : 13 + (i % 3) * 5;
            var body = new WC.Canvas { Width = d, Height = d, Opacity = 0, RenderTransformOrigin = new W.Point(0.5, 0.5) };
            var s = new WM.ScaleTransform(0.3, 0.3); var tr = new WM.TranslateTransform(0, 150); var tg = new WM.TransformGroup(); tg.Children.Add(s); tg.Children.Add(tr); body.RenderTransform = tg;
            var b = new WS.Ellipse { Width = d, Height = d, Stroke = Brs("#FFFFFF", 210), StrokeThickness = 1.2, Fill = Rad(0.32, 0.28, 0.0, "#44FFFFFF", 0.6, "#26FECACA", 1.0, "#88FFFFFF") };
            body.Children.Add(b); body.Children.Add(At(new WS.Ellipse { Width = d * 0.34, Height = d * 0.22, Fill = Brs("#FFFFFF", 235) }, d * 0.2, d * 0.14));
            var rs = new WM.ScaleTransform(0.4, 0.4);
            var rg = new WS.Ellipse { Width = d * 1.6, Height = d * 1.6, Stroke = Brs("#FFFFFF", 230), StrokeThickness = 1.6, Opacity = 0, RenderTransformOrigin = new W.Point(0.5, 0.5), RenderTransform = rs };
            double x = i == 8 ? 86 - d / 2 : bx[i];
            bubbles.Children.Add(At(body, x, 0)); bubbles.Children.Add(At(rg, x - d * 0.3, 70 - d * 0.3));
            bubs.Add(new Bub { Body = body, T = tr, S = s, Ring = rg, RS = rs, X = x });
        }
        // a bubble swells from the bottom, reaches the surface and pops exactly `rise` designed ms after it is started
        Action<int, int, double> popBub = (idx, rdur, topY) => {
            var b = bubs[idx]; b.Body.Opacity = 1;
            b.T.BeginAnimation(WM.TranslateTransform.YProperty, A(b.Body.Height > 30 ? 100 : 190, topY, rdur, QIn));
            S2(b.S, K(0, 0.35, rdur, 1, rdur + 36, 1.35, rdur + 90, 0.2));
            b.Body.BeginAnimation(W.UIElement.OpacityProperty, K(0, 1, rdur, 1, rdur + 50, 0));
            WC.Canvas.SetTop(b.Ring, topY + b.Body.Height * 0.5 - b.Ring.Height * 0.5 + 0);
            S2(b.RS, KD(rdur, 0, 0.5, 330, 1.5));
            b.Ring.BeginAnimation(W.UIElement.OpacityProperty, KD(rdur, 0, 0.95, 40, 0.8, 330, 0));
        };

        // clogged: red murk; steam-stink wisps on the heavy tier
        for (int i = 0; i < 3; i++) {
            var wisp = new WC.Canvas { RenderTransform = new WM.TranslateTransform() };
            string d = "M0,0 C9,-12 -9,-24 0,-36 C9,-48 -9,-60 0,-72";
            wisp.Children.Add(Ln(d, Brs("#3F6212", 130), 6)); wisp.Children.Add(Ln(d, Brs("#BEF264", 225), 3.2));
            stink.Children.Add(At(wisp, 190 + i * 30, 250 - (i % 2) * 14));
        }
        Action stinkOn = () => {
            stink.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 400, null));
            int i = 0;
            foreach (WC.Canvas w in stink.Children) {
                var t = (WM.TranslateTransform)w.RenderTransform;
                t.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(0, -34, Ms(1100 + i * 140)) { BeginTime = Ms(i * 260), RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QOut });
                var ok = K(0, 0, 250, 1, 1100 + i * 140, 0); ok.BeginTime = Ms(i * 260); ok.RepeatBehavior = WA.RepeatBehavior.Forever;
                w.BeginAnimation(W.UIElement.OpacityProperty, ok);
                i++;
            }
        };
        Action stinkOff = () => stink.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 500, null));

        // glints at the end of a flush (peak-end): three small four-point stars on the clean porcelain
        var gl = new System.Collections.Generic.List<Tuple<WS.Path, WM.ScaleTransform>>();
        double[][] gpos = { new double[] { 62, 58 }, new double[] { 60, 200 }, new double[] { 238, 252 } };
        foreach (double[] gp in gpos) {
            var st = new WM.ScaleTransform(0, 0);
            var s = Pth("M0,-10 L2.4,-2.4 L10,0 L2.4,2.4 L0,10 L-2.4,2.4 L-10,0 L-2.4,-2.4 Z", Brs("#FFFFFF", 250)); s.Opacity = 0; s.RenderTransform = st;
            glints.Children.Add(At(s, gp[0], gp[1])); gl.Add(Tuple.Create(s, st));
        }
        Action glint = () => {
            for (int i = 0; i < gl.Count; i++) {
                S2(gl[i].Item2, KD(i * 130, 0, 0.2, 120, 1.15, 380, 0.3));
                gl[i].Item1.BeginAnimation(W.UIElement.OpacityProperty, KD(i * 130, 0, 0, 80, 1, 380, 0));
            }
        };

        System.Media.SoundPlayer sound = null;
        if (Settings.Sound && !render) try { sound = new System.Media.SoundPlayer(Assembly.GetExecutingAssembly().GetManifestResourceStream(Files[tier])); sound.Load(); } catch (Exception) { sound = null; }

        // the duck hops (anticipation, jump, landing squash, settle)
        Action<double> duckHop = h => {
            duckT.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 70, 1.5, 200, -h, 330, 0, 400, -h * 0.2, 470, 0));
            duckS.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 70, 0.86, 200, 1.12, 330, 0.88, 400, 1));
            duckS.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 70, 1.08, 200, 0.94, 330, 1.1, 400, 1));
            duckR.BeginAnimation(WM.RotateTransform.AngleProperty, K(0, 0, 200, -7, 330, 6, 470, 0));
        };
        Action<int> duckWobble = dur => duckR.BeginAnimation(WM.RotateTransform.AngleProperty,
            new WA.DoubleAnimation(-6, 6, Ms(150)) { AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(Math.Max(1, dur / 300)), FillBehavior = WA.FillBehavior.Stop, EasingFunction = QInOut });
        // press: wind-up (L2), squash (L1), settle (L5); the contact (cue + 140) throws a spark and a ring on the water
        Action<double> pressH = h => {
            S2(btn, K(0, 1, 70, 1.12, 200, 0.68, 420, 1));
            spark.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 130, 0, 150, 1, 320, 0));
            S2(sparkS, K(0, 0.3, 150, 1.15, 320, 0.5));
            duckHop(h);
        };
        // flush: swirl, hole, water level drop, the rim wash whirling round (all on the same beat)
        Action<int, int, double, double, double, double> flush = (deg, dur, swirlTo, hole, lvlTo, turns) => {
            swRot.BeginAnimation(WM.RotateTransform.AngleProperty, A(0, deg, dur, QIn));
            S2(swScale, A(1, swirlTo, dur, CIn));
            S2(drain, A(0.3, hole, dur, QIn));
            S2(lvl, A(1, lvlTo, dur, QIn));
            rim.BeginAnimation(W.UIElement.OpacityProperty, K(0, 0, 140, tier == 0 ? 0.55 : 0.95, dur - 360, tier == 0 ? 0.55 : 0.95, dur, 0));
            rim.BeginAnimation(WS.Shape.StrokeDashOffsetProperty, A(0, -163 * turns, dur, QIn));
            duckWobble((int)(dur * Speed));
        };
        Action refill = () => { S2(swScale, A(null, 1, 560, Back)); S2(drain, A(null, 0.3, 560, COut)); S2(lvl, A(null, 1, 560, Back)); glint(); };
        Action<string> say = s => msg.Text = s;

        var cues = new System.Collections.Generic.List<Tuple<int, Action>>();
        Action<int, Action> at = (t, a) => cues.Add(Tuple.Create((int)(t * Speed), a));
        int endAt;
        if (!motion) endAt = 400;
        else if (tier == 0) {           // light: one press, a half drain, quick refill
            at(420, () => pressH(6));
            at(560, ring);              // press contact = cue + 140 (the click sound sits here)
            at(600, () => flush(720, 1500, 0.4, 1.2, 0.93, 1.3));
            at(2150, () => { refill(); burst(3, 20, 18); });
            endAt = 2750;
        } else if (tier == 1) {         // medium: the full swirl
            at(450, () => pressH(8));
            at(590, ring);
            at(620, () => flush(1440, 2500, 0.08, 1.7, 0.86, 3));
            at(3150, () => { refill(); burst(5, 36, 30); });
            endAt = 3750;
        } else if (tier == 2) {         // heavy: murky water, two presses, the bowl shakes, big splash
            at(250, () => { tintWater(Murky, 450); stinkOn(); });
            at(450, () => pressH(8));
            at(590, ring);
            at(760, () => pressH(13));
            at(900, ring);
            at(950, () => { flush(2520, 2900, 0.05, 2.0, 0.8, 6); Shake(shake, 3, 26); });
            at(3250, () => { bigBurst(); S2(puddleS, A(0, 1, 520, COut)); });
            at(3900, () => { refill(); tintWater(Clean, 600); stinkOff(); puddle.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 600, null)); });
            endAt = 4500;
        } else {                        // clogged: water rises, the press fails, plunger x3, then it bursts free
            at(200, () => {
                tintWater(Red, 500);
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 900, COut));
                S2(riseScale, A(0.8, 1, 900, COut));
                alarm.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0.1, 0.8, Ms(520)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut });
            });
            // bubble pops, one per sound cue (bubbling, water rising): started 260 ms early so each pops ON its cue
            int[] pops = { 300, 850, 1350, 1950, 2500, 3050, 3600, 4050 };
            for (int k = 0; k < pops.Length; k++) {
                int kk = k, t0 = pops[k];
                at(t0 - 260, () => popBub(kk, 260, 52 + (kk % 3) * 10));
            }
            at(1100, () => { say(Tx.T("ov.clogged")); S2(riseScale, new WA.DoubleAnimation(1, 1.035, Ms(450)) { AutoReverse = true, RepeatBehavior = WA.RepeatBehavior.Forever, EasingFunction = QInOut }); });
            at(1400, () => { pressH(5); swRot.BeginAnimation(WM.RotateTransform.AngleProperty, new WA.DoubleAnimation(0, 40, Ms(220)) { AutoReverse = true, EasingFunction = COut }); Shake(shake, 4, 5); });
            at(1540, ring);             // press contact: nothing drains
            at(1400, () => popBub(8, 240, 44));    // the failed flush: one big glug, popping on its sound cue (1640)
            at(1640, () => { S2(riseScale, K(0, 1.04, 110, 1.075, 330, 1.035)); burst(4, 26, 26); });
            at(2050, () => {
                say(Tx.T("ov.plunge"));
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 150, null));
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(-540, 0, 520, Back));
            });
            int hit = 0; double[] pud = { 0.35, 0.65, 0.95 };
            foreach (int t in new[] { 2700, 3250, 3800 }) {
                int tt = t, hh = hit++;
                at(tt, () => {
                    plY.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 90, -14, 240, 26, 440, 0));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleYProperty, K(0, 1, 90, 1.05, 240, 0.55, 440, 1));
                    cup.BeginAnimation(WM.ScaleTransform.ScaleXProperty, K(0, 1, 90, 0.95, 240, 1.25, 440, 1));
                    Shake(shake, 2, 2);
                });
                at(tt + 240, () => {    // the squish sound sits here: the cup bottoms out, water squirts, the surface jolts
                    ring(); burst(6, 52, 42); duckHop(9);
                    S2(riseScale, K(0, 1.03, 90, 1.07, 300, 1.03));
                    S2(puddleS, A(null, pud[hh], 380, QOut));
                });
            }
            at(4400, () => {
                say(Tx.T("ov.ram"));
                plY.BeginAnimation(WM.TranslateTransform.YProperty, A(null, -540, 420, CIn));
                plunger.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 420, CIn));
                rise.BeginAnimation(W.UIElement.OpacityProperty, A(null, 0, 350, null));
                S2(riseScale, A(null, 0.8, 350, CIn));
                alarm.BeginAnimation(W.UIElement.OpacityProperty, A(0, 0, 350, null));
                bigBurst();
                flush(2880, 2400, 0.04, 2.2, 0.78, 5);
                S2(puddleS, A(null, 0, 2400, CIn));
                tintWater(Clean, 2400);
                Tint(btnHi, Clean[0], 2400); Tint(btnLo, Clean[2], 2400);
            });
            at(6850, refill);
            endAt = 7450;
        }
        var ordered = cues.OrderBy(c => c.Item1).ToList(); cues.Clear(); cues.AddRange(ordered);   // stable: cues fire in time order

        bool started = false;
        Action startWork = () => { if (work != null && !started) { started = true; work.Start(); } };
        var clock = new Stopwatch();
        int next = 0, stage = 0; long shownAt = 0;
        var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var shots = new System.Collections.Generic.List<WM.Imaging.BitmapSource>(); int shot = 0;
        timer.Tick += delegate {
            long t = clock.ElapsedMilliseconds;
            if (render && Scenes.RenderFps > 0) {   // NJFLUSH_RENDER_FPS: every frame, for video
                Scenes.SeqShot((W.FrameworkElement)root, t, preview ?? "ram");
                if (stage >= 1 && t >= shownAt + 1500) { win.Close(); Scenes.FlushSeq(); return; }
            } else if (render) {   // 8 frames across the choreography, the 9th once the result has landed
                long len = (long)(endAt * Speed);
                bool take = shot < 8 ? t >= len * (shot + 1) / 8 : stage >= 1 && t >= shownAt + 500;
                if (take && shot < 9) {
                    var fe = (W.FrameworkElement)root;
                    var bmp = new WM.Imaging.RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, WM.PixelFormats.Pbgra32);
                    bmp.Render(fe); bmp.Freeze(); shots.Add(bmp); shot++;
                    if (shot == 9) { Scenes.SaveFrames(shots, Scenes.RenderDir, preview ?? "ram"); win.Close(); return; }
                }
            }
            while (next < cues.Count && t >= cues[next].Item1) cues[next++].Item2();
            // 1.2.3: the flush starts with the last cue (the refill), so the motion runs exactly like the preview instead of
            // stuttering while every working set is emptied
            if (next >= cues.Count) startWork();
            long end = (long)(endAt * Speed);
            // the ending lands (L26) only when the flush, Explorer restart included, is done (20 s safety cap; the ripples keep moving)
            if (stage == 0 && next >= cues.Count && t >= end && (result != null || t >= end + 20000)) {
                msg.Text = result ?? Tx.T("ov.finishing"); shownAt = t; stage = 1;
                if (motion) { S2(msgScale, A(0.85, 1, 380, Back)); msg.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 220, null)); }
            } else if (stage == 1 && t >= shownAt + 1300) {
                var fade = new WA.DoubleAnimation(0, Ms(motion ? 350 : 1));
                fade.Completed += delegate { win.Close(); };
                root.BeginAnimation(W.UIElement.OpacityProperty, fade);
                stage = 2;
            }
        };
        // 1.2.1: the clock and the sound start when the first frame is on screen (Loaded fired before it, so a slow first paint
        // under full RAM skipped frames while the sound ran ahead)
        win.ContentRendered += delegate {
            clock.Start(); timer.Start();
            if (sound != null && !render) {    // one soundtrack per tier, cued to this clock, at the user's volume
                uint v = (uint)(Settings.Volume * 0xFFFF / 100); waveOutSetVolume(IntPtr.Zero, v | (v << 16));
                sound.Play();
            }
            root.BeginAnimation(W.UIElement.OpacityProperty, A(null, 1, motion ? 250 : 1, null));
            if (motion) S2(pop, A(0.6, 1, 450, new WA.BackEase { Amplitude = 0.4, EasingMode = WA.EasingMode.EaseOut }));
            else { pop.ScaleX = 1; pop.ScaleY = 1; }
        };
        if (!render) win.MouseDown += delegate { win.Close(); };
        win.KeyDown += (s, e) => { if (e.Key == W.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        timer.Stop(); if (sound != null) sound.Stop();
        startWork();  // closed before it started (a click on the overlay): the flush still runs
        if (work != null) work.Join(20000);  // let the flush finish before the widget starts
    }
}
