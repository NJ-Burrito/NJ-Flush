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
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;

// The widget (1.3 round 2, Nader: "not placed properly; let me move it anywhere and it remembers; proper dimensions, theme, beautify").
// 224 x 72 (96 dpi, scaled to the screen; 224 x 46 without the bars): a 10 px margin all round, then on a 4 px grid: RAM label, RAM % and GB
// on one baseline, a 6 px RAM bar, CPU and VRAM labels and values on one baseline, 4 px bars. Everything is placed by baseline and by the
// same margins, so the space above and below is equal. Rounded card (Theme.cs colours), 1 px border that brightens on hover. Drag it anywhere (it snaps to screen edges within
// 12 px); the spot is saved and survives restarts and flushes; "Reset widget position" puts it back above the clock.
partial class Widget : Form {
    [StructLayout(LayoutKind.Sequential)]
    struct WindowPos { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr p);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    const double Lit = 0.97;
    static double Dim { get { return Settings.IdleOpacity / 100.0; } }
    public static Widget Current;
    public static bool RenderOnly;
    public static float ForceScale;   // /render-ui: draw at this scale (1.25 for 125%) instead of the screen's   // /render-ui widget: drawn off screen, no tray icon, shortcuts, hooks or timers
    public static Widget ForRender() { return new Widget(); }
    public void SetHover(bool on) { hovered = on; Invalidate(); }
    public string[] HotkeyErrors = new string[5];   // per action: null, or why the shortcut could not be registered
    readonly float sc;                              // screen scale (1.25 at 125%)
    int S(float v) { return (int)Math.Round(v * sc); }
    Size Want { get { return new Size(S(224), S(Settings.WidgetBars ? 72 : 46)); } }

    // called from the panel's settings page: shortcuts, bars, position and idle brightness change live
    public void ApplySettings() {
        string lang = (Tx.Ar ? "ar" : "en") + Settings.Language;
        if (lang != shownLang) { shownLang = lang; ApplyLanguage(); }
        for (int i = 0; i < Settings.Actions.Length; i++) {
            UnregisterHotKey(Handle, i + 1); HotkeyErrors[i] = null;
            uint m, k;
            if (!Settings.Hotkey || !Hotkeys.Parse(Settings.Keys_[i], out m, out k) || k == 0) continue;
            if (!RegisterHotKey(Handle, i + 1, m | 0x4000, k)) HotkeyErrors[i] = Tx.T("hotkey.taken");  // MOD_NOREPEAT
        }
        Size = Want; Pin(); Invalidate();   // Size, not ClientSize: ClientSize is re-applied after the handle exists with a caption's height added (91 px instead of 62)
        if (!hovered) Opacity = Dim;
    }
    public void SuspendHotkeys() { for (int i = 1; i <= 5; i++) UnregisterHotKey(Handle, i); }

    Mem.MemStatus m;
    string pctText = "", gbText = "";
    float pct, cpu, vram; bool hasGpu, hovered;
    readonly Font big, small, label;
    string face = "Segoe UI";
    ContextMenuStrip menu;
    readonly Color bg = Theme.Gdi(Theme.Bg), bgHot = Theme.Gdi(Theme.Surface), edge = Theme.Gdi(Theme.LineStrong), edgeHot = Theme.Gdi(Theme.Ident[0]);
    readonly SolidBrush fg = new SolidBrush(Theme.Gdi(Theme.Fg)), mutedB = new SolidBrush(Theme.Gdi(Theme.Muted)), track = new SolidBrush(Theme.Gdi("#223249"));
    readonly SolidBrush ok = new SolidBrush(Theme.Gdi(Theme.Ident[0])), hot = new SolidBrush(Theme.Gdi(Theme.Hot));
    readonly SolidBrush warm = new SolidBrush(Theme.Gdi(Theme.Warn)), red = new SolidBrush(Theme.Gdi(Theme.Crit));
    readonly SolidBrush cpuB = new SolidBrush(Theme.Gdi(Theme.Ident[1])), gpuB = new SolidBrush(Theme.Gdi(Theme.Ident[2]));

    public static void Start() {
        bool first;
        using (new Mutex(true, "NJ-Flush-Widget", out first)) {
            if (!first) return;  // one widget only
            Application.EnableVisualStyles();
            Live.WriteBusy = true; Live.Start();   // the sampler: CPU history for the CPU flush, the efficiency guard
            Application.Run(Current = new Widget());
        }
    }

