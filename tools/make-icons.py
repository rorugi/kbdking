"""Builds the icons and installer pictures from the logo.

    python tools/make-icons.py [source.png]

Writes assets/logo.png (cleaned, square), the app/CLI icon src/Kbdking.App/Assets/kbdking.ico, a PNG for the
About page and the installer bitmaps installer/banner.bmp (493x58) and installer/dialog.bmp (493x312).
Needs Pillow.
"""
import sys
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parent.parent
source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "assets" / "logo.png"

logo = Image.open(source).convert("RGBA")
# Drop the faint stray pixels around the drawing, then crop to it and centre it on a square canvas.
alpha = logo.getchannel("A").point(lambda v: 0 if v < 16 else v)
logo.putalpha(alpha)
logo = logo.crop(alpha.getbbox())
side = max(logo.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(logo, ((side - logo.width) // 2, (side - logo.height) // 2), logo)
square = square.resize((1024, 1024), Image.LANCZOS)
square.save(root / "assets" / "logo.png", optimize=True)

app_assets = root / "src" / "Kbdking.App" / "Assets"
app_assets.mkdir(parents=True, exist_ok=True)
square.save(app_assets / "kbdking.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
square.resize((256, 256), Image.LANCZOS).save(app_assets / "kbdking.png", optimize=True)


def on_background(size, background, logo_size, position):
    picture = Image.new("RGB", size, background)
    small = square.resize((logo_size, logo_size), Image.LANCZOS)
    picture.paste(small, position, small)
    return picture


# WixUI pictures: the dialog picture's left part shows on the welcome and finish pages, the banner on top of the others.
dialog = Image.new("RGB", (493, 312), (255, 255, 255))
panel = on_background((164, 312), (236, 241, 251), 112, (26, 100))
dialog.paste(panel, (0, 0))
dialog.save(root / "installer" / "dialog.bmp")
on_background((493, 58), (255, 255, 255), 46, (437, 6)).save(root / "installer" / "banner.bmp")
print("Icons written.")
