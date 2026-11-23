"""Turns logo-1024.png (rendered by make_logo.ps1) into NJ-Flush.ico (16-256 px) and NJ-Flush.png (256 px)."""
from pathlib import Path
from PIL import Image

here = Path(__file__).parent
img = Image.open(here / "logo-1024.png").convert("RGBA")
img.resize((256, 256), Image.LANCZOS).save(here / "NJ-Flush.png")
img.save(here / "NJ-Flush.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
