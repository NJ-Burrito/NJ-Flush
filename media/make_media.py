"""Builds NJ-Flush's marketing media (hero, clips, banner, OG, logos) into docs/.

Run:  uv run --with pillow --with playwright python media/make_media.py [--rerender] [stage ...]
Stages: frames hero clips stills banner logos   (default: all)

Frames come from the product's own off-screen renderer (dist\\NJ-Flush.exe /render, 15 fps), never a screen
recording. Work files go to NJFLUSH_MEDIA_WORK (default: media-work/ beside the repo). The Thmanyah font files
are read from NJ_FONTS (a folder you are licensed to use) for the HTML templates only; no font file is copied into
the repo.
"""
import os, subprocess, sys, shutil
from pathlib import Path
from PIL import Image

os.environ.setdefault("PYTHONUTF8", "1")
ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
TPL = ROOT / "media"
WORK = Path(os.environ.get("NJFLUSH_MEDIA_WORK") or (ROOT.parent / "media-work"))
FR = WORK / "fr"
FONTS = Path(os.environ.get("NJ_FONTS") or (ROOT / "fonts-not-in-repo"))   # set NJ_FONTS to your licensed Thmanyah folder
EXE = ROOT / "dist" / "NJ-Flush.exe"
FPS = 15

# the art tile inside the 640x647 render frame (caption and "preview" badge sit below it and are cropped away)
CROP = (110, 40, 530, 460)
BG = (3, 7, 15)

# (variant, first frame, last frame, label) - time-lapsed onto SHOT_N output frames each
HERO = [
    ("clogged", 6, 80, "RAM", "clogged pipes"),
    ("cpu-3", 2, 100, "CPU", "meltdown"),
    ("gpu-3", 4, 92, "GPU", "frozen"),
    ("all-3", 4, 112, "Flush all", "flood"),
]
SHOT_N = 36
TITLE_N = 18
FADE_N = 6          # last shot dissolves back into the title card: the loop closes there

CLIPS = {  # kind: (variant, first, last, frames)
    "ram": ("heavy", 2, 62, 60),
    "cpu": ("cpu-3", 2, 100, 60),
    "gpu": ("gpu-3", 4, 92, 60),
    "all": ("all-3", 4, 112, 66),
}
LOOP_X = 8


def sh(*a, **k):
    k.setdefault("check", True)
    return subprocess.run([str(x) for x in a], **k)


def log(*a):
    print(*a, flush=True)


# ---------------------------------------------------------------- frames
def stage_frames(rerender):
    FR.mkdir(parents=True, exist_ok=True)
    need = {v for v, *_ in HERO} | {c[0] for c in CLIPS.values()}
    for v in sorted(need):
        d = FR / v
        if d.exists() and any(d.glob(f"{v}-f*.png")) and not rerender:
            continue
        d.mkdir(parents=True, exist_ok=True)
        env = dict(os.environ, NJFLUSH_RENDER_FPS=str(FPS))
        log("render", v)
        p = subprocess.run([str(EXE), "/render", v, str(d)], env=env, timeout=300)  # waits for exit
        if p.returncode != 0:
            raise SystemExit(f"/render {v} exit {p.returncode}")


def count(v):
    return len(list((FR / v).glob(f"{v}-f*.png")))


_cache = {}


def art(v, i):
    i = max(0, min(count(v) - 1, int(round(i))))
    k = (v, i)
    if k not in _cache:
        _cache[k] = Image.open(FR / v / f"{v}-f{i:05d}.png").convert("RGB").crop(CROP)
    return _cache[k]


def lerp(a, b, t):
    return a + (b - a) * t


