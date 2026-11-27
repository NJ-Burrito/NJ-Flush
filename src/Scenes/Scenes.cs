// NJ-Flush 1.3 scenes: CPU (the exhaust fan), GPU (the mirror), All (the shower), each in four variants, plus the short
// "RAM is fine" card. Beats and sound cues: design/1.3-scenes.md (the contract with make_sfx_scenes.py); all times are real ms
// from the first frame. The variant's wav starts with the first frame; after the last beat the real work runs while the scene
// idles; then the result lands. Motion laws (NJ-Kit nj-ui-motion-laws): L2 anticipation before every big move, L3 one primary
// motion at a time, L5 follow-through, L7 arcs, L8 a small flourish last; reduced motion: no movement, the result still shows.
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
    public static bool Solid;
    // /render <variant> <dir>: plays the scene off screen (nothing shows, no sound) and saves 9 frames and a 3x3 contact sheet,
    // so design work can be checked without covering the owner's screen or catching another window in a screenshot
    public static string RenderDir;
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr device, uint volume);

    public static readonly string[] CpuNames = { "Breeze", "Vent", "Overheat", "Meltdown" };
    public static readonly string[] GpuNames = { "Smudge", "Steamy", "Fogged", "Frozen" };
    public static readonly string[] AllNames = { "Rinse", "Shower", "Scrub", "Flood" };
    public static string[] NamesOf(string kind) { return kind == "cpu" ? CpuNames : kind == "gpu" ? GpuNames : AllNames; }
    static readonly int[] CpuLen = { 2600, 3400, 4600, 6400 }, GpuLen = { 2200, 2900, 4000, 5200 }, AllLen = { 2800, 3800, 5200, 6600 };

    // which variant plays (design/1.3-scenes.md): the higher of the two measures, 0..3
    public static int CpuTier(double cpuPct, int busy) { return Math.Max(cpuPct >= 90 ? 3 : cpuPct >= 70 ? 2 : cpuPct >= 40 ? 1 : 0, Math.Min(3, Math.Max(0, busy))); }
    public static int GpuTier(double vramPct) { return vramPct >= 95 ? 3 : vramPct >= 85 ? 2 : vramPct >= 60 ? 1 : 0; }
    public static int AllTier(int ram, int cpu, int gpu) { return Math.Max(ram, Math.Max(cpu, gpu)); }
    // "cpu", "cpu-2", "cpu-meltdown" -> (kind, variant); an unknown variant is 1
    public static bool ParsePreview(string s, out string kind, out int variant) {
        kind = null; variant = 1; s = (s ?? "").ToLowerInvariant();
        string k = s.Split('-')[0];
        if (k != "cpu" && k != "gpu" && k != "all") return false;
        kind = k;
        string v = s.Length > k.Length ? s.Substring(k.Length + 1) : "";
        int n; if (int.TryParse(v, out n) && n >= 0 && n <= 3) variant = n;
        else { int i = Array.FindIndex(NamesOf(k), x => x.ToLowerInvariant() == v); if (i >= 0) variant = i; }
        return true;
    }

    class Scene {
        public double Width = 360, Height = 360;
        public string Pill, Accent, Busy = Tx.T("ov.working"), Sound;
        public readonly List<Tuple<int, Action>> Cues = new List<Tuple<int, Action>>();
        public int Len;
        public Action<long> Hold;   // every frame while the work runs
        public WC.Canvas Root;      // the drawing
        public void At(int ms, Action a) { Cues.Add(Tuple.Create(ms, a)); }
    }

    // ---------- small motion vocabulary ----------
    static WM.Color C(string hex) { return (WM.Color)WM.ColorConverter.ConvertFromString(hex); }
    static WM.Color Alpha(string hex, byte a) { var c = C(hex); c.A = a; return c; }
    static WM.SolidColorBrush Br(string hex) { return new WM.SolidColorBrush(C(hex)); }
    static WM.SolidColorBrush Br(string hex, byte a) { return new WM.SolidColorBrush(Alpha(hex, a)); }
    static WM.LinearGradientBrush Lin(double x1, double y1, double x2, double y2, params string[] stops) {
        var g = new WM.LinearGradientBrush { StartPoint = new W.Point(x1, y1), EndPoint = new W.Point(x2, y2) };
        for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new WM.GradientStop(C(stops[i]), i / (double)(stops.Length - 1)));
        return g;
    }
    static WM.RadialGradientBrush Rad(string inner, string outer) { return new WM.RadialGradientBrush(C(inner), C(outer)); }
    static WA.EasingFunctionBase E(WA.EasingFunctionBase e, WA.EasingMode m) { e.EasingMode = m; return e; }
    static readonly WA.IEasingFunction QIn = E(new WA.QuadraticEase(), WA.EasingMode.EaseIn), QOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseOut),
        QInOut = E(new WA.QuadraticEase(), WA.EasingMode.EaseInOut), CIn = E(new WA.CubicEase(), WA.EasingMode.EaseIn), COut = E(new WA.CubicEase(), WA.EasingMode.EaseOut),
        Back = new WA.BackEase { Amplitude = 0.45, EasingMode = WA.EasingMode.EaseOut };
    static WA.DoubleAnimation A(double? from, double to, int ms, WA.IEasingFunction e = null, int delay = 0) {
        var a = new WA.DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = e, BeginTime = TimeSpan.FromMilliseconds(delay) };
        if (from.HasValue) a.From = from.Value;
        return a;
    }
    // keyframes as (ms, value) pairs, eased in-out
    static WA.DoubleAnimationUsingKeyFrames K(params double[] tv) {
        var k = new WA.DoubleAnimationUsingKeyFrames();
        for (int i = 0; i < tv.Length; i += 2) k.KeyFrames.Add(new WA.EasingDoubleKeyFrame(tv[i + 1], WA.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(tv[i])), QInOut));
        return k;
    }
    static T At<T>(T e, double x, double y) where T : W.UIElement { WC.Canvas.SetLeft(e, x); WC.Canvas.SetTop(e, y); return e; }
    static void S2(WM.ScaleTransform s, WA.AnimationTimeline a) { s.BeginAnimation(WM.ScaleTransform.ScaleXProperty, a); s.BeginAnimation(WM.ScaleTransform.ScaleYProperty, a.Clone()); }
    static void Fade(W.UIElement e, double to, int ms, int delay = 0) { e.BeginAnimation(W.UIElement.OpacityProperty, A(null, to, ms, QInOut, delay)); }
    static void Tint(WM.SolidColorBrush b, string hex, int ms) { b.BeginAnimation(WM.SolidColorBrush.ColorProperty, new WA.ColorAnimation(C(hex), TimeSpan.FromMilliseconds(ms)) { EasingFunction = QInOut }); }
    static void Shake(WM.TranslateTransform t, double amp, int times) {
        t.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(-amp, amp, TimeSpan.FromMilliseconds(45)) { AutoReverse = true, RepeatBehavior = new WA.RepeatBehavior(times), FillBehavior = WA.FillBehavior.Stop });
    }
    static WS.Path Star(double size, string fill) {
        double a = size, b = size * 0.22;
        return new WS.Path { Data = WM.Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "M0,-{0} L{1},-{1} L{0},0 L{1},{1} L0,{0} L-{1},{1} L-{0},0 L-{1},-{1} Z", a, b)), Fill = Br(fill), Opacity = 0, RenderTransform = new WM.ScaleTransform(0.3, 0.3) };
    }
    // a star that flashes and grows a little: the small flourish at the end (L8)
    static void Glint(WS.Path s, int delay) {
        s.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay), KeyFrames = {
            new WA.EasingDoubleKeyFrame(1, WA.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), QOut),
            new WA.EasingDoubleKeyFrame(0, WA.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)), QIn) } });
        S2((WM.ScaleTransform)s.RenderTransform, A(0.3, 1.15, 480, Back, delay));
    }
    // a pull: dips (anticipation), is pulled down, springs back past rest and settles
    static void Tug(WM.TranslateTransform t, double depth) {
        t.BeginAnimation(WM.TranslateTransform.YProperty, K(0, 0, 80, -4, 220, depth, 420, -3, 560, 0));
    }

    const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent' Topmost='True' ShowInTaskbar='False'
        SizeToContent='WidthAndHeight' WindowStartupLocation='CenterScreen' ResizeMode='NoResize'>
  <Grid x:Name='Root' Opacity='0'>
    <Rectangle x:Name='Dim' Fill='#05080F' Visibility='Collapsed'/>
    <StackPanel VerticalAlignment='Center' HorizontalAlignment='Center' Margin='40' Width='560'>
      <Viewbox x:Name='Box' Height='420' RenderTransformOrigin='0.5,0.5'>
        <Viewbox.RenderTransform><ScaleTransform x:Name='Pop' ScaleX='0.6' ScaleY='0.6'/></Viewbox.RenderTransform>
        <Canvas x:Name='Art'/>
      </Viewbox>
      <Border x:Name='Pill' HorizontalAlignment='Center' Margin='0,22,0,0' Padding='16,5' CornerRadius='15' BorderThickness='1' Background='#E00B1220'>
        <TextBlock x:Name='Tier' FontFamily='Thmanyah Sans, Segoe UI' FontSize='15' FontWeight='Bold'/>
      </Border>
      <Border Background='#D90B1220' CornerRadius='14' Padding='18,6' Margin='0,10,0,0' HorizontalAlignment='Center'>
        <TextBlock x:Name='Msg' Foreground='White' FontFamily='Thmanyah Sans, Segoe UI' FontSize='26'
                   HorizontalAlignment='Center' TextAlignment='Center' TextWrapping='Wrap' RenderTransformOrigin='0.5,0.5'>
          <TextBlock.RenderTransform><ScaleTransform x:Name='MsgScale'/></TextBlock.RenderTransform>
        </TextBlock>
      </Border>
    </StackPanel>
  </Grid>