    Widget() {
        // the screen's scale from a screen DC: CreateGraphics() here would create the window now, and the property changes below
        // would then recreate it, hooking the desktop event twice (the 1.3 widget crash of 2026-11-27, c0000005 in PeekMessage)
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) sc = ForceScale > 0 ? ForceScale : g.DpiX / 96f;
        AutoScaleMode = AutoScaleMode.None;
        face = FontFamily.Families.Any(f => string.Equals(f.Name, "Thmanyah Sans", StringComparison.OrdinalIgnoreCase)) ? "Thmanyah Sans" : "Segoe UI";
        big = new Font(face, 13.5f, FontStyle.Bold); small = new Font(face, 9f); label = new Font(face, 8f, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = Want;
        BackColor = bg;
        Opacity = Dim;
        DoubleBuffered = true;
        Pin();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate { Pin(); };

        RightToLeft = Tx.Ar ? RightToLeft.Yes : RightToLeft.No; shownLang = (Tx.Ar ? "ar" : "en") + Settings.Language;   // tips read from the right in Arabic
        menu = BuildMenu(); ContextMenuStrip = menu;
        // the icon in the hidden icons (Nader, "like NJ-Sort"): left-click opens the panel, right-click the same menu
        if (!RenderOnly) {
            tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), Text = "NJ-Flush", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) Panel.Open(); };
            FormClosed += delegate { tray.Visible = false; tray.Dispose(); };
        }
        tip = new ToolTip { InitialDelay = 500 };
        MouseEnter += delegate { hovered = true; Opacity = Lit; tip.SetToolTip(this, TipText()); Invalidate(); };
        MouseLeave += delegate { hovered = false; if (!dragging) Opacity = Dim; Invalidate(); };
        DoubleClick += delegate { if (!moved) Panel.Open(); };   // the widget opens the panel; a drag is not a click
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { downAt = Cursor.Position; from = Location; dragging = true; moved = false; } };
        MouseMove += delegate { if (dragging) Drag(); };
        MouseUp += delegate {
            if (!dragging) return;
            dragging = false; Cursor = Cursors.Default;
            if (moved) { Settings.WidgetX = Location.X; Settings.WidgetY = Location.Y; Settings.Save(); }
        };

        try { File.Delete(App.Exe + ".old"); } catch (Exception) { }   // left by the last update
        if (RenderOnly) { Sample(); return; }
        var upd = new System.Windows.Forms.Timer { Interval = 15000 };    // first check 15 s after start, then every 24 h
        upd.Tick += delegate { upd.Interval = 24 * 3600 * 1000; if (Settings.AutoUpdate) CheckInBackground(); };
        upd.Start();
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += delegate { Sample(); };
        timer.Start();
        Sample();
    }

    void ApplyLanguage() {
        try { RightToLeft = Tx.Ar ? RightToLeft.Yes : RightToLeft.No; } catch (Exception) { }
        var old = menu; menu = BuildMenu(); ContextMenuStrip = menu;
        if (tray != null) tray.ContextMenuStrip = menu;
        if (old != null) try { old.Dispose(); } catch (Exception) { }
        gbText = ""; Invalidate();   // the next sample repaints with the new text
    }
    string shownLang = "";
    ToolStripMenuItem allItem; readonly ToolStripMenuItem[] oneItems = new ToolStripMenuItem[3];
    NotifyIcon tray;
    bool dragging, moved; Point downAt, from;

    void Drag() {
        var c = Cursor.Position;
        if (!moved && Math.Abs(c.X - downAt.X) + Math.Abs(c.Y - downAt.Y) < S(4)) return;   // a click, not yet a drag
        moved = true; Cursor = Cursors.SizeAll;
        var p = new Point(from.X + c.X - downAt.X, from.Y + c.Y - downAt.Y);
        Location = Snap(p, Size, Screen.FromPoint(c).WorkingArea, S(12));
    }
    // inside the working area, and onto an edge when it is within `snap` px of it
    public static Point Snap(Point p, Size size, Rectangle wa, int snap) {
        int x = Math.Max(wa.Left, Math.Min(wa.Right - size.Width, p.X)), y = Math.Max(wa.Top, Math.Min(wa.Bottom - size.Height, p.Y));
        if (x - wa.Left < snap) x = wa.Left; if (wa.Right - size.Width - x < snap) x = wa.Right - size.Width;
        if (y - wa.Top < snap) y = wa.Top; if (wa.Bottom - size.Height - y < snap) y = wa.Bottom - size.Height;
        return new Point(x, y);
    }

    string TipText() {
        var sb = new StringBuilder();
        sb.Append(Tx.F("tip.ram", m.avail / 1073741824.0)).Append("\n").Append(Tx.F("tip.cpu", cpu));
        lock (Live.L) if (Live.GpuName != null) sb.Append("\n").Append(Tx.F("tip.vram", Live.VramUsed, Live.VramTotal, Live.GpuName));
        if (Settings.Guard) sb.Append("\n").Append(Tx.F("tip.guard", Guard.Paused ? Tx.T("tip.guard_paused") : Tx.F("tip.guard_kept", Tx.Plural("process", Guard.Kept))));
        sb.Append("\n").Append(Tx.F("last.line", Settings.Last())).Append("\n").Append(Tx.T("tip.dbl"));
        if (Settings.Hotkey && Settings.Keys_[0].Length > 0) sb.Append("\n").Append(Tx.F("tip.key", Settings.Keys_[0]));
        if (updateTag != null) sb.Append("\n").Append(Tx.F("tip.update", updateTag));
        return sb.ToString();
    }

    // tool window (no taskbar / Alt+Tab), never takes focus
    protected override CreateParams CreateParams {
        get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);  // Win11 rounded corners
        if (RenderOnly) { Size = Want; return; }
        WatchDesktop();
        ApplySettings();   // registers the shortcuts; one taken by another app is reported in Settings
    }
    protected override void OnHandleDestroyed(EventArgs e) { for (int i = 1; i <= 5; i++) UnregisterHotKey(Handle, i); base.OnHandleDestroyed(e); }

    protected override void OnShown(EventArgs e) {
        base.OnShown(e);
        GC.Collect();
        EmptyWorkingSet(Process.GetCurrentProcess().Handle);  // start lean
    }

    // WM_WINDOWPOSCHANGING: always insert at HWND_BOTTOM, so it sits on the desktop under every window
    protected override void WndProc(ref Message msg) {
        if (msg.Msg == 0x46 && !raised) {
            var wp = (WindowPos)Marshal.PtrToStructure(msg.LParam, typeof(WindowPos));
            wp.after = new IntPtr(1);  // shown desktop (raised): Restack() places it, topmost
            wp.flags &= ~0x4u;  // clear SWP_NOZORDER
            Marshal.StructureToPtr(wp, msg.LParam, false);
        }
        if (msg.Msg == 0x312) {  // WM_HOTKEY: id 1..5 = Settings.Actions
            int id = msg.WParam.ToInt32() - 1;
            if (id == 4) Panel.Open(); else if (id >= 0 && id < 4) TryFlush(Settings.Actions[id]);
        }
        base.WndProc(ref msg);
    }

    // 1.2.4: "Show desktop" (the corner button, Win+D) raises the desktop over every window, this one included. When the desktop
    // becomes the foreground window the widget lifts to the top (no focus taken); when any other window does, it goes back to the bottom.
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    IntPtr hookHandle;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    WinEventProc onForeground;  // kept alive for the hook
    bool raised;
    readonly System.Windows.Forms.Timer restack = new System.Windows.Forms.Timer { Interval = 300 };
    void Restack() {  // shown desktop: topmost (it sits above normal windows); otherwise not topmost, at the bottom. NOSIZE|NOMOVE|NOACTIVATE
        if (raised) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        else { SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }
    }
    // installed once per process: the delegate must outlive the hook, so a second call (a recreated handle) changes nothing
    void WatchDesktop() {
        if (hookHandle != IntPtr.Zero) return;
        onForeground = (hook, ev, h, obj, child, thread, time) => {
            var cls = new StringBuilder(32); GetClassName(h, cls, 32);
            bool desk = cls.ToString() == "WorkerW" || cls.ToString() == "Progman";
            if (h == Handle) return;
            raised = desk;
            Restack();
            if (desk) { restack.Stop(); restack.Start(); }  // the shell re-stacks the desktop just after the event: lift again once it settled
        };
        restack.Tick += delegate { restack.Stop(); if (raised) Restack(); };
        hookHandle = SetWinEventHook(3, 3, IntPtr.Zero, onForeground, 0, 0, 0);  // EVENT_SYSTEM_FOREGROUND, out of context
        FormClosed += delegate { if (hookHandle != IntPtr.Zero) UnhookWinEvent(hookHandle); hookHandle = IntPtr.Zero; };
    }

    // the saved spot when it is still on a screen (a monitor may be gone); otherwise just above the taskbar clock
    void Pin() {
        if (Settings.WidgetX >= 0 && Settings.WidgetY >= 0) {
            var r = new Rectangle(Settings.WidgetX, Settings.WidgetY, Width, Height);
            Location = Snap(r.Location, Size, Screen.FromRectangle(r).WorkingArea, 0);
            return;
        }
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - S(10), wa.Bottom - Height - S(8));
    }

    ToolTip tip;
    ToolStripMenuItem updateItem;
    void CheckInBackground() {
        new Thread(() => {
            var i = Updater.Check();
            if (i.Error == null && i.Newer) BeginInvoke((Action)delegate {
                updateTag = i.Tag.TrimStart('v');
                updateItem.Text = Tx.F("menu.update_to", updateTag); updateItem.Font = new Font(updateItem.Font, FontStyle.Bold);
            });
        }) { IsBackground = true }.Start();
    }
    // every flush request from the widget, the menu or a shortcut goes through here: it answers at once (L17) and explains a refusal
    public void TryFlush(string kind, bool deep = false) {
        int left = Settings.CooldownLeft(kind);
        if (left > 0) { Say(kind == "all" ? Tx.F("say.cooldown_all", left) : Tx.F("say.cooldown_kind", kind.ToUpperInvariant(), left), 2000); return; }
        if (kind == "ram" && !deep && pct * 100 < Settings.MinRam) { Say(Tx.F("say.ram_fine", pct * 100), 2500); return; }
        if ((DateTime.Now - lastTry).TotalSeconds < 3) return;  // a second request while the first is still starting
        lastTry = DateTime.Now;
        Say(Tx.T(kind == "all" ? "say.all" : kind == "ram" ? (deep ? "say.deep" : "say.ram") : kind == "cpu" ? "say.cpu" : "say.gpu"), 1500);
        App.FlushNow(kind, false, deep);
    }
    public void Say(string text, int ms) { if (InvokeRequired) { BeginInvoke((Action)delegate { Say(text, ms); }); return; } tip.Show(text, this, 0, -S(48), ms); }
    DateTime lastTry = DateTime.MinValue;

    bool commitTight;
    int highFor; DateTime lastAuto = DateTime.MinValue;
    void Sample() {
        // a double-click on NJ-Flush.exe asked for the panel (App.Launch): open it, or bring it forward and reload it
        try {
            if (File.Exists(App.PanelRequest)) {
                bool fresh = (DateTime.Now - File.GetLastWriteTime(App.PanelRequest)).TotalSeconds < 30;
                File.Delete(App.PanelRequest);
                if (fresh) Panel.Open(null, true);
            }
        } catch (Exception) { }
        if (!Mem.Status(ref m)) return;
        double total = m.total / 1073741824.0, used = (m.total - m.avail) / 1073741824.0;
        // auto-flush (RAM only, never restarts the GPU): RAM at or above the threshold for 30 s in a row, at most once per 10 minutes
        highFor = used / total * 100 >= Settings.AutoAt ? highFor + 1 : 0;
        if (Settings.AutoFlush && highFor >= 30 && (DateTime.Now - lastAuto).TotalMinutes >= 10) { lastAuto = DateTime.Now; highFor = 0; App.FlushNow("ram", true); }
        int cd = Settings.CooldownLeft("ram");
        string p = string.Format("{0:0}%", used / total * 100), g = cd > 0 ? Tx.F("w.next", cd) : string.Format("{0:0.0} / {1:0.0} GB", used, total);
        bool tight = m.tPage > 0 && (m.tPage - m.aPage) / (double)m.tPage >= 0.9;  // reserved memory near its limit (2026-11-23)
        float c, v; bool gpu;
        lock (Live.L) { c = (float)Math.Round(Live.Cpu); v = (float)Math.Round(Live.Vram); gpu = Live.GpuName != null; }
        string tt = Tx.F("tray.base", p, c) + (gpu ? Tx.F("tray.vram", v) : "");
        if (tray != null && tray.Text != tt) tray.Text = tt.Length > 63 ? tt.Substring(0, 63) : tt;   // a tray tooltip holds 63 characters
        if (p == pctText && g == gbText && tight == commitTight && c == cpu && v == vram && gpu == hasGpu) return;  // repaint only on change
        pct = (float)(used / total); pctText = p; gbText = g; commitTight = tight; cpu = c; vram = v; hasGpu = gpu;
        Invalidate();
    }
}
