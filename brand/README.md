# NJ-Flush brand

**Concept.** A toilet seen from above, drawn as a white porcelain tank and bowl on a dark navy tile, with a cyan
(#22D3EE) whirlpool in the bowl: the same bathroom world as the animations. Light comes from the top-left; layered
gradients, a rim specular, a soft contact shadow, a cyan glow and rim line on the tile. No text.

| File | Use |
|---|---|
| `logo.svg` | master, 1024 viewBox, full detail (frames 40 px and up, `logo-1024.png`, `NJ-Flush.png`) |
| `logo-small.svg` | simplified, bolder variant, no blur filters, for the 16/20/24/32 px frames |
| `make_sheet.py` | check sheet: frames read back from `NJ-Flush.ico` on dark, light and a taskbar strip |

## Regenerate

```
pwsh -File make_logo.ps1
# or: uv run --with pillow --with playwright python make_icon.py
uv run --with pillow python brand/make_sheet.py <output-folder>/sheet.png
```

Outputs: `logo-1024.png`, `NJ-Flush.png` (256 px), `NJ-Flush.ico` (16, 20, 24, 32, 40, 48, 64, 128, 256).
`build.cmd` embeds the .ico with `/win32icon`. Rendering is headless Chromium (Playwright) of the SVG.

## Credits

All art is original. No third-party artwork; Pillow (HPND) and Playwright (Apache-2.0) are build tools only.
