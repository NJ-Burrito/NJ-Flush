using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WS = System.Windows.Shapes;

partial class View {
    static WM.SolidColorBrush B(string hex) { return Theme.Brush(hex); }
    static WM.Color Col(string hex) { return Theme.Col(hex); }
    static readonly WM.Brush Bg = B(Theme.Bg), Card = B(Theme.Surface), Fg = B(Theme.Fg), Muted = B(Theme.Muted), Line = B(Theme.Line), Ink = B(Theme.Ink), Amber = B(Theme.Warn);
    static readonly string[] Pages = { "ram", "cpu", "gpu", "guard", "settings" };
    static readonly string[] IdentHex = Theme.Ident;
    static readonly WM.Brush[] Ident = IdentHex.Select(B).ToArray();
    static WM.Brush Scale(double pct) { string h = Theme.Load(pct); return h == null ? null : B(h); }
    public static bool Still;   // /render-ui: no motion, so the picture is the finished state
    static bool Motion { get { return !Still && W.SystemParameters.ClientAreaAnimation; } }
    const double SparkW = 170, SparkH = 24, GraphW = 606, GraphH = 92;   // tile sparkline (NavW - tile padding and border), page graph (Content - card padding and border)

    public readonly W.Window Window;
    WC.ContentControl page = new WC.ContentControl();
    WC.ScrollViewer scroll;
    WC.TextBlock status, lastLine;
    WC.Border allKey;
    Tile[] tiles = new Tile[5];
    static string Title(int i) { return Tx.T("title." + Pages[i]); }
    int current = -1;
    Action tickPage;
    WC.Panel hoverGuard;
    int ticks;

    class Tile { public WC.Border Box; public WM.SolidColorBrush Back, Edge, Mark; public WC.TextBlock Value, Sub; public WS.Polyline Line; public WS.Polygon Area; }

    // the shared styles (buttons, switches, sliders, scrollbars): ViewStyles.cs
    static WC.TextBlock T(string text, double size, WM.Brush color, bool bold = false) {
        return new WC.TextBlock { Text = Tx.Bidi(text), FontSize = size, Foreground = color, FontWeight = bold ? W.FontWeights.Bold : W.FontWeights.Normal, TextWrapping = W.TextWrapping.Wrap };
    }
    // primary = filled with the page's colour, bold ink on it; the rest are ghost buttons (surface, hairline, lighter on hover). 44 px tall at least (L14)
    static WC.Button Btn(object content, WM.Brush fill, Action act, bool primary = true) {
        var b = new WC.Button { Content = content, Style = (W.Style)Styles[primary ? "Btn" : "Ghost"], Padding = new W.Thickness(20, 10, 20, 10), MinHeight = 44, FontSize = Theme.Strong,
                                Background = primary ? fill : Card, BorderBrush = primary ? fill : Line, Foreground = primary ? Ink : Fg,
                                FontWeight = primary ? W.FontWeights.Bold : W.FontWeights.Normal };
        b.Click += delegate { act(); };
        return b;
    }
    // the shortcut shown on a filled button: a darker plate of the button's own colour
    static WC.Border Kbd(string keys) {
        return new WC.Border { Background = Theme.Brush(WM.Color.FromArgb(0x38, 0, 0, 0)), CornerRadius = new W.CornerRadius(Theme.RSm), Padding = new W.Thickness(8, 2, 8, 3), Margin = new W.Thickness(12, 0, 0, 0),
                               VerticalAlignment = W.VerticalAlignment.Center, FlowDirection = W.FlowDirection.LeftToRight,   // key caps stay left to right
                               Child = new WC.TextBlock { Text = keys, FontSize = Theme.Small, FontWeight = W.FontWeights.Normal, Foreground = Ink } };
    }
    static WC.CheckBox Toggle(string text, bool value, Action<bool> set, WM.Brush on = null) {
        var c = new WC.CheckBox { Content = text, IsChecked = value, Style = (W.Style)Styles["Toggle"], Tag = on ?? Ident[0] };
        c.Click += delegate { set(c.IsChecked == true); Panel.Applied(); };
        return c;
    }
    // a rounded surface that clips its children to the corners (list rows, cards)
    static WC.Border Surface(W.UIElement child, W.Thickness pad) {
        var b = new WC.Border { Background = Card, BorderBrush = Line, BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(Theme.RLg), Padding = pad, Child = child };
        b.SizeChanged += delegate { double r = Theme.RLg - 1; b.Clip = new WM.RectangleGeometry(new W.Rect(0, 0, b.ActualWidth, b.ActualHeight), r + 1, r + 1); };
        return b;
    }

