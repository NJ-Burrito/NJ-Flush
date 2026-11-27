// NJ-Flush 1.3: off-screen renders of the panel and the widget, for design checks (NJ-Kit nj-art-crew): nothing shows on the
// screen, nothing is flushed, no tray icon or shortcut is registered.
//   NJ-Flush.exe /render-ui panel-ram|panel-cpu|panel-gpu|panel-guard|panel-settings <dir>  -> <what>.png (the whole panel, live data)
//   NJ-Flush.exe /render-ui widget <dir>  -> widget.png, widget-hover.png, widget-2x.png (normal, hovered, and at 200% for detail)
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using W = System.Windows;
using WM = System.Windows.Media;

static class UiRender {
    public static void Run(string what, string dir) {
        Directory.CreateDirectory(dir);
        if (what == "widget") { RenderWidget(dir); return; }
        if (!what.StartsWith("panel")) return;
        View.Still = true;   // no entrance animation: the picture is the finished page
        Panel.StartPage = what.Length > 6 ? what.Substring(6) : "ram";
        Live.WantDetail = true; Live.Start();
        var view = new View(); var win = view.Window;
        win.WindowStartupLocation = W.WindowStartupLocation.Manual; win.Left = -32000; win.Top = 0; win.ShowActivated = false; win.ShowInTaskbar = false;
        var t = new W.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };   // the sampler fills lists and graphs first
        t.Tick += delegate {
            t.Stop();
            var fe = (W.FrameworkElement)win.Content;
            var bmp = new WM.Imaging.RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, WM.PixelFormats.Pbgra32);
            bmp.Render(fe);
            // a right-to-left window is mirrored by its own root visual, which RenderTargetBitmap leaves out of a render of its content: put it back
            WM.Imaging.BitmapSource shot = bmp;
            if (Tx.Ar) { var flip = new WM.Imaging.TransformedBitmap(bmp, new WM.ScaleTransform(-1, 1)); flip.Freeze(); shot = flip; }
            var enc = new WM.Imaging.PngBitmapEncoder(); enc.Frames.Add(WM.Imaging.BitmapFrame.Create(shot));
            using (var f = File.Create(Path.Combine(dir, what + ".png"))) enc.Save(f);
            win.Close();
        };
        win.Loaded += delegate { t.Start(); };
        win.Closed += delegate { W.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(W.Threading.DispatcherPriority.Background); };
        int tall; if (int.TryParse(Environment.GetEnvironmentVariable("NJFLUSH_RENDER_H"), out tall) && tall >= 300) { win.MinHeight = tall; win.Height = tall; }   // a taller picture, to see a long page whole
        win.Show();
        W.Threading.Dispatcher.Run();
    }

    // widget.png (live values, 100%), widget-hover.png, widget-load.png (RAM 93%, CPU 97%, VRAM 84%), widget-slim.png (no bars),
    // widget-125.png (125% scale), widget-2x.png (the 100% one doubled, for detail)
    static void RenderWidget(string dir) {
        Widget.RenderOnly = true; View.Still = true;
        Live.Start();
        System.Threading.Thread.Sleep(3500);   // a few samples, so CPU and VRAM have values
        Application.EnableVisualStyles();
        var w = Widget.ForRender();
        w.StartPosition = FormStartPosition.Manual; w.Location = new Point(-32000, 0);
        w.Show(); w.Refresh();
        Func<Widget, Bitmap> grab = f => { var b = new Bitmap(f.Width, f.Height); f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); return b; };
        Action<Widget, string> save = (f, name) => { using (var b = grab(f)) b.Save(Path.Combine(dir, name), System.Drawing.Imaging.ImageFormat.Png); };
        if (Live.Demo) w.Demo(0.58f, 23, 42, true, false);   // NJFLUSH_DEMO: the demo numbers, not this PC's
        w.SetHover(false); w.Refresh(); save(w, "widget.png");
        using (var b = grab(w)) using (var big = new Bitmap(b, w.Width * 2, w.Height * 2)) big.Save(Path.Combine(dir, "widget-2x.png"), System.Drawing.Imaging.ImageFormat.Png);
        w.SetHover(true); w.Refresh(); save(w, "widget-hover.png"); w.SetHover(false);
        w.Demo(0.93f, 97, 84, true, false); w.Refresh(); save(w, "widget-load.png");
        w.Close();
        // 125%: a second form made at that scale (the scale is read once, in the constructor)
        Widget.ForceScale = 1.25f;
        var w2 = Widget.ForRender(); w2.StartPosition = FormStartPosition.Manual; w2.Location = new Point(-32000, 0); w2.Show(); w2.Refresh();
        w2.Demo(0.47f, 38, 29, true, false); w2.Refresh(); save(w2, "widget-125.png");
        w2.Close();
        Settings.WidgetBars = false;   // not saved: only this picture
        Widget.ForceScale = 1f;
        var w3 = Widget.ForRender(); w3.StartPosition = FormStartPosition.Manual; w3.Location = new Point(-32000, 0); w3.Show(); w3.Refresh();
        w3.Demo(0.47f, 38, 29, true, false); w3.Refresh(); save(w3, "widget-slim.png");
        w3.Close();
    }
}
