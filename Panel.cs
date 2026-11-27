// NJ-Flush 1.3 panel (Nader 2026-11-27: "the widget opens a list like Task Manager for CPU, GPU and RAM; flush each or all";
// round 2: "design the settings and tabs better, smoother transitions using NJ-Kit; the flush button on top; the guard must show
// the real processes; a shortcut can be cleared"). Layout: tiles on the left like Task Manager's Performance tab (Jakob's law),
// the page on the right with its flush button first, Flush all as the largest target at the bottom (Fitts), Settings as the
// last tile. Motion (NJ-Kit nj-ui-motion-laws): a page enters with a 200 ms fade and a 10 px rise (L9, L6 ease-out), tiles and
// switches change colour in 150 ms, presses squash to 97% (L1), nothing animates on the once-a-second refresh (L20), lists
// never rebuild under the mouse, every action answers in the bar at once (L17); Windows "Show animations" off = no motion.
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

static class Panel {
    static W.Window win;
    static View view;
    static W.Threading.Dispatcher disp;
    public static string StartPage;   // /panel ram|cpu|gpu|guard|settings

    // from the widget: one panel, on its own UI thread, so the widget keeps sampling and its shortcuts keep working
    // reload: rebuild the page shown (a double-click on NJ-Flush.exe while the panel is open)
    public static void Open(string page = null, bool reload = false) {
        if (disp != null && win != null) {
            disp.BeginInvoke((Action)delegate {
                if (win.WindowState == W.WindowState.Minimized) win.WindowState = W.WindowState.Normal;
                win.Activate(); win.Topmost = true; win.Topmost = false;   // to the front even when another app has focus
                if (page != null) view.Go(page); else if (reload) view.Reload();
            });
            return;
        }
        StartPage = page;
        var t = new Thread(Run) { IsBackground = true, Name = "NJ-Flush panel" };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }

    // standalone (NJ-Flush.exe /panel [page], /settings) or the panel thread
    public static void Run() {
        disp = W.Threading.Dispatcher.CurrentDispatcher;
        Live.WantDetail = true;
        view = new View(); win = view.Window;
        win.Closed += delegate {
            Live.WantDetail = false; win = null; view = null; disp = null;
            W.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(W.Threading.DispatcherPriority.Background);
        };
        win.Show(); win.Activate();
        W.Threading.Dispatcher.Run();
    }

    // why a flush would be refused right now, or null (the same rules the widget and the task use)
    public static string Refusal(string kind, bool deep) {
        int left = Settings.CooldownLeft(kind);
        if (left > 0) return Tx.F("refusal.cooldown", left);
        double ram; lock (Live.L) ram = Live.Ram;
        if (kind == "ram" && !deep && ram < Settings.MinRam) return Tx.F("refusal.ram_fine", ram, Settings.MinRam);
        return null;
    }

    // settings changed in the panel: saved at once, and the widget (its own thread) applies them
    public static void Applied() {
        Settings.Save(); Tx.Resolve();
        var w = Widget.Current; if (w != null) try { w.BeginInvoke((Action)w.ApplySettings); } catch (Exception) { }
    }
}

class View {
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

