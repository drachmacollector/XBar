"""Generates the XBar icons into src/XBar.App/Assets (needs Pillow).  python tools/make_icons.py"""
import math, os
from PIL import Image, ImageDraw, ImageOps

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "XBar.App", "Assets")
S = 1024            # draw big, then downscale
SALMON = (244, 128, 106)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def gradient(c1, c2):
    g = Image.new("RGB", (S, S))
    px = g.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * S - 2)
            px[x, y] = tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(3))
    return g


def tile(c1, c2):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((28, 28, S - 28, S - 28), radius=212, fill=255)
    img.paste(gradient(c1, c2), (0, 0), mask)
    return img


def pill(d, box, fill):
    d.rounded_rectangle(box, radius=(box[3] - box[1]) // 2, fill=fill)


def main_icon(paused=False):
    img = tile((22, 34, 79), (92, 40, 104)) if not paused else tile((52, 54, 60), (84, 86, 92))
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255) if not paused else (215, 215, 215, 255)
    salmon = SALMON + (255,) if not paused else (150, 150, 150, 255)
    # stylised X
    w = 112
    d.line((300, 190, 724, 614), fill=white, width=w)
    d.line((724, 190, 300, 614), fill=white, width=w)
    for p in ((300, 190), (724, 614), (724, 190), (300, 614)):
        d.ellipse((p[0] - w // 2, p[1] - w // 2, p[0] + w // 2, p[1] + w // 2), fill=white)
    # the two pills (apps + tray) of the bar
    pill(d, (140, 700, 660, 880), salmon)
    pill(d, (700, 700, 884, 880), salmon)
    return img


def restore_icon():
    img = tile((12, 92, 90), (22, 140, 120))
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    cx, cy, r, w = 512, 512, 250, 100
    d.arc((cx - r, cy - r, cx + r, cy + r), start=-60, end=250, fill=white, width=w)
    a = math.radians(250)  # arrow head at the end of the arc, pointing along it
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    t = (-math.sin(a), math.cos(a))  # tangent (direction of travel)
    n = (math.cos(a), math.sin(a))
    tip = (ex + t[0] * 120, ey + t[1] * 120)
    d.polygon([(ex + n[0] * 150, ey + n[1] * 150), (ex - n[0] * 150, ey - n[1] * 150), tip], fill=white)
    return img


def safe_icon():
    img = tile((52, 60, 78), (92, 104, 128))
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    shield = [(512, 190), (790, 290), (770, 560), (512, 840), (254, 560), (234, 290)]
    d.polygon(shield, fill=white)
    inner = [(512, 270), (710, 345), (695, 545), (512, 750), (329, 545), (314, 345)]
    d.polygon(inner, fill=(60, 70, 92, 255))
    d.line((420, 520, 490, 600), fill=white, width=64)
    d.line((490, 600, 620, 420), fill=white, width=64)
    return img


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    frames = [img.resize((s, s), Image.LANCZOS) for s in SIZES]
    frames[-1].save(os.path.join(OUT, name + ".ico"), format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, name + ".png"))


save(main_icon(), "xbar")
save(main_icon(paused=True), "xbar-paused")
save(restore_icon(), "restore")
save(safe_icon(), "safe")
print("icons written to", os.path.abspath(OUT))