def place(canvas_size, v, pos, art_px, zoom=1.0, margin_top=None):
    """Composite one scene frame onto the dark brand canvas. pos may be fractional (blend of two frames)."""
    W = canvas_size
    lo = int(pos)
    im = art(v, lo)
    if pos - lo > 1e-3 and lo + 1 < count(v):
        im = Image.blend(im, art(v, lo + 1), pos - lo)
    if zoom != 1.0:
        w = CROP[2] - CROP[0]
        c = w / zoom
        o = (w - c) / 2
        im = im.resize((art_px, art_px), Image.LANCZOS, box=(o, o, o + c, o + c))
    else:
        im = im.resize((art_px, art_px), Image.LANCZOS)
    cv = Image.new("RGB", (W, W), BG)
    top = (W - art_px) // 2 if margin_top is None else margin_top
    cv.paste(im, ((W - art_px) // 2, top))
    return cv


# ---------------------------------------------------------------- html overlays
class Shooter:
    def __init__(self):
        from playwright.sync_api import sync_playwright
        self._pw = sync_playwright().start()
        self._b = self._pw.chromium.launch()
        self.fonts = FONTS.as_uri()

    def shot(self, html, out, w, h, transparent=False):
        f = WORK / "_page.html"
        f.write_text(html, encoding="utf-8")
        pg = self._b.new_page(viewport={"width": w, "height": h})
        pg.goto(f.as_uri())
        pg.evaluate("document.fonts.ready")
        pg.wait_for_timeout(250)
        pg.screenshot(path=str(out), omit_background=transparent)
        pg.close()

    def close(self):
        self._b.close()
        self._pw.stop()


def card_html(s, mode, body):
    t = (TPL / "card.html").read_text(encoding="utf-8")
    return t.replace("{{FONTS}}", s.fonts).replace("{{MODE}}", mode).replace("{{BODY}}", body)


def make_overlays(s):
    ov = WORK / "ov"
    ov.mkdir(exist_ok=True)
    shutil.copy(ROOT / "logo-1024.png", WORK / "logo-1024.png")
    logo = (WORK / "logo-1024.png").as_uri()
    title = (f'<div class="wrap"><img class="logo" src="{logo}"><h1>NJ-<b>Flush</b></h1>'
             f'<p>Flush RAM, CPU and GPU</p><div class="chip">1.3 &nbsp;·&nbsp; FREE FOR WINDOWS</div></div>')
    s.shot(card_html(s, "title", title), ov / "title.png", 720, 720)
    pills = {"clogged": ("RAM", "clogged pipes"), "cpu": ("CPU", "meltdown"), "gpu": ("GPU", "frozen"),
             "all": ("Flush all", "flood"), "done": ("Flush all", "all clean")}
    for k, (a, b) in pills.items():
        s.shot(card_html(s, "pill", f'<div class="pill"><i></i>{a}<span>{b}</span></div>'),
               ov / f"pill-{k}.png", 720, 720, transparent=True)
    return ov


def over(base, png, alpha=1.0):
    o = Image.open(png).convert("RGBA")
    if alpha < 1:
        a = o.getchannel("A").point(lambda x: int(x * alpha))
        o.putalpha(a)
    b = base.convert("RGBA")
    b.alpha_composite(o)
    return b.convert("RGB")


# ---------------------------------------------------------------- hero
def hero_frames(ov, out):
    out.mkdir(parents=True, exist_ok=True)
    for f in out.glob("*.png"):
        f.unlink()
    title = Image.open(ov / "title.png").convert("RGB")
    seq = []
    for _ in range(TITLE_N):
        seq.append(title)
    keys = ["clogged", "cpu", "gpu", "all"]
    ART, TOP = 612, 22
    for si, (v, a, b, _k, _l) in enumerate(HERO):
        for k in range(SHOT_N):
            t = k / (SHOT_N - 1)
            pos = lerp(a, b, t)
            fr = place(720, v, pos, ART, zoom=lerp(1.0, 1.045, t), margin_top=TOP)
            fr = over(fr, ov / f"pill-{keys[si]}.png", min(1.0, (k + 1) / 4))
            if k < 2:  # cut flash on the beat
                fl = Image.new("RGB", fr.size, (34, 211, 238))
                fr = Image.blend(fr, fl, 0.22 if k == 0 else 0.08)
            if si == len(HERO) - 1 and k >= SHOT_N - FADE_N:
                w = (k - (SHOT_N - FADE_N) + 1) / (FADE_N + 1)
                fr = Image.blend(fr, title, w)
            seq.append(fr)
    for i, fr in enumerate(seq):
        fr.save(out / f"f{i:04d}.png")
    return len(seq)


def encode(src, dst_base, w, crf_h264=23, crf_vp9=34, gif=False, gif_w=None, gif_colors=128, name="f%04d.png", audio=None):
    pat = str(src / name)
    vf = f"scale={w}:{w}:flags=lanczos,format=yuv420p"
    sh("ffmpeg", "-v", "error", "-y", "-framerate", FPS, "-i", pat, "-vf", vf, "-c:v", "libx264", "-preset", "veryslow",
       "-crf", crf_h264, "-movflags", "+faststart", "-an", str(dst_base) + ".mp4")
    sh("ffmpeg", "-v", "error", "-y", "-framerate", FPS, "-i", pat, "-vf", vf, "-c:v", "libvpx-vp9", "-crf", crf_vp9, "-b:v", "0",
       "-row-mt", "1", "-cpu-used", "0", "-deadline", "good", "-an", str(dst_base) + ".webm")


def make_gif(src, dst, w, colors=128, dither="bayer:bayer_scale=4"):
    pal = WORK / "pal.png"
    sh("ffmpeg", "-v", "error", "-y", "-framerate", FPS, "-i", str(src / "f%04d.png"),
       "-vf", f"fps=12.5,scale={w}:{w}:flags=lanczos,palettegen=max_colors={colors}:stats_mode=diff", str(pal))
    sh("ffmpeg", "-v", "error", "-y", "-framerate", FPS, "-i", str(src / "f%04d.png"), "-i", str(pal), "-lavfi",
       f"fps=12.5,scale={w}:{w}:flags=lanczos[x];[x][1:v]paletteuse=dither={dither}:diff_mode=rectangle", "-loop", "0", str(dst))


def stage_hero(s):
    ov = make_overlays(s)
    hd = WORK / "hero-seq"
    n = hero_frames(ov, hd)
    log("hero frames", n, f"{n / FPS:.1f}s")
    encode(hd, DOCS / "hero", 720)
    make_gif(hd, DOCS / "hero.gif", 480, 128, "bayer:bayer_scale=5")
    # poster and still: the best single frames
    k = TITLE_N + 3 * SHOT_N + 12  # flood with the shower running
    Image.open(hd / f"f{k:04d}.png").save(DOCS / "hero-poster.jpg", quality=88, optimize=True)
    # reduced-motion still: everything clean, check marks on
    keep = place(720, "all-3", count("all-3") - 6, 612, margin_top=22)
    over(keep, ov / "pill-done.png").save(DOCS / "hero-still.jpg", quality=88, optimize=True)


# ---------------------------------------------------------------- clips
def stage_clips():
    for kind, (v, a, b, n) in CLIPS.items():
        d = WORK / f"clip-{kind}"
        d.mkdir(exist_ok=True)
        for f in d.glob("*.png"):
            f.unlink()
        N = n + LOOP_X
        L = n
        src = [place(480, v, lerp(a, b, i / (N - 1)), 456) for i in range(N)]
        out = []
        for k in range(L):
            if k < LOOP_X:
                w = (k + 1) / (LOOP_X + 1)
                out.append(Image.blend(src[L + k], src[k], w))
            else:
                out.append(src[k])
        for i, fr in enumerate(out):
            fr.save(d / f"f{i:04d}.png")
        (DOCS / "clips").mkdir(exist_ok=True)
        encode(d, DOCS / "clips" / kind, 480, crf_h264=27, crf_vp9=38)
        out[LOOP_X + 14].save(DOCS / "clips" / f"{kind}.jpg", quality=86, optimize=True)
        log("clip", kind, L, f"{L / FPS:.1f}s")


# ---------------------------------------------------------------- banner / og
def stage_banner(s):
    shutil.copy(ROOT / "logo-1024.png", WORK / "logo-1024.png")
    picks = {"clogged": ("clogged", 40), "cpu": ("cpu-3", 22), "gpu": ("gpu-3", 16), "all": ("all-3", 42)}
    for k, (v, i) in picks.items():
        art(v, i).save(WORK / f"s-{k}.png")
    t = (TPL / "banner.html").read_text(encoding="utf-8")
    t = t.replace("{{FONTS}}", s.fonts).replace("{{WORK}}", WORK.as_uri())
    for name, W, H in (("banner", 1280, 640), ("og-image", 1200, 630)):
        sc = W / 1280
        top = (H - 640 * sc) / 2
        s.shot(t.replace("{{W}}", str(W)).replace("{{H}}", str(H)).replace("{{S}}", f"{sc:.5f}").replace("{{TOP}}", f"{top:.1f}"),
               DOCS / f"{name}.png", W, H)
        log(name, W, H)


def stage_logos():
    im = Image.open(ROOT / "logo-1024.png")
    shutil.copy(ROOT / "logo-1024.png", DOCS / "logo-1024.png")
    im.convert("RGBA").resize((256, 256), Image.LANCZOS).save(DOCS / "logo.png", optimize=True)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    stages = args or ["frames", "logos", "hero", "clips", "banner"]
    stage_frames("--rerender" in sys.argv)
    if "logos" in stages:
        stage_logos()
    if any(x in stages for x in ("hero", "banner")):
        s = Shooter()
        try:
            if "hero" in stages:
                stage_hero(s)
            if "banner" in stages:
                stage_banner(s)
        finally:
            s.close()
    if "clips" in stages:
        stage_clips()


if __name__ == "__main__":
    main()
