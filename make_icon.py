"""Builds the NJ-Flush icon set from the vector masters in brand/.

  brand/logo.svg        full mark (40 px and up)
  brand/logo-small.svg  simplified, bolder variant (16, 20, 24, 32 px)

Writes logo-1024.png, NJ-Flush.png (256 px), NJ-Flush.ico (16-256 px, 9 frames).
Run:  uv run --with pillow --with playwright python make_icon.py
Needs Chromium from PLAYWRIGHT_BROWSERS_PATH (headless render of the SVG). See brand/README.md.
"""
import io
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

here = Path(__file__).parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SMALL_MAX = 32


def render(page, svg_text, size):
    body = svg_text.split("?>", 1)[-1].replace('width="1024" height="1024"', f'width="{size}" height="{size}"', 1)
    page.set_viewport_size({"width": size, "height": size})
    page.set_content(f'<html><body style="margin:0;background:transparent">{body}</body></html>')
    return Image.open(io.BytesIO(page.screenshot(omit_background=True))).convert("RGBA")


def render_all():
    full = (here / "brand" / "logo.svg").read_text(encoding="utf-8")
    small = (here / "brand" / "logo-small.svg").read_text(encoding="utf-8")
    out = {}
    with sync_playwright() as p:
        b = p.chromium.launch()
        pg = b.new_page()
        for s in SIZES:
            out[s] = render(pg, small if s <= SMALL_MAX else full, s)
        out[1024] = render(pg, full, 1024)
        b.close()
    return out


if __name__ == "__main__":
    f = render_all()
    f[1024].save(here / "logo-1024.png")
    f[256].save(here / "NJ-Flush.png")
    # Pillow writes each requested size from append_images only when passed explicitly
    f[256].save(here / "NJ-Flush.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=[f[s] for s in SIZES if s != 256])
    print("ok", sorted(Image.open(here / "NJ-Flush.ico").info.get("sizes", [])))