    static readonly W.ResourceDictionary Styles = (W.ResourceDictionary)W.Markup.XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Btn' TargetType='{x:Type Button}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='~RMD~'
                Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.88'/></Trigger>
          <Trigger Property='ButtonBase.IsPressed' Value='True'><Setter TargetName='Bd' Property='RenderTransform'><Setter.Value><ScaleTransform ScaleX='0.97' ScaleY='0.97'/></Setter.Value></Setter></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~FG~'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter TargetName='Bd' Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Ghost' TargetType='{x:Type Button}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='~RMD~'
                Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='~RAISED~'/><Setter TargetName='Bd' Property='BorderBrush' Value='~LINE2~'/></Trigger>
          <Trigger Property='ButtonBase.IsPressed' Value='True'><Setter TargetName='Bd' Property='RenderTransform'><Setter.Value><ScaleTransform ScaleX='0.97' ScaleY='0.97'/></Setter.Value></Setter></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~FG~'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter TargetName='Bd' Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Toggle' TargetType='{x:Type CheckBox}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='~FG~'/><Setter Property='FontSize' Value='14'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type CheckBox}'>
        <DockPanel Background='Transparent' MinHeight='40'>
          <Grid DockPanel.Dock='Right' Width='40' Height='22' Margin='16,0,0,0' VerticalAlignment='Center'>
            <Border x:Name='Track' CornerRadius='11' Background='#2A3A52' BorderBrush='#3A4C66' BorderThickness='1'/>
            <Ellipse x:Name='Knob' Width='16' Height='16' Fill='~FG~' HorizontalAlignment='Left' Margin='3,0,0,0'>
              <Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform>
            </Ellipse>
          </Grid>
          <ContentPresenter VerticalAlignment='Center'/>
        </DockPanel>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='Track' Property='Background' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
            <Setter TargetName='Track' Property='BorderBrush' Value='{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}'/>
            <Setter TargetName='Knob' Property='Fill' Value='~INK~'/>
            <Trigger.EnterActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='18' Duration='0:0:0.15'>
                <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.15'>
                <DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Track' Property='Opacity' Value='0.88'/></Trigger>
          <Trigger Property='UIElement.IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Field' TargetType='{x:Type TextBox}'>
    <Setter Property='Foreground' Value='~FG~'/><Setter Property='CaretBrush' Value='~FG~'/><Setter Property='FontSize' Value='14'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type TextBox}'>
        <Border x:Name='Bd' Background='~SURFACE~' BorderBrush='~LINE~' BorderThickness='1' CornerRadius='~RMD~' Padding='12,0' MinHeight='40'>
          <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~LINE2~'/></Trigger>
          <Trigger Property='UIElement.IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='~GUARD~'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Clear' TargetType='{x:Type RepeatButton}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Fill' TargetType='{x:Type RepeatButton}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'>
      <Border Height='4' CornerRadius='2' Background='~RAM~' VerticalAlignment='Center'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Slide' TargetType='{x:Type Slider}'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='{x:Type Slider}'>
        <Grid Height='28' Background='Transparent'>
          <Border Height='4' CornerRadius='2' Background='#2A3A52' VerticalAlignment='Center'/>
          <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource Fill}'/></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource Clear}'/></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
              <Ellipse x:Name='K' Width='16' Height='16' Fill='~FG~' Stroke='~RAM~' StrokeThickness='2'/>
              <ControlTemplate.Triggers><Trigger Property='UIElement.IsMouseOver' Value='True'><Setter TargetName='K' Property='Fill' Value='#FFFFFF'/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
          </Track>
        </Grid>
        <ControlTemplate.Triggers><Trigger Property='UIElement.IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Style='{StaticResource Clear}'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Style='{StaticResource Clear}'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='4' Background='#2A3A52' Margin='2,0,2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>"
        .Replace("~RMD~", "8").Replace("~FG~", Theme.Fg).Replace("~INK~", Theme.Ink).Replace("~RAISED~", Theme.Raised).Replace("~LINE2~", Theme.LineStrong)
        .Replace("~LINE~", Theme.Line).Replace("~SURFACE~", Theme.Surface).Replace("~GUARD~", Theme.Ident[3]).Replace("~RAM~", Theme.Ident[0]));

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

    // ---- RAM ----
    void RamPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        var big = Header(body, 0, "", out sub);
        ActionRow(body, Tx.T("ram.flush"), 0, () => Do("ram", false), Settings.Keys_[1], Tx.T("ram.deep_note"),
                  Btn(Tx.T("ram.deep"), null, () => Do("ram", true), false));
        Graph(body, 0, out g, out g2, Tx.T("ram.graph"));
        var detail = new WC.StackPanel(); body.Children.Add(detail);
        var list = Section(body, Tx.T("ram.top")); hoverGuard = list;
        Action fill = () => { detail.Children.Clear(); BreakdownWindow.Build(detail, Mem.Snapshot(), false); };
        Action apps = () => {
            if (ListBusy()) return;
            List<ProcRow> rows; lock (Live.L) rows = Live.Rows;
            list.Children.Clear();
            foreach (var grp in rows.Where(r => r.Pid > 4).GroupBy(r => Procs.Short(r.Name)).OrderByDescending(x => x.Sum(r => r.Private)).Take(7)) {
                var topRow = grp.OrderByDescending(r => r.Private).First(); int n = grp.Count();
                list.Children.Add(Row(grp.Key, n > 1 ? Tx.Plural("process", n) : Tx.F("proc.pid", topRow.Pid), string.Format("{0:0.00} GB", grp.Sum(r => r.Private) / 1073741824.0), null,
                                      n == 1 && !Busy.Protected(topRow.Name) ? new W.UIElement[] { EndButton(topRow.Pid, grp.Key) } : new W.UIElement[0]));
            }
        };
        fill(); apps();
        tickPage = () => {
            double ram, used, total; lock (Live.L) { ram = Live.Ram; used = Live.RamUsed; total = Live.RamTotal; }
            big.Text = Tx.Pct(ram); big.Foreground = Scale(ram) ?? Ident[0];
            sub.Text = Tx.F("ram.sub_use", used, total) + "   ·   " +
                       (ram < Settings.MinRam ? Tx.F("ram.sub_fine", Settings.MinRam) : Tx.F("ram.sub_play", Tx.TierName(Overlay.TierOf(ram))));
            PlotG(g, Live.Ordered(Live.RamH));
            if (ticks % 3 == 0) apps();
            if (ticks % 15 == 0 && !detail.IsMouseOver) fill();   // the breakdown reads every process: every 15 s is enough
        };
    }

    // ---- CPU ----
    void CpuPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        var big = Header(body, 1, CpuMeter.Model() + "   ·   " + Tx.F("threads", Environment.ProcessorCount), out sub);
        ActionRow(body, Tx.T("cpu.calm"), 1, () => Do("cpu", false), Settings.Keys_[2], Tx.T("cpu.calm_note"));
        var busyLine = T("", 13, Muted); busyLine.Margin = new W.Thickness(0, 6, 0, 0); body.Children.Add(busyLine);
        Graph(body, 1, out g, out g2, Tx.T("cpu.graph"));
        var list = Section(body, Tx.T("cpu.top")); hoverGuard = list;
        int n = Environment.ProcessorCount;
        Action apps = null;
        apps = () => {
            if (ListBusy()) return;
            Dictionary<int, double> now; List<ProcRow> rows; List<BusyApp> busy;
            lock (Live.L) { now = Live.CpuNow; rows = Live.Rows; busy = Live.BusyNow; }
            busyLine.Text = busy.Count == 0 ? Tx.T("cpu.busy_none") : Tx.F("cpu.busy_list", Tx.Join(busy.Select(b => Procs.Short(b.Name)).Distinct().Take(4)));
            busyLine.Foreground = busy.Count == 0 ? Muted : Amber;
            list.Children.Clear();
            var names = rows.GroupBy(r => r.Pid).ToDictionary(x => x.Key, x => x.First().Name);
            foreach (var kv in now.Where(x => names.ContainsKey(x.Key) && x.Key > 4).OrderByDescending(x => x.Value).Take(8)) {
                int pid = kv.Key; string name = Procs.Short(names[pid]); bool eco = Eco.IsOn(pid), isBusy = busy.Any(b => b.Pid == pid), sys = Busy.Protected(names[pid]);
                var dp = new List<string>(); if (eco) dp.Add(Tx.T("cpu.eco")); if (isBusy) dp.Add(Tx.T("cpu.busy"));
                string detail = sys ? Tx.T("proc.system") : Tx.Join(dp);
                W.UIElement[] right = sys ? new W.UIElement[0] : new W.UIElement[] {
                    eco ? Btn(Tx.T("cpu.restore"), null, () => { Eco.Set(pid, false); Say(Tx.F("cpu.restored", name)); Refresh(apps); }, false)
                        : Btn(Tx.T("cpu.calm_btn"), null, () => { Eco.Set(pid, true); Say(Tx.F("cpu.calmed", name)); Refresh(apps); }, false),
                    EndButton(pid, name) };
                list.Children.Add(Row(name, detail.Length > 0 ? detail : Tx.F("proc.pid", pid), string.Format("{0:0.0}%", kv.Value / n * 100), isBusy ? Amber : null, right));
            }
            if (list.Children.Count == 0) list.Children.Add(Empty(Tx.T("list.measuring")));
        };
        apps();
        tickPage = () => {
            double cpu; lock (Live.L) cpu = Live.Cpu;
            big.Text = Tx.Pct(cpu); big.Foreground = Scale(cpu) ?? Ident[1];
            PlotG(g, Live.Ordered(Live.CpuH));
            if (ticks % 3 == 0) apps();
        };
    }

    // ---- GPU ----
    void GpuPage(WC.StackPanel body) {
        WC.TextBlock sub; WS.Polyline g, g2;
        string name; lock (Live.L) name = Live.GpuName;
        var big = Header(body, 2, name ?? Tx.T("gpu.looking"), out sub);
        ActionRow(body, Tx.T("gpu.restart"), 2, () => Do("gpu", false), Settings.Keys_[3], Tx.T("gpu.restart_note"));
        var facts = T("", 13, Muted); facts.Margin = new W.Thickness(0, 6, 0, 0); body.Children.Add(facts);
        Graph(body, 2, out g, out g2, Tx.T("gpu.graph"));
        var list = Section(body, Tx.T("gpu.top")); hoverGuard = list;
        Action apps = () => {
            if (ListBusy()) return;
            List<GpuProc> gp; List<ProcRow> rows; lock (Live.L) { gp = Live.GpuProcs; rows = Live.Rows; }
            var names = rows.GroupBy(r => r.Pid).ToDictionary(x => x.Key, x => Procs.Short(x.First().Name));
            list.Children.Clear();
            foreach (var p in gp.Where(x => names.ContainsKey(x.Pid)).OrderByDescending(x => x.VramGB).Take(8)) {
                string nm = names[p.Pid]; bool sys = Busy.Protected(nm);
                list.Children.Add(Row(nm, Tx.F("gpu.row", p.Load, sys ? Tx.T("proc.system") : Tx.F("proc.pid", p.Pid)),
                                      p.VramGB >= 1 ? string.Format("{0:0.0} GB", p.VramGB) : string.Format("{0:0} MB", p.VramGB * 1024), null,
                                      sys ? new W.UIElement[0] : new W.UIElement[] { EndButton(p.Pid, nm) }));
            }
            if (list.Children.Count == 0) list.Children.Add(Empty(Tx.T("list.measuring")));
        };
        apps();
        tickPage = () => {
            double v, load, vu, vt, sh; string now;
            lock (Live.L) { v = Live.Vram; load = Live.GpuLoad; vu = Live.VramUsed; vt = Live.VramTotal; sh = Live.Shared; now = Live.GpuName; }
            if (name == null) { if (now != null) { current = -1; Select(2); } return; }   // the first GPU sample arrived: build the page for real
            big.Text = Tx.Pct(v); big.Foreground = Scale(v) ?? Ident[2];
            facts.Text = Tx.F("gpu.facts", vu, vt, load, sh);
            PlotG(g, Live.Ordered(Live.VramH)); PlotG(g2, Live.Ordered(Live.GpuH));
            if (ticks % 3 == 0) apps();
        };
    }

    // ---- efficiency guard: the real apps running now, each with a switch (Nader, round 2) ----
    void GuardPage(WC.StackPanel body) {
        WC.TextBlock sub;
        var big = Header(body, 3, "", out sub);
        var intro = T(Tx.T("guard.intro"), 13, Muted);
        intro.Margin = new W.Thickness(0, 10, 0, 6); body.Children.Add(intro);
        var on = Toggle(Tx.T("guard.keep"), Settings.Guard, x => Settings.Guard = x, Ident[3]);
        var bat = Toggle(Tx.T("guard.battery"), Settings.GuardBattery, x => Settings.GuardBattery = x, Ident[3]);
        on.Click += delegate { bat.IsEnabled = Settings.Guard; }; bat.IsEnabled = Settings.Guard;
        body.Children.Add(on); body.Children.Add(bat);
        // a filter box: find any running app
        var search = new WC.TextBox { Style = (W.Style)Styles["Field"] };
        var hint = T(Tx.T("guard.hint"), 13, Muted); hint.IsHitTestVisible = false; hint.Margin = new W.Thickness(13, 0, 0, 0); hint.VerticalAlignment = W.VerticalAlignment.Center; hint.Foreground = B(Theme.Dim);
        var sg = new WC.Grid { Margin = new W.Thickness(0, Theme.S5, 0, 0) }; sg.Children.Add(search); sg.Children.Add(hint); body.Children.Add(sg);
        var list = Section(body, Tx.T("guard.running"), Tx.T("guard.running_note"));
        hoverGuard = list;
        var goneTitle = new WC.TextBlock { Text = Tx.T("guard.gone"), FontSize = Theme.Body, Foreground = Muted, Margin = new W.Thickness(0, Theme.S4, 0, Theme.S2) };
        var gone = new WC.WrapPanel();
        body.Children.Add(goneTitle); body.Children.Add(gone);
        int session = Procs.MySession;
        Action fill = null;
        fill = () => {
            if (ListBusy()) return;
            List<ProcRow> rows; lock (Live.L) rows = Live.Rows;
            var mine = rows.Where(r => r.Session == session && r.Pid > 4 && r.Pid != Procs.MyPid && !Busy.Protected(r.Name)).ToList();
            var guarded = new HashSet<string>(Settings.GuardApps);
            var apps = mine.GroupBy(r => Procs.Short(r.Name).ToLowerInvariant())
                           .Select(x => new { Name = x.Key, Display = Procs.Short(x.First().Name), Pids = x.Select(r => r.Pid).ToList(), Eco = x.Count(r => Eco.IsOn(r.Pid)), Kept = guarded.Contains(x.Key) })
                           .ToList();
            string q = search.Text.Trim().ToLowerInvariant();
            var shown = (q.Length > 0 ? apps.Where(a => a.Name.Contains(q)) : apps.Where(a => a.Eco > 0 || a.Kept))
                        .OrderByDescending(a => a.Eco > 0).ThenByDescending(a => a.Kept).ThenBy(a => a.Name).Take(40).ToList();
            list.Children.Clear();
            foreach (var a in shown) {
                var app = a;
                var sw = new WC.CheckBox { IsChecked = app.Kept, Style = (W.Style)Styles["Toggle"], Tag = Ident[3], Content = "", ToolTip = Tx.F("guard.tip_keep", app.Display) };
                sw.Click += delegate {
                    if (sw.IsChecked == true) { if (!Settings.GuardApps.Contains(app.Name)) Settings.GuardApps.Add(app.Name); foreach (int pid in app.Pids) Eco.Set(pid, false); Say(Tx.F("guard.said_keep", app.Display)); }
                    else { Settings.GuardApps.Remove(app.Name); Say(Tx.F("guard.said_free", app.Display)); }
                    Panel.Applied(); Refresh(fill);
                };
                var info = new WC.WrapPanel();
                if (app.Eco > 0) info.Children.Add(Chip(app.Eco == app.Pids.Count ? Tx.T("chip.eco") : Tx.F("chip.eco_n", app.Eco, app.Pids.Count), "#FACC15"));
                if (app.Kept) info.Children.Add(Chip(Tx.T("chip.kept"), "#60A5FA"));
                var row = Row(app.Display, Tx.Plural("process", app.Pids.Count), null, null, sw);
                ((WC.StackPanel)row.Tag).Children.Add(info);
                list.Children.Add(row);
            }
            if (shown.Count == 0) list.Children.Add(Empty(Tx.T(q.Length > 0 ? "guard.none_q" : "guard.none")));
            gone.Children.Clear();
            foreach (string gn in Settings.GuardApps.Where(x => !apps.Any(a => a.Name == x)).ToList()) {
                string gname = gn;
                var x = Btn(gname + "   ×", null, () => { Settings.GuardApps.Remove(gname); Panel.Applied(); Say(Tx.F("guard.removed", gname)); Refresh(fill); }, false);
                x.MinHeight = 32; x.Padding = new W.Thickness(12, 2, 12, 2); x.FontSize = Theme.Small; x.Margin = new W.Thickness(0, 0, 6, 6); x.ToolTip = Tx.F("guard.remove_tip", gname);
                gone.Children.Add(x);
            }
            goneTitle.Visibility = gone.Visibility = gone.Children.Count > 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
            int ecoApps = apps.Count(a => a.Eco > 0);
            big.Text = ecoApps.ToString(); big.Foreground = ecoApps > 0 ? Amber : Ident[3];
            sub.Text = !Settings.Guard ? Tx.T("guard.off") : Guard.Paused ? Tx.T("guard.paused") :
                       Tx.F("guard.status", Tx.Plural("app", ecoApps), Settings.GuardApps.Count, Tx.Plural("process", Guard.Kept));
        };
        search.TextChanged += delegate { hint.Visibility = search.Text.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed; Refresh(fill); };
        fill();
        tickPage = () => { if (ticks % 3 == 0) fill(); };
    }

    // ---- settings (1.3 round 2: the WinForms window became this page) ----
    bool recording; int recIndex = -1; Action recDone;
    void StopRecording() { recording = false; recIndex = -1; Window.PreviewKeyDown -= OnRecordKey; if (recDone != null) recDone(); Panel.Applied(); }
    void OnRecordKey(object s, W.Input.KeyEventArgs e) {
        if (!recording) return;
        e.Handled = true;
        var key = e.Key == W.Input.Key.System ? e.SystemKey : e.Key;
        if (key == W.Input.Key.Escape) { StopRecording(); return; }
        if (key == W.Input.Key.Back || key == W.Input.Key.Delete) { Settings.Keys_[recIndex] = ""; StopRecording(); return; }
        if (key == W.Input.Key.LeftCtrl || key == W.Input.Key.RightCtrl || key == W.Input.Key.LeftAlt || key == W.Input.Key.RightAlt || key == W.Input.Key.LeftShift ||
            key == W.Input.Key.RightShift || key == W.Input.Key.LWin || key == W.Input.Key.RWin) return;
        var m = W.Input.Keyboard.Modifiers;
        uint mods = ((m & W.Input.ModifierKeys.Control) != 0 ? Hotkeys.Ctrl : 0) | ((m & W.Input.ModifierKeys.Alt) != 0 ? Hotkeys.Alt : 0) | ((m & W.Input.ModifierKeys.Shift) != 0 ? Hotkeys.Shift : 0);
        if (mods == 0) { Say(Tx.T("set.key_need_mod"), Amber); return; }
        string combo = Hotkeys.Format(mods, (uint)W.Input.KeyInterop.VirtualKeyFromKey(key));
        for (int j = 0; j < Settings.Keys_.Length; j++) if (j != recIndex && Settings.Keys_[j] == combo) { Settings.Keys_[j] = ""; Say(Tx.F("set.key_moved", combo, Tx.Action(j))); }
        Settings.Keys_[recIndex] = combo;
        StopRecording();
    }

    WC.StackPanel Card_(WC.StackPanel body, string title, string note = null) {
        body.Children.Add(new WC.TextBlock { Text = title, FontSize = Theme.Title, FontWeight = W.FontWeights.Bold, Margin = new W.Thickness(0, Theme.S5, 0, note == null ? Theme.S2 : 2) });
        if (note != null) { var n = T(note, Theme.Small, Muted); n.Margin = new W.Thickness(0, 0, 0, Theme.S2); body.Children.Add(n); }
        var inner = new WC.StackPanel();
        body.Children.Add(Surface(inner, new W.Thickness(Theme.S4, Theme.S1, Theme.S4, Theme.S2)));
        return inner;
    }
    W.FrameworkElement SliderRow(string label, int min, int max, int value, int step, string fmt, Action<int> set) {
        var r = new WC.DockPanel { Margin = new W.Thickness(0, 6, 0, 6) };
        var val = T(string.Format(fmt, value), 13, Muted); val.Width = Tx.Ar ? 140 : 90; val.TextAlignment = W.TextAlignment.Right; val.VerticalAlignment = W.VerticalAlignment.Center;
        WC.DockPanel.SetDock(val, WC.Dock.Right); r.Children.Add(val);
        var s = new WC.Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = 220, Style = (W.Style)Styles["Slide"], IsSnapToTickEnabled = true,
                                TickFrequency = step, SmallChange = step, LargeChange = step * 5, VerticalAlignment = W.VerticalAlignment.Center, Margin = new W.Thickness(16, 0, 0, 0) };
        s.ValueChanged += delegate { int v = (int)Math.Round(s.Value); val.Text = Tx.Bidi(string.Format(fmt, v)); set(v); Panel.Applied(); };
        WC.DockPanel.SetDock(s, WC.Dock.Right); r.Children.Add(s);
        var l = T(label, 14, Fg); l.VerticalAlignment = W.VerticalAlignment.Center; r.Children.Add(l);
        return r;
    }
    static W.UIElement Note(string text) { var n = T(text, 12, Muted); n.Margin = new W.Thickness(0, -4, 60, 6); return n; }

    void SettingsPage(WC.StackPanel body) {
        WC.TextBlock sub;
        Header(body, 4, Tx.T("set.sub"), out sub);

        // the language: first on the page; the page and the frame are rebuilt in the new language at once, the widget follows
        var lang = Card_(body, Tx.T("lang.label"), Tx.T("lang.note"));
        var lrow = new WC.WrapPanel { Margin = new W.Thickness(0, 6, 0, 8) };
        foreach (var opt in new[] { Tuple.Create("auto", Tx.T("lang.auto")), Tuple.Create("en", Tx.T("lang.en")), Tuple.Create("ar", Tx.T("lang.ar")) }) {
            string code = opt.Item1; bool on = Settings.Language == code;
            var lb = Btn(opt.Item2, Ident[0], () => { if (Settings.Language == code) return; Settings.Language = code; Panel.Applied(); Relang(); }, on);
            lb.MinHeight = 38; lb.MinWidth = 110; lb.Padding = new W.Thickness(16, 4, 16, 4); lb.FontSize = Theme.Body; lb.Margin = new W.Thickness(0, 0, 8, 0);
            lrow.Children.Add(lb);
        }
        lang.Children.Add(lrow);


        var anim = Card_(body, Tx.T("set.anim"));
        anim.Children.Add(Toggle(Tx.T("set.play_anim"), Settings.Animation, x => Settings.Animation = x));
        var snd = Toggle(Tx.T("set.play_sound"), Settings.Sound, x => Settings.Sound = x); anim.Children.Add(snd);
        var vol = SliderRow(Tx.T("set.volume"), 0, 100, Settings.Volume, 5, Tx.T("fmt.pct"), x => Settings.Volume = x); anim.Children.Add(vol);
        snd.Click += delegate { vol.IsEnabled = Settings.Sound; }; vol.IsEnabled = Settings.Sound;
        // previews: one row per flush, every variant (Nader, round 2)
        var prev = Card_(body, Tx.T("set.preview"), Tx.T("set.preview_note"));
        foreach (var grp in new[] {
            Tuple.Create(Tx.T("grp.ram"), new[] { "light", "medium", "heavy", "clogged" }, Tx.TierNames(), 0),
            Tuple.Create(Tx.T("grp.cpu"), new[] { "cpu-0", "cpu-1", "cpu-2", "cpu-3" }, Tx.SceneNames("cpu"), 1),
            Tuple.Create(Tx.T("grp.gpu"), new[] { "gpu-0", "gpu-1", "gpu-2", "gpu-3" }, Tx.SceneNames("gpu"), 2),
            Tuple.Create(Tx.T("grp.all"), new[] { "all-0", "all-1", "all-2", "all-3" }, Tx.SceneNames("all"), 0) }) {
            var row = new WC.DockPanel { Margin = new W.Thickness(0, 6, 0, 6) };
            var lab = T(grp.Item1, 14, Ident[grp.Item4], true); lab.Width = Tx.Ar ? 64 : 56; lab.VerticalAlignment = W.VerticalAlignment.Center; row.Children.Add(lab);
            var wrap = new WC.WrapPanel();
            for (int k = 0; k < 4; k++) {
                string arg = grp.Item2[k];
                var b = Btn(grp.Item3[k], null, () => { Settings.Save(); Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "/preview " + arg) { UseShellExecute = false }); }, false);
                b.MinHeight = 36; b.Padding = new W.Thickness(12, 3, 12, 3); b.FontSize = Theme.Body; b.Margin = new W.Thickness(0, 0, 6, 0); b.MinWidth = 100;
                wrap.Children.Add(b);
            }
            row.Children.Add(wrap); prev.Children.Add(row);
        }

        var ram = Card_(body, Tx.T("set.ram"));
        ram.Children.Add(SliderRow(Tx.T("set.ram_from"), 50, 99, Settings.MinRam, 1, Tx.T("fmt.in_use"), x => Settings.MinRam = x));
        ram.Children.Add(Note(Tx.T("set.ram_from_note")));
        ram.Children.Add(Toggle(Tx.T("set.explorer"), Settings.RestartExplorer, x => Settings.RestartExplorer = x));
        ram.Children.Add(Note(Tx.T("set.explorer_note")));
        ram.Children.Add(SliderRow(Tx.T("set.wait"), 10, 600, Settings.Cooldown, 10, Tx.T("fmt.sec"), x => Settings.Cooldown = x));
        var auto = Toggle(Tx.T("set.auto"), Settings.AutoFlush, x => Settings.AutoFlush = x); ram.Children.Add(auto);
        var at = SliderRow(Tx.T("set.auto_at"), 70, 99, Settings.AutoAt, 1, Tx.T("fmt.ram_pct"), x => Settings.AutoAt = x); ram.Children.Add(at);
        auto.Click += delegate { at.IsEnabled = Settings.AutoFlush; }; at.IsEnabled = Settings.AutoFlush;

        var all = Card_(body, Tx.T("set.all"));
        all.Children.Add(Toggle(Tx.T("set.gpu_reset"), Settings.GpuReset, x => Settings.GpuReset = x));
        all.Children.Add(Note(Tx.T("set.gpu_note")));
        all.Children.Add(Toggle(Tx.T("set.calm"), Settings.CalmCpu, x => Settings.CalmCpu = x));

        var wid = Card_(body, Tx.T("set.widget"));
        wid.Children.Add(Toggle(Tx.T("set.bars"), Settings.WidgetBars, x => Settings.WidgetBars = x));
        wid.Children.Add(SliderRow(Tx.T("set.bright"), 20, 100, Settings.IdleOpacity, 5, Tx.T("fmt.pct"), x => Settings.IdleOpacity = x));
        wid.Children.Add(Toggle(Tx.T("set.autostart"), Settings.Autostart(), x => Settings.SetAutostart(x)));
        var pos = new WC.DockPanel { Margin = new W.Thickness(0, 6, 0, 4) };
        var rp = Btn(Tx.T("set.reset_pos"), null, () => { Settings.WidgetX = Settings.WidgetY = -1; Panel.Applied(); Say(Tx.T("set.pos_done")); }, false);
        rp.MinHeight = 36; rp.Padding = new W.Thickness(12, 3, 12, 3); WC.DockPanel.SetDock(rp, WC.Dock.Right); pos.Children.Add(rp);
        var pl = T(Tx.T("set.drag_note"), 13, Muted); pl.VerticalAlignment = W.VerticalAlignment.Center; pos.Children.Add(pl);
        wid.Children.Add(pos);

        // shortcuts: a recorder per action, and a clear button (Nader, round 2: "let me remove a shortcut")
        var keys = Card_(body, Tx.T("set.keys"), Tx.T("set.keys_note"));
        keys.Children.Add(Toggle(Tx.T("set.keys_any"), Settings.Hotkey, x => Settings.Hotkey = x));
        var keyRows = new List<Action>();
        for (int i = 0; i < Settings.Actions.Length; i++) {
            int idx = i;
            var r = new WC.DockPanel { Margin = new W.Thickness(0, 4, 0, 4) };
            var clear = Btn("×", null, () => { if (recording) StopRecording(); Settings.Keys_[idx] = ""; Panel.Applied(); Say(Tx.F("set.key_cleared", Tx.Action(idx))); foreach (var f in keyRows) f(); }, false);
            clear.MinHeight = 36; clear.Width = 38; clear.Padding = new W.Thickness(0); clear.FontSize = 16; clear.ToolTip = Tx.T("set.key_remove");
            WC.DockPanel.SetDock(clear, WC.Dock.Right); r.Children.Add(clear);
            var rec = Btn("", null, () => { }, false); rec.MinHeight = 36; rec.Width = 170; rec.FontSize = 13; rec.Margin = new W.Thickness(0, 0, 6, 0);
            WC.DockPanel.SetDock(rec, WC.Dock.Right); r.Children.Add(rec);
            var err = T("", 12, B("#F87171")); err.VerticalAlignment = W.VerticalAlignment.Center; err.Margin = new W.Thickness(0, 0, 10, 0);
            WC.DockPanel.SetDock(err, WC.Dock.Right); r.Children.Add(err);
            var l = T(Tx.Action(idx), 14, Fg); l.VerticalAlignment = W.VerticalAlignment.Center; r.Children.Add(l);
            Action show = () => {
                bool me = recording && recIndex == idx;
                rec.Content = me ? Tx.T("set.key_press") : Settings.Keys_[idx].Length > 0 ? Settings.Keys_[idx] : Tx.T("set.key_none");
                rec.BorderBrush = me ? Ident[0] : Line; rec.Foreground = Settings.Keys_[idx].Length > 0 || me ? Fg : Muted;
                clear.IsEnabled = Settings.Keys_[idx].Length > 0;
                var w = Widget.Current; string e = w != null && Settings.Hotkey ? w.HotkeyErrors[idx] : null;
                err.Text = e ?? "";
            };
            keyRows.Add(show);
            rec.Click += delegate {
                if (recording) StopRecording();
                recording = true; recIndex = idx; recDone = () => { foreach (var f in keyRows) f(); };
                var w = Widget.Current; if (w != null) try { w.BeginInvoke((Action)w.SuspendHotkeys); } catch (Exception) { }   // so the keys reach this box
                Window.PreviewKeyDown += OnRecordKey;
                foreach (var f in keyRows) f();
            };
            keys.Children.Add(r);
        }
        foreach (var f in keyRows) f();

        var upd = Card_(body, Tx.T("set.updates"));
        upd.Children.Add(Toggle(Tx.T("set.auto_update"), Settings.AutoUpdate, x => Settings.AutoUpdate = x));
        var ur = new WC.DockPanel { Margin = new W.Thickness(0, 6, 0, 4) };
        var cb = Btn(Tx.T("set.check_now"), null, () => Updater.Interactive(null), false); cb.MinHeight = 36; cb.Padding = new W.Thickness(12, 3, 12, 3);
        WC.DockPanel.SetDock(cb, WC.Dock.Right); ur.Children.Add(cb);
        var vt = T(Tx.F("set.version", Updater.Version), 13, Muted); vt.VerticalAlignment = W.VerticalAlignment.Center; ur.Children.Add(vt);
        upd.Children.Add(ur);

        var reset = Btn(Tx.T("set.reset_all"), null, () => {
            if (Tx.WBox(Window, Tx.T("set.reset_q"), "NJ-Flush", W.MessageBoxButton.YesNo, W.MessageBoxImage.Question) != W.MessageBoxResult.Yes) return;
            var keep = Settings.GuardApps; Settings.Defaults(); Settings.GuardApps = keep; Panel.Applied(); Relang(); Select(4); Say(Tx.T("set.reset_done"));
        }, false);
        reset.HorizontalAlignment = W.HorizontalAlignment.Left; reset.Margin = new W.Thickness(0, 24, 0, 8);
        body.Children.Add(reset);
        tickPage = () => { if (ticks % 2 == 0 && !recording) foreach (var f in keyRows) f(); };
    }
}