    public View() {
        Window = new W.Window {
            Title = "NJ-Flush", Width = 1000, Height = 740, MinWidth = 980, MinHeight = 560, Background = Bg, Foreground = Fg, FontFamily = NjFont.Wpf(), FontSize = Theme.Strong,
            WindowStartupLocation = W.WindowStartupLocation.CenterScreen, FlowDirection = Tx.Flow
        };
        Window.Resources.MergedDictionaries.Add(Styles);
        try { Window.Icon = WM.Imaging.BitmapFrame.Create(new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location)); } catch (Exception) { }
        Window.SourceInitialized += delegate {   // dark title bar, in the panel's own navy (Windows 11 also takes the colours)
            var h = new W.Interop.WindowInteropHelper(Window).Handle;
            int dark = 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
            int cap = 0x0023170F, txt = 0x00F3EDE6, brd = 0x005F4633;   // COLORREF 0x00BBGGRR of Bg, Fg, LineStrong
            DwmSetWindowAttribute(h, 35, ref cap, 4); DwmSetWindowAttribute(h, 36, ref txt, 4); DwmSetWindowAttribute(h, 34, ref brd, 4);
        };
        Window.KeyDown += (s, e) => {
            if (recording) return;
            if (e.Key == W.Input.Key.Escape) Window.Close();
            else if (W.Input.Keyboard.Modifiers == W.Input.ModifierKeys.None && !(W.Input.Keyboard.FocusedElement is WC.TextBox) &&
                     e.Key >= W.Input.Key.D1 && e.Key <= W.Input.Key.D5) Select(e.Key - W.Input.Key.D1);   // 1-5: pages
        };

        BuildChrome();

