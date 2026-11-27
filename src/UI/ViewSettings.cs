// NJ-Flush panel: the Settings page and the shortcut recorder.
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
