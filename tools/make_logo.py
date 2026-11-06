#!/usr/bin/env python3
"""Draws the KSAMainMenu wordmark: KSP's logo, kitten edition (user, 2026-10-26: "see the ksp and ksp2 logo, a bit
close to that"; picked the wordmark, "needs to line up better").

Red heavy-italic KITTEN with a white edge; KSP's four-point star flare under the K, with a thin streak to the left and a
swoosh that runs under the word as the divider and lifts at the end (user: "lose the rocket and just have the
starburst, swoosh like divider"); SPACE AGENCY in navy with a white edge, justified under the word.

Writes a transparent PNG (cropped, 2x the in-game size at interface scale 1) for the mod, and a preview on stars.
    python3 tools/make_logo.py KSAMainMenu/Assets/logo_wordmark.png [preview.png]
"""
import math
import random
import sys

from PIL import Image, ImageDraw, ImageFont

BOLD_ITALIC = "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf"
ITALIC = "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf"
SS = 4          # supersampling while drawing
OUT_SCALE = 2   # the PNG is 2x the in-game size at interface scale 1

RED = (217, 31, 26, 255)
WHITE = (255, 255, 255, 255)
NAVY = (33, 41, 107, 255)
SHADOW = (0, 0, 0, 110)

# layout in in-game pixels (scale 1)
F = 120                    # KITTEN size
SUB = 34                   # SPACE AGENCY size
EDGE = 5.0                 # white edge on the red
X0, Y0 = 170, 40           # where KITTEN is drawn (room for the streak on the left)


def main(out, preview=None):
    w, h = 900, 300
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    bold = ImageFont.truetype(BOLD_ITALIC, F * SS)
    sub = ImageFont.truetype(ITALIC, SUB * SS)

    def P(x, y):
        return (x * SS, y * SS)

    def poly(pts, col):
        d.polygon([P(*p) for p in pts], fill=col)

    def text(x, y, s, font, col):
        d.text(P(x, y), s, font=font, fill=col)

    def edged(x, y, s, font, fill, edge, ew, steps=24):
        for i in range(steps):
            a = i * math.tau / steps
            text(x + math.cos(a) * ew, y + math.sin(a) * ew, s, font, edge)
        text(x, y, s, font, fill)

    # glyph geometry (font units at F, origin = text position)
    bbox_word = bold.getbbox("KITTEN")
    cap_top, baseline = bbox_word[1] / SS, bbox_word[3] / SS          # 26, 109 at F=120
    word_right = X0 + bbox_word[2] / SS
    word_left = X0 + bold.getbbox("K")[0] / SS
    flare = (word_left - 16, Y0 + baseline + 15)                      # starburst just left of the K, on the divider

    # 1. drop shadow for the whole mark, then the white edges, then the fills (so edges never cut the red)
    swoosh_pts, streak_pts = swoosh(flare, word_right)
    # shadow
    for pts in (swoosh_pts, streak_pts):
        poly([(x + 3, y + 4) for x, y in pts], SHADOW)
    for i in range(3):
        text(X0 + 2 + i, Y0 + 3 + i, "KITTEN", bold, (0, 0, 0, 40))

    # swoosh + streak (white edge, red fill)
    for pts in (swoosh_pts, streak_pts):
        poly(grow(pts, 2.5), WHITE)
    for pts in (swoosh_pts, streak_pts):
        poly(pts, RED)

    # the word
    edged(X0, Y0, "KITTEN", bold, RED, WHITE, EDGE)

    # the star flare under the K, where the swoosh starts
    star(poly, flare, 50, 24)

    # SPACE AGENCY, navy with a white edge, justified under ITTEN
    label = "SPACE AGENCY"
    glyphs = [sub.getlength(c) / SS for c in label]
    gap = (word_right - 6 - (word_left + 14) - sum(glyphs)) / (len(label) - 1)
    x = word_left + 14
    y = Y0 + baseline + 25
    for c, gw in zip(label, glyphs):
        edged(x, y, c, sub, NAVY, WHITE, 2.4, 16)
        x += gw + gap

    out_img = img.resize((w * OUT_SCALE, h * OUT_SCALE), Image.LANCZOS)
    box = out_img.getbbox()
    out_img = out_img.crop(box)
    out_img.save(out)
    print(f"{out}: {out_img.size[0]}x{out_img.size[1]} (in game {out_img.size[0] / OUT_SCALE:.0f} px wide at scale 1)")

    if preview:
        bg = Image.new("RGBA", out_img.size, (6, 8, 16, 255))
        rnd = random.Random(5)
        dd = ImageDraw.Draw(bg)
        for _ in range(300):
            px, py, r = rnd.uniform(0, bg.width), rnd.uniform(0, bg.height), rnd.choice([1, 1, 2])
            dd.ellipse([px - r, py - r, px + r, py + r], fill=(255, 255, 255, rnd.randint(50, 190)))
        bg.alpha_composite(out_img)
        bg.convert("RGB").save(preview)


def swoosh(flare, word_right):
    """The red swoosh under the word (thick at the flare, thinning, lifting at the end) and the streak to the left."""
    x0, y0 = flare
    x1 = word_right + 6
    upper, lower = [], []
    n = 48
    for i in range(n + 1):
        t = i / n
        x = x0 + (x1 - x0) * t
        y = y0 + 5 * math.sin(t * math.pi * 0.9) - 22 * t ** 3          # a slight sag, then a lift at the end
        half = 6.5 * (1 - t) ** 0.9 + 0.6
        upper.append((x, y - half))
        lower.append((x, y + half))
    streak = [(x0 - 130, y0 - 0.8), (x0, y0 - 3.5), (x0, y0 + 3.5), (x0 - 130, y0 + 0.8)]
    return upper + lower[::-1], streak


def grow(pts, by):
    """Push a polygon's points out from its centre (a cheap outline)."""
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    out = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        ln = math.hypot(dx, dy) or 1
        out.append((x + dx / ln * by, y + dy / ln * by))
    return out


def star(poly, c, rx, ry):
    """KSP's four-point flare: long sideways, shorter up and down, white-edged."""
    for grow_by, col in ((3.5, WHITE), (0.0, RED)):
        a, b = rx + grow_by, ry + grow_by
        k = 0.075
        poly([(c[0] - a, c[1]), (c[0] - a * k, c[1] - b * k), (c[0], c[1] - b), (c[0] + a * k, c[1] - b * k),
              (c[0] + a, c[1]), (c[0] + a * k, c[1] + b * k), (c[0], c[1] + b), (c[0] - a * k, c[1] + b * k)], col)
    for r, col in ((5, WHITE),):
        poly([(c[0] + r * math.cos(t), c[1] + r * math.sin(t)) for t in [k * math.tau / 16 for k in range(16)]], col)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