</Window>";

    static readonly string[] TierAccent = { null, null, "#FB923C", "#F87171" };

    // kind: cpu | gpu | all | fine. job: the flush (null in a preview). preview: "cpu-2" and the like, or null.
    public static void Run(string kind, Flush job, string preview) {
        WA.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(WA.Timeline), new W.FrameworkPropertyMetadata { DefaultValue = Overlay.FPS });
        var win = (W.Window)W.Markup.XamlReader.Parse(Xaml);
        Func<string, object> n = win.FindName;
        var art = (WC.Canvas)n("Art"); var msg = (WC.TextBlock)n("Msg"); var tier = (WC.TextBlock)n("Tier"); var pill = (WC.Border)n("Pill");
        var root = (W.UIElement)n("Root"); var pop = (WM.ScaleTransform)n("Pop"); var msgScale = (WM.ScaleTransform)n("MsgScale");
        ((W.FrameworkElement)root).FlowDirection = Tx.Flow;                                          // the pill and the lines read from the right in Arabic
        ((WC.Viewbox)n("Box")).FlowDirection = W.FlowDirection.LeftToRight;                        // the drawing is never mirrored
        if (Solid) { var d = (W.UIElement)n("Dim"); d.Visibility = W.Visibility.Visible; win.SizeToContent = W.SizeToContent.Manual; win.WindowState = W.WindowState.Maximized; }
        bool render = RenderDir != null;
        if (render) {   // off screen, never activated, opaque backdrop so the frames read
            win.WindowStartupLocation = W.WindowStartupLocation.Manual; win.Left = -32000; win.Top = 0; win.Topmost = false; win.ShowActivated = false;
            ((System.Windows.Controls.Grid)root).Background = Br("#05080F");
        }

        int variant = 1; string pk;
        if (preview != null && ParsePreview(preview, out pk, out variant)) kind = pk;
        string result = preview != null ? Tx.T("ov.preview") : null;
        Scene sc;
        if (kind == "fine") sc = Fine(job);
        else {
            if (job != null) {
                double cpu = 0; int busy = 0;
                if (kind != "gpu") { var meter = new CpuMeter(); Thread.Sleep(200); cpu = meter.Sample(); var b = Busy.Read(); busy = b == null ? 0 : b.Count; }
                int ct = CpuTier(cpu, busy), gt = GpuTier(job.VramPct);
                variant = kind == "cpu" ? ct : kind == "gpu" ? gt : AllTier(job.Tier, ct, gt);
                sc = kind == "cpu" ? Fan(variant, Tx.F("ms.cpu", cpu)) : kind == "gpu" ? Mirror(variant, job.HasGpu ? Tx.F("ms.vram", job.VramPct) : Tx.T("ms.gpu"))
                   : Shower(variant, job, Tx.F("ms.ram", job.RamPct));
            } else sc = kind == "cpu" ? Fan(variant, Tx.T("ms.preview")) : kind == "gpu" ? Mirror(variant, Tx.T("ms.preview")) : Shower(variant, null, Tx.T("ms.preview"));
        }
        art.Width = sc.Width; art.Height = sc.Height;
        if (sc.Root != null) art.Children.Add(sc.Root);
        if (kind == "fine") ((WC.Viewbox)n("Box")).Visibility = W.Visibility.Collapsed;
        string accent = kind == "fine" ? sc.Accent : TierAccent[variant] ?? sc.Accent;
        tier.Text = sc.Pill; tier.Foreground = Br(accent); pill.BorderBrush = Br(accent, 0x88);
        msg.Text = sc.Busy;
        sc.Cues.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        System.Media.SoundPlayer sound = null;
        if (Settings.Sound && sc.Sound != null && !render) try {
            var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(sc.Sound);
            if (st != null) { sound = new System.Media.SoundPlayer(st); sound.Load(); }
        } catch (Exception) { sound = null; }

        Thread work = job == null ? null : new Thread(() => { result = job.Run(); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        bool motion = W.SystemParameters.ClientAreaAnimation;
        var clock = new Stopwatch(); int next = 0, stage = 0; long shownAt = 0;
        var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var shots = new List<W.Media.Imaging.BitmapSource>(); int shot = 0;
        timer.Tick += delegate {
            long t = clock.ElapsedMilliseconds;
            if (render && RenderFps > 0) {   // NJFLUSH_RENDER_FPS: every frame, for video (hero, site clips)
                SeqShot((W.FrameworkElement)root, t, preview ?? kind);
                if (stage >= 2 && t >= shownAt + 1500) { win.Close(); FlushSeq(); return; }
            } else if (render) {   // 8 frames across the beats, the 9th once the result has landed
                bool take = shot < 8 ? t >= sc.Len * (shot + 1) / 8 : stage >= 2 && t >= shownAt + 500;
                if (take && shot < 9) {
                    var fe = (W.FrameworkElement)root;
                    var bmp = new W.Media.Imaging.RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, WM.PixelFormats.Pbgra32);
                    bmp.Render(fe); bmp.Freeze(); shots.Add(bmp); shot++;
                    if (shot == 9) { SaveFrames(shots, RenderDir, preview ?? kind); win.Close(); return; }
                }
            }
            if (stage == 0) {                                   // the choreography
                while (motion && next < sc.Cues.Count && t >= sc.Cues[next].Item1) sc.Cues[next++].Item2();
                if (!motion || t >= sc.Len) { if (work != null) work.Start(); stage = 1; }
            }
            if (stage == 1) {                                   // the work runs; the scene idles (30 s safety cap)
                if (sc.Hold != null) sc.Hold(t);
                if (result != null || t >= sc.Len + 30000) {
                    if (sc.Hold != null) sc.Hold(long.MaxValue);
                    msg.Text = result ?? Tx.T("ov.finishing"); shownAt = t; stage = 2;
                    if (motion) { S2(msgScale, A(0.85, 1, 380, Back)); msg.BeginAnimation(W.UIElement.OpacityProperty, A(0, 1, 220)); }
                }
            } else if (stage == 2 && t >= shownAt + 1400 + 450 * msg.Text.Count(ch => ch == '\n')) {   // a longer result stays a little longer
                var fade = new WA.DoubleAnimation(0, TimeSpan.FromMilliseconds(motion ? 350 : 1));
                fade.Completed += delegate { win.Close(); };
                root.BeginAnimation(W.UIElement.OpacityProperty, fade);
                stage = 3;
            }
        };
        win.ContentRendered += delegate {
            clock.Start(); timer.Start();
            if (sound != null && !render) { uint v = (uint)(Settings.Volume * 0xFFFF / 100); waveOutSetVolume(IntPtr.Zero, v | (v << 16)); sound.Play(); }
            root.BeginAnimation(W.UIElement.OpacityProperty, A(null, 1, motion ? 250 : 1));
            if (motion) S2(pop, A(0.6, 1, 450, new WA.BackEase { Amplitude = 0.4, EasingMode = WA.EasingMode.EaseOut })); else { pop.ScaleX = 1; pop.ScaleY = 1; }
        };
        if (!render) win.MouseDown += delegate { win.Close(); };
        win.KeyDown += (s, e) => { if (e.Key == W.Input.Key.Escape) win.Close(); };
        win.ShowDialog();
        timer.Stop(); if (sound != null) sound.Stop();
        if (work != null) {
            if (work.ThreadState == System.Threading.ThreadState.Unstarted) work.Start();   // closed early (a click): the flush still runs
            work.Join(30000);
        }
    }

    // NJFLUSH_RENDER_FPS=30 with /render: <name>-f00000.png ... one file per frame time (index = ms * fps / 1000). A slow frame
    // can skip an index: fill gaps with the previous frame when assembling the video.
    internal static readonly int RenderFps = ParseFps(Environment.GetEnvironmentVariable("NJFLUSH_RENDER_FPS"));
    static int ParseFps(string s) { int n; return int.TryParse(s, out n) && n > 0 && n <= 60 ? n : 0; }
    static int lastSeq = -1;
    static readonly List<Tuple<int, W.Media.Imaging.BitmapSource>> seq = new List<Tuple<int, W.Media.Imaging.BitmapSource>>();
    static string seqName;
    // grab only (cheap) while the scene plays; the PNGs are written once it has closed (FlushSeq)
    internal static void SeqShot(W.FrameworkElement fe, long t, string name) {
        int idx = (int)(t * RenderFps / 1000);
        if (idx == lastSeq) return; lastSeq = idx; seqName = name;
        var bmp = new W.Media.Imaging.RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, WM.PixelFormats.Pbgra32);
        bmp.Render(fe); bmp.Freeze();
        seq.Add(Tuple.Create(idx, (W.Media.Imaging.BitmapSource)bmp));
    }
    // writes every index from the first to the last, repeating the previous frame where a slow tick skipped one
    internal static void FlushSeq() {
        if (seq.Count == 0) return;
        System.IO.Directory.CreateDirectory(RenderDir);
        int k = 0;
        for (int idx = seq[0].Item1; idx <= seq[seq.Count - 1].Item1; idx++) {
            while (k + 1 < seq.Count && seq[k + 1].Item1 <= idx) k++;
            var enc = new W.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(W.Media.Imaging.BitmapFrame.Create(seq[k].Item2));
            using (var f = System.IO.File.Create(System.IO.Path.Combine(RenderDir, seqName + "-f" + idx.ToString("00000") + ".png"))) enc.Save(f);
        }
        seq.Clear();
    }

    internal static void SaveFrames(List<W.Media.Imaging.BitmapSource> shots, string dir, string name) {
        System.IO.Directory.CreateDirectory(dir);
        Action<W.Media.Imaging.BitmapSource, string> save = (b, file) => {
            var enc = new W.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(W.Media.Imaging.BitmapFrame.Create(b));
            using (var f = System.IO.File.Create(System.IO.Path.Combine(dir, file))) enc.Save(f);
        };
        for (int i = 0; i < shots.Count; i++) save(shots[i], name + "-" + (i + 1) + ".png");
        int w = shots[0].PixelWidth, h = shots[0].PixelHeight;
        var dv = new WM.DrawingVisual();
        using (var dc = dv.RenderOpen()) for (int i = 0; i < shots.Count; i++) dc.DrawImage(shots[i], new W.Rect((i % 3) * w, (i / 3) * h, w, h));
        var sheet = new W.Media.Imaging.RenderTargetBitmap(w * 3, h * 3, 96, 96, WM.PixelFormats.Pbgra32); sheet.Render(dv);
        save(sheet, name + "-sheet.png");
    }

    static Scene Fine(Flush job) {
        return new Scene { Width = 10, Height = 10, Accent = "#22D3EE", Busy = Tx.T("ov.checking"), Len = 0,
                           Pill = job == null ? Tx.T("pill.fine") : Tx.F("pill.fine_use", job.RamPct) };
    }

    static void Add(Scene sc, WC.Canvas art, W.UIElement e) { art.Children.Add(e); }
}
