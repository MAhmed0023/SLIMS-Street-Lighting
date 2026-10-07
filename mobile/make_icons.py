"""Draws the SLIMS Field icon (street light on plum) for the app bundle and every Android launcher density.

    python make_icons.py            public/icon.png only
    python make_icons.py android    also android/app/src/main/res/mipmap-*/ (run after `npx cap add android`)
"""
import os
import sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PLUM, TEAL, GOLD = (113, 75, 103, 255), (1, 126, 132, 255), (246, 196, 83, 255)


def draw(size, background=True, inset=0.0):
    """inset shrinks the drawing towards the centre (adaptive-icon foregrounds need a safe zone)."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = size * (1 - 2 * inset) / 256.0
    o = size * inset
    P = lambda x, y: (o + x * s, o + y * s)
    B = lambda x0, y0, x1, y1: [*P(x0, y0), *P(x1, y1)]
    if background:
        d.rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.2), fill=PLUM)
    d.polygon([P(150, 92), P(186, 92), P(222, 214), P(114, 214)], fill=(246, 196, 83, 120))
    d.rectangle(B(70, 70, 86, 214), fill="white")
    d.rounded_rectangle(B(70, 62, 176, 76), radius=max(1, int(7 * s)), fill="white")
    d.rounded_rectangle(B(140, 74, 196, 96), radius=max(1, int(9 * s)), fill="white")
    d.ellipse(B(154, 86, 182, 104), fill=GOLD)
    d.rounded_rectangle(B(54, 206, 102, 220), radius=max(1, int(5 * s)), fill="white")
    d.rectangle(B(40, 220, 216, 226), fill=TEAL)
    return im


os.makedirs(os.path.join(HERE, "public"), exist_ok=True)
draw(512).save(os.path.join(HERE, "public", "icon.png"))
if "android" in sys.argv:
    res = os.path.join(HERE, "android", "app", "src", "main", "res")
    for density, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        folder = os.path.join(res, "mipmap-" + density)
        os.makedirs(folder, exist_ok=True)
        draw(px).save(os.path.join(folder, "ic_launcher.png"))
        draw(px).save(os.path.join(folder, "ic_launcher_round.png"))
        fg = int(px * 108 / 48)                      # adaptive foreground is 108 dp with a 66 dp safe zone
        draw(fg, background=False, inset=0.2).save(os.path.join(folder, "ic_launcher_foreground.png"))
    # splash screens: plum, with the icon in the middle, at the size Capacitor generated
    import glob
    for p in glob.glob(os.path.join(res, "drawable*", "splash.png")):
        w, h = Image.open(p).size
        splash = Image.new("RGBA", (w, h), PLUM)
        s = int(min(w, h) * 0.32)
        splash.alpha_composite(draw(s, background=False), ((w - s) // 2, (h - s) // 2))
        splash.convert("RGB").save(p)
    values = os.path.join(res, "values", "ic_launcher_background.xml")
    if os.path.exists(os.path.dirname(values)):
        open(values, "w", encoding="utf-8").write('<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">#714B67</color>\n</resources>\n')
print("icons written")