        var timer = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += delegate { Tick(); };
        Window.Loaded += delegate { Go(Panel.StartPage); Tick(); timer.Start(); };
        Window.Closed += delegate { timer.Stop(); if (recording) StopRecording(); };
    }

    // the frame: bottom bar, tile column and the page area; built again when the language changes
    void BuildChrome() {
        page = new WC.ContentControl(); tiles = new Tile[5];
        var root = new WC.DockPanel { Background = Bg };
        // bottom bar: what just happened on the left; Flush all, the largest target, on the right
        var bar = new WC.DockPanel();
        var barBorder = new WC.Border { Background = B(Theme.Sidebar), BorderBrush = Line, BorderThickness = new W.Thickness(0, 1, 0, 0), Padding = new W.Thickness(Theme.S5, Theme.S4, Theme.S5, Theme.S4), Child = bar };
        WC.DockPanel.SetDock(barBorder, WC.Dock.Bottom); root.Children.Add(barBorder);
        var allContent = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        allContent.Children.Add(new WC.TextBlock { Text = Tx.T("ui.flush_all"), FontSize = Theme.Title, FontWeight = W.FontWeights.Bold, VerticalAlignment = W.VerticalAlignment.Center });
        allKey = Kbd(""); allContent.Children.Add(allKey);
        var all = Btn(allContent, Ident[0], () => Do("all", false)); all.Padding = new W.Thickness(28, 12, 28, 12); all.MinHeight = 52;
        WC.DockPanel.SetDock(all, WC.Dock.Right); bar.Children.Add(all);
        var left = new WC.StackPanel { VerticalAlignment = W.VerticalAlignment.Center };
        status = T("", Theme.Strong, Fg, true); lastLine = T("", Theme.Small, Muted);
        left.Children.Add(status); left.Children.Add(lastLine); bar.Children.Add(left);

        // left: the name, four tiles, then Settings at the foot
        var nav = new WC.DockPanel { Width = Theme.NavW };
        var navBorder = new WC.Border { Background = B(Theme.Sidebar), BorderBrush = Line, BorderThickness = new W.Thickness(0, 0, 1, 0), Padding = new W.Thickness(Theme.S4), Child = nav };
        WC.DockPanel.SetDock(navBorder, WC.Dock.Left); root.Children.Add(navBorder);
        var brand = new WC.TextBlock { Margin = new W.Thickness(Theme.S1, Theme.S1, 0, Theme.S4) };
        brand.Inlines.Add(new System.Windows.Documents.Run("NJ-Flush") { FontSize = Theme.Title, FontWeight = W.FontWeights.Bold, Foreground = Fg });
        brand.Inlines.Add(new System.Windows.Documents.Run("  " + Updater.Version) { FontSize = Theme.Small, Foreground = B(Theme.Dim) });
        WC.DockPanel.SetDock(brand, WC.Dock.Top); nav.Children.Add(brand);
        var set = MakeTile(4); WC.DockPanel.SetDock(set, WC.Dock.Bottom); nav.Children.Add(set);
        var top = new WC.StackPanel(); nav.Children.Add(top);
        for (int i = 0; i < 4; i++) top.Children.Add(MakeTile(i));

        scroll = new WC.ScrollViewer { VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto, Content = page, Padding = new W.Thickness(Theme.S6, Theme.S5, Theme.S5, Theme.S5) };
        root.Children.Add(scroll);
        Window.Content = root;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    public void Reload() { int keep = Math.Max(0, current); current = -1; Select(keep); Tick(); Say(Tx.T("ui.refreshed")); }
    // the language changed (Settings page): direction, frame and page are built again in the new language
    void Relang() {
        if (recording) StopRecording();
        Window.FlowDirection = Tx.Flow;
        int keep = Math.Max(0, current);
        BuildChrome(); current = -1; Select(keep); Tick();
    }
    public void Go(string name) { Select(Math.Max(0, Array.IndexOf(Pages, (name ?? "").ToLowerInvariant()))); }

    W.UIElement MakeTile(int i) {
        string id = IdentHex[i];
        var t = new Tile { Back = new WM.SolidColorBrush(Col(Theme.Tile)), Edge = new WM.SolidColorBrush(Col(Theme.Line)), Mark = new WM.SolidColorBrush(Theme.Tint(id, 0)) };
        var g = new WC.Grid();
        g.RowDefinitions.Add(new WC.RowDefinition { Height = W.GridLength.Auto }); g.RowDefinitions.Add(new WC.RowDefinition { Height = W.GridLength.Auto }); g.RowDefinitions.Add(new WC.RowDefinition { Height = W.GridLength.Auto });
        var name = T(Title(i), Theme.Body, Fg, true); name.TextWrapping = W.TextWrapping.NoWrap; g.Children.Add(name);
        t.Value = T("", Theme.Strong, Ident[i], true); t.Value.HorizontalAlignment = W.HorizontalAlignment.Right; g.Children.Add(t.Value);
        t.Sub = T("", Theme.Small, Muted); t.Sub.TextWrapping = W.TextWrapping.NoWrap; t.Sub.TextTrimming = W.TextTrimming.CharacterEllipsis; t.Sub.Margin = new W.Thickness(0, 2, 0, 0);
        WC.Grid.SetRow(t.Sub, 1); g.Children.Add(t.Sub);
        if (i < 3) {
            var spark = new WC.Grid { Width = SparkW, Height = SparkH, FlowDirection = W.FlowDirection.LeftToRight, Margin = new W.Thickness(0, Theme.S2, 0, 0), HorizontalAlignment = W.HorizontalAlignment.Left };
            t.Area = new WS.Polygon { Fill = Theme.Brush(Theme.Tint(id, 0x1C)), Margin = new W.Thickness(0, 1, 0, 1), VerticalAlignment = W.VerticalAlignment.Top, HorizontalAlignment = W.HorizontalAlignment.Left };
            t.Line = new WS.Polyline { Stroke = Ident[i], StrokeThickness = 1.5, StrokeLineJoin = WM.PenLineJoin.Round, Margin = new W.Thickness(0, 1, 0, 1), VerticalAlignment = W.VerticalAlignment.Top, HorizontalAlignment = W.HorizontalAlignment.Left };
            spark.Children.Add(t.Area); spark.Children.Add(t.Line);
            WC.Grid.SetRow(spark, 2); g.Children.Add(spark);
        }
        t.Box = new WC.Border { Child = g, Padding = new W.Thickness(Theme.S3 + 2, Theme.S3, Theme.S3 + 2, Theme.S3), CornerRadius = new W.CornerRadius(Theme.RLg),
                                BorderThickness = new W.Thickness(1), BorderBrush = t.Edge, Background = t.Back, Cursor = W.Input.Cursors.Hand };
        var mark = new WC.Border { Width = 3, Height = 28, CornerRadius = new W.CornerRadius(0, 2, 2, 0), Background = t.Mark, HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Center, IsHitTestVisible = false };
        var wrap = new WC.Grid { Margin = new W.Thickness(0, 0, 0, Theme.S2) };
        wrap.Children.Add(t.Box); wrap.Children.Add(mark);
        int idx = i;
        t.Box.MouseLeftButtonUp += delegate { Select(idx); };
        t.Box.MouseEnter += delegate { if (current != idx) Shade(t.Back, Col(Theme.Raised), 120); };
        t.Box.MouseLeave += delegate { if (current != idx) Shade(t.Back, Col(Theme.Tile), 120); };
        tiles[i] = t;
        return wrap;
    }
    static void Shade(WM.SolidColorBrush b, WM.Color c, int ms) {
        if (!Motion) { b.BeginAnimation(WM.SolidColorBrush.ColorProperty, null); b.Color = c; return; }
        b.BeginAnimation(WM.SolidColorBrush.ColorProperty, new WA.ColorAnimation(c, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut } });
    }

    static WM.PointCollection Spark(double[] h, double w, double hgt) {
        var pts = new WM.PointCollection();
        for (int i = 0; i < h.Length; i++) pts.Add(new W.Point(i * w / (h.Length - 1), hgt - Math.Max(0, Math.Min(100, h[i])) / 100 * hgt));
        return pts;
    }
    // the line and the soft area under it (a polygon closed along the bottom)
    static void Plot(WS.Polyline line, WS.Polygon area, double[] h, double w, double hgt) {
        var pts = Spark(h, w, hgt); line.Points = pts;
        if (area == null) return;
        var ap = pts.Clone(); ap.Add(new W.Point(w, hgt)); ap.Add(new W.Point(0, hgt)); area.Points = ap;
    }

    void Select(int i) {
        if (i == current) return;
        if (recording) StopRecording();
        current = i;
        for (int k = 0; k < tiles.Length; k++) {
            Shade(tiles[k].Edge, k == i ? Theme.Tint(IdentHex[k], 0x70) : Col(Theme.Line), 150);
            Shade(tiles[k].Back, k == i ? Theme.Tint(IdentHex[k], 0x22) : Col(Theme.Tile), 150);
            Shade(tiles[k].Mark, Theme.Tint(IdentHex[k], (byte)(k == i ? 0xFF : 0)), 150);
        }
        status.Text = "";
        var body = new WC.StackPanel { Width = Theme.Content, HorizontalAlignment = W.HorizontalAlignment.Left };
        hoverGuard = null; tickPage = null;
        if (i == 0) RamPage(body); else if (i == 1) CpuPage(body); else if (i == 2) GpuPage(body); else if (i == 3) GuardPage(body); else SettingsPage(body);
        page.Content = body;
        scroll.ScrollToTop();
        // the page enters: 200 ms fade and a 10 px rise, eased out (L6, L9)
        if (Motion) {
            var tt = new WM.TranslateTransform(0, 10); body.RenderTransform = tt; body.Opacity = 0;
            var ease = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut };
            body.BeginAnimation(W.UIElement.OpacityProperty, new WA.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            tt.BeginAnimation(WM.TranslateTransform.YProperty, new WA.DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
        if (tickPage != null) tickPage();
    }

    void Tick() {
        ticks++;
        // a panel without a widget answers a double-click on NJ-Flush.exe itself: forward and reload, never a second panel
        if (Widget.Current == null && System.IO.File.Exists(App.PanelRequest)) {
            try { System.IO.File.Delete(App.PanelRequest); } catch (Exception) { }
            if (Window.WindowState == W.WindowState.Minimized) Window.WindowState = W.WindowState.Normal;
            Window.Activate(); Window.Topmost = true; Window.Topmost = false; Reload(); return;
        }
        double ram, cpu, vram, used, total, vu, vt; string gpu;
        lock (Live.L) { ram = Live.Ram; cpu = Live.Cpu; vram = Live.Vram; used = Live.RamUsed; total = Live.RamTotal; vu = Live.VramUsed; vt = Live.VramTotal; gpu = Live.GpuName; }
        tiles[0].Value.Text = Tx.Pct(ram); tiles[0].Sub.Text = Tx.F("tile.ram_sub", used, total); Plot(tiles[0].Line, tiles[0].Area, Live.Ordered(Live.RamH), SparkW, SparkH - 2);
        tiles[1].Value.Text = Tx.Pct(cpu); tiles[1].Sub.Text = Tx.F("threads", Environment.ProcessorCount); Plot(tiles[1].Line, tiles[1].Area, Live.Ordered(Live.CpuH), SparkW, SparkH - 2);
        if (gpu != null) { tiles[2].Value.Text = Tx.Pct(vram); tiles[2].Sub.Text = Tx.F("tile.gpu_sub", vu, vt); Plot(tiles[2].Line, tiles[2].Area, Live.Ordered(Live.VramH), SparkW, SparkH - 2); }
        else { tiles[2].Value.Text = ""; tiles[2].Sub.Text = Tx.T("tile.gpu_wait"); }
        tiles[3].Value.Text = Tx.T(Settings.Guard ? (Guard.Paused ? "tile.guard_paused" : "tile.guard_on") : "tile.guard_off");
        tiles[3].Sub.Text = Settings.Guard ? Tx.F("tile.guard_apps", Settings.GuardApps.Count) : Tx.T("tile.guard_slow");
        tiles[4].Sub.Text = Tx.T("tile.settings_sub"); tiles[4].Value.Text = "";
        for (int k = 0; k < 3; k++) { var s = Scale(k == 0 ? ram : k == 1 ? cpu : vram); tiles[k].Value.Foreground = s ?? Ident[k]; }
        lastLine.Text = Tx.F("last.line", Settings.Last());
        string k0 = Settings.Hotkey ? Settings.Keys_[0] : "";
        ((WC.TextBlock)allKey.Child).Text = k0; allKey.Visibility = k0.Length > 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
        if (tickPage != null) tickPage();
    }

    void Say(string text, WM.Brush color = null) { status.Foreground = color ?? Fg; status.Text = text; }

    // the request: refused with a reason in the bar, or sent with an immediate answer
    void Do(string kind, bool deep) {
        string why = Panel.Refusal(kind, deep);
        if (why != null) { Say(why, Amber); return; }
        Say(Tx.T(kind == "all" ? "do.all" : kind == "ram" ? (deep ? "do.deep" : "do.ram") : kind == "cpu" ? "do.cpu" : "do.gpu"));
        App.FlushNow(kind, false, deep);
    }

    // ---------- page parts ----------
    // title, one line under it, the big value on the right
    WC.TextBlock Header(WC.StackPanel body, int i, string sub, out WC.TextBlock subLine) {
        var head = new WC.DockPanel();
        var big = T("", Theme.Display, Ident[i], true); big.Margin = new W.Thickness(0, -8, 0, 0);
        WC.DockPanel.SetDock(big, WC.Dock.Right); head.Children.Add(big);
        var titles = new WC.StackPanel(); titles.Children.Add(T(Title(i), Theme.Page, Fg, true));
        subLine = T(sub, Theme.Body, Muted); subLine.Margin = new W.Thickness(0, 2, 0, 0); titles.Children.Add(subLine);
        head.Children.Add(titles); body.Children.Add(head);
        return big;
    }
    // the page's flush, first thing under the header (Nader, round 2: "on top always")
    void ActionRow(WC.StackPanel body, string label, int i, Action act, string key, string note, WC.Button extra = null) {
        var row = new WC.StackPanel { Orientation = WC.Orientation.Horizontal, Margin = new W.Thickness(0, Theme.S4, 0, 0) };
        var content = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
        content.Children.Add(new WC.TextBlock { Text = label, VerticalAlignment = W.VerticalAlignment.Center });
        if (Settings.Hotkey && key.Length > 0) content.Children.Add(Kbd(key));
        row.Children.Add(Btn(content, Ident[i], act));
        if (extra != null) { extra.Margin = new W.Thickness(Theme.S2, 0, 0, 0); row.Children.Add(extra); }
        body.Children.Add(row);
        if (note != null) { var n = T(note, Theme.Small, Muted); n.Margin = new W.Thickness(0, Theme.S3, 0, 0); body.Children.Add(n); }
    }
    // the live graph on a card: three hairlines, the line and a soft area under it; Plot() fills it every second
    void Graph(WC.StackPanel body, int i, out WS.Polyline graph, out WS.Polyline graph2, string caption) {
        var canvas = new WC.Grid { Height = GraphH + 4, Width = GraphW, FlowDirection = W.FlowDirection.LeftToRight, HorizontalAlignment = W.HorizontalAlignment.Left };
        foreach (double f in new[] { 0.25, 0.5, 0.75 })
            canvas.Children.Add(new WS.Line { X1 = 0, X2 = GraphW, Y1 = 2 + GraphH * (1 - f), Y2 = 2 + GraphH * (1 - f), Stroke = Line, StrokeThickness = 1, StrokeDashArray = new WM.DoubleCollection { 3, 5 },
                                              HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Top });
        var area = new WS.Polygon { Fill = Theme.Brush(Theme.Tint(IdentHex[i], 0x1C)), Margin = new W.Thickness(0, 2, 0, 2), HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Top };
        graph2 = new WS.Polyline { Stroke = Muted, StrokeThickness = 1.25, Opacity = 0.6, Margin = new W.Thickness(0, 2, 0, 2), HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Top };
        graph = new WS.Polyline { Stroke = Ident[i], StrokeThickness = 2, StrokeLineJoin = WM.PenLineJoin.Round, Tag = area, Margin = new W.Thickness(0, 2, 0, 2), HorizontalAlignment = W.HorizontalAlignment.Left, VerticalAlignment = W.VerticalAlignment.Top };
        canvas.Children.Add(area); canvas.Children.Add(graph2); canvas.Children.Add(graph);
        var card = Surface(canvas, new W.Thickness(Theme.S4)); card.Margin = new W.Thickness(0, Theme.S4, 0, 0);
        body.Children.Add(card);
        var cap = T(caption, Theme.Small, Muted); cap.Margin = new W.Thickness(0, Theme.S2, 0, 0); body.Children.Add(cap);
    }
    static void PlotG(WS.Polyline g, double[] h) { Plot(g, g.Tag as WS.Polygon, h, GraphW, GraphH); }

    // a titled group: the list sits on one rounded surface, rows divided by hairlines
    WC.StackPanel Section(WC.StackPanel body, string title, string note = null) {
        body.Children.Add(new WC.TextBlock { Text = title, FontSize = Theme.Title, FontWeight = W.FontWeights.Bold, Margin = new W.Thickness(0, Theme.S5, 0, note == null ? Theme.S2 : 2) });
        if (note != null) { var n = T(note, Theme.Small, Muted); n.Margin = new W.Thickness(0, 0, 0, Theme.S2); body.Children.Add(n); }
        var list = new WC.StackPanel { Background = WM.Brushes.Transparent, Margin = new W.Thickness(0, -1, 0, 0) };
        body.Children.Add(Surface(list, new W.Thickness(0)));
        return list;
    }
    static WC.TextBlock Empty(string text) { var t = T(text, Theme.Body, Muted); t.Margin = new W.Thickness(Theme.S4); return t; }
    WC.Border Row(string name, string detail, string value, WM.Brush valueColor, params W.UIElement[] right) {
        var r = new WC.DockPanel { MinHeight = 56 };
        var bs = new WC.StackPanel { Orientation = WC.Orientation.Horizontal, Margin = new W.Thickness(0, Theme.S2, Theme.S3, Theme.S2), VerticalAlignment = W.VerticalAlignment.Center };
        foreach (var b in right) {
            var btn = b as WC.Button;
            if (btn != null) { btn.Margin = new W.Thickness(Theme.S2, 0, 0, 0); btn.MinHeight = 36; btn.Padding = new W.Thickness(14, 4, 14, 4); btn.FontSize = Theme.Body; }
            bs.Children.Add(b);
        }
        WC.DockPanel.SetDock(bs, WC.Dock.Right); r.Children.Add(bs);
        if (value != null) {
            var v = T(value, Theme.Strong, valueColor ?? Fg, true); v.Width = 88; v.TextAlignment = W.TextAlignment.Right; v.VerticalAlignment = W.VerticalAlignment.Center; v.Margin = new W.Thickness(0, 0, Theme.S3, 0);
            WC.DockPanel.SetDock(v, WC.Dock.Right); r.Children.Add(v);
        }
        var n = new WC.StackPanel { VerticalAlignment = W.VerticalAlignment.Center, Margin = new W.Thickness(Theme.S4, Theme.S2, 0, Theme.S2) };
        n.Children.Add(T(name, Theme.Strong, Fg)); if (!string.IsNullOrEmpty(detail)) n.Children.Add(T(detail, Theme.Small, Muted));
        r.Children.Add(n);
        var hover = Theme.Brush(Theme.Tint(Theme.Raised, 0x70));
        var row = new WC.Border { Child = r, Background = WM.Brushes.Transparent, BorderBrush = Line, BorderThickness = new W.Thickness(0, 1, 0, 0), Tag = n };
        row.MouseEnter += delegate { row.Background = hover; }; row.MouseLeave += delegate { row.Background = WM.Brushes.Transparent; };
        return row;
    }
    static WC.Border Chip(string text, string hex) {
        var c = Col(hex); var bg = new WM.SolidColorBrush(WM.Color.FromArgb(0x22, c.R, c.G, c.B)); bg.Freeze();
        return new WC.Border { CornerRadius = new W.CornerRadius(Theme.RSm), Padding = new W.Thickness(8, 1, 8, 2), Margin = new W.Thickness(0, Theme.S1, 6, 0), Background = bg,
                               BorderBrush = Theme.Brush(Theme.Tint(hex, 0x66)), BorderThickness = new W.Thickness(1), HorizontalAlignment = W.HorizontalAlignment.Left,
                               Child = new WC.TextBlock { Text = text, FontSize = Theme.Caption, Foreground = B(hex) } };
    }
    bool ListBusy() { return hoverGuard != null && hoverGuard.IsMouseOver; }
    // after a click in a list: rebuild now, even with the mouse on it (the result of the click must show at once)
    void Refresh(Action fill) { var keep = hoverGuard; hoverGuard = null; fill(); hoverGuard = keep; }

    WC.Button EndButton(int pid, string name) {
        return Btn(Tx.T("proc.end"), null, () => {
            if (Tx.WBox(Window, Tx.F("proc.end_q", name, pid), "NJ-Flush", W.MessageBoxButton.YesNo, W.MessageBoxImage.Warning) != W.MessageBoxResult.Yes) return;
            Say(Tx.F(Procs.End(pid) ? "proc.ended" : "proc.end_fail", name));
        }, false);
    }
}
