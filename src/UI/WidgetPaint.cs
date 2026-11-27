// NJ-Flush: how the widget draws itself.
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

partial class Widget {
    // one colour scale everywhere: the resource's own colour, yellow from 80%, orange from 90%, red from 96% (the RAM tiers)
    SolidBrush LoadBrush(float f, SolidBrush own) { return f >= 0.96f ? red : f >= 0.9f ? hot : f >= 0.8f ? warm : own; }
    static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float rad) {
        var gp = new System.Drawing.Drawing2D.GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { gp.AddRectangle(r); return gp; }
        gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90); gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        gp.CloseFigure(); return gp;
    }
    // x is the left edge in left-to-right terms; in Arabic the bar is mirrored and fills from the right
    float paintW;
    void Bar(Graphics gr, float x, float y, float w, float h, float frac, Brush fill) {
        if (Tx.Ar) x = paintW - x - w;
        using (var t = Round(new RectangleF(x, y, w, h), h / 2)) gr.FillPath(track, t);
        float fw = Math.Max(0, Math.Min(1, frac)) * w;
        if (fw >= h) using (var f = Round(new RectangleF(Tx.Ar ? x + w - fw : x, y, fw, h), h / 2)) gr.FillPath(fill, f);
    }

    // off-screen demo values for /render-ui (a high-load picture); the live sampler never runs there
    public void Demo(float ramFrac, float cpuPct, float vramPct, bool gpu, bool tight) {
        pct = ramFrac; pctText = string.Format("{0:0}%", ramFrac * 100); gbText = string.Format("{0:0.0} / 31.9 GB", ramFrac * 31.9);
        cpu = cpuPct; vram = vramPct; hasGpu = gpu; commitTight = tight; Invalidate();
    }

    // the baseline of a font's first line, so text of two sizes can share one baseline
    static float Ascent(Font f, Graphics gr) { var ff = f.FontFamily; return f.Size * gr.DpiY / 72f * ff.GetCellAscent(f.Style) / ff.GetEmHeight(f.Style); }
    static readonly StringFormat Tf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
    float TextW(Graphics gr, string t, Font f) { return gr.MeasureString(t, f, PointF.Empty, Tf).Width; }
    static readonly StringFormat TfR = new StringFormat(Tf) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.DirectionRightToLeft };
    // x and `right` are in left-to-right terms; in Arabic the layout is mirrored (left-aligned text sits at the right edge) and Arabic text reads from the right
    void DrawText(Graphics gr, string t, Font f, Brush b, float x, float baseline, bool right = false) {
        float w = TextW(gr, t, f);
        if (Tx.Ar) { x = paintW - x; right = !right; }
        float left = right ? x - w : x, top = baseline - Ascent(f, gr);
        if (Tx.Ar && Tx.HasArabic(t)) gr.DrawString(t, f, b, new RectangleF(left - 8, top, w + 8, f.Height * 2f), TfR);   // the rectangle's right edge is the text's
        else gr.DrawString(t, f, b, left, top, Tf);
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics gr = e.Graphics;
        gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        gr.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        float W_ = ClientSize.Width, H_ = ClientSize.Height, pad = S(14), top = S(10);
        paintW = W_;
        using (var path = Round(new RectangleF(0.5f, 0.5f, W_ - 1, H_ - 1), S(8))) {
            if (hovered) using (var fill = new SolidBrush(bgHot)) gr.FillPath(fill, path);
            using (var pen = new Pen(hovered ? Color.FromArgb(170, edgeHot) : edge, 1)) gr.DrawPath(pen, path);
        }
        float inner = W_ - 2 * pad;
        // line 1, one baseline: RAM label, the big %, GB on the right
        float b1 = top + S(12);   // cap height of the big figure is about 12 px at 100%
        DrawText(gr, "RAM", label, mutedB, pad, b1);
        SolidBrush rb = pct >= 0.96f ? red : pct >= 0.9f || commitTight ? hot : pct >= 0.8f ? warm : ok;
        DrawText(gr, pctText, big, pct >= 0.8f ? rb : fg, pad + TextW(gr, "RAM", label) + S(6), b1);
        DrawText(gr, gbText, small, mutedB, W_ - pad, b1, true);
        // RAM bar: the colour of the flush it would play (commit near its limit counts as orange)
        Bar(gr, pad, b1 + S(8), inner, S(6), pct, rb);
        if (!Settings.WidgetBars) return;
        // line 2, one baseline: CPU and VRAM, label left and value right, a 4 px bar under each
        float gap = S(16), half = (inner - gap) / 2, b2 = b1 + S(8) + S(6) + S(15);
        Action<string, float, float, SolidBrush> part = (name, val, x, own) => {
            DrawText(gr, name, label, mutedB, x, b2);
            SolidBrush lb = LoadBrush(val / 100f, own);
            DrawText(gr, string.Format("{0:0}%", val), label, lb == own ? fg : lb, x + half, b2, true);
            Bar(gr, x, b2 + S(7), half, S(4), val / 100f, lb);
        };
        part("CPU", cpu, pad, cpuB);
        if (hasGpu) part("VRAM", vram, pad + half + gap, gpuB);
    }
}
