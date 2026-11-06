#!/usr/bin/env python3
"""Draws the 512x512 listing icon: the menu's loading emblem (a kitten-head planet in a tilted orbit ring, with a
little rocket going round it) on a starfield. Mirrors Logo.DrawEmblem in the mod.
    python3 tools/make_icon.py content/icon.png
"""
import math
import random
import sys

from PIL import Image, ImageDraw

SS = 4
N = 512


def main(out):
    img = Image.new("RGBA", (N * SS, N * SS), (6, 8, 18, 255))
    d = ImageDraw.Draw(img, "RGBA")
    rnd = random.Random(11)
    for _ in range(260):
        x, y, r = rnd.uniform(0, N * SS), rnd.uniform(0, N * SS), rnd.choice([2, 2, 3, 4, 6])
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, rnd.randint(60, 200)))

    def C(r, g, b, a=1.0):
        return (int(r * 255), int(g * 255), int(b * 255), int(a * 255))

    def P(p):
        return (p[0] * SS, p[1] * SS)

    def poly(pts, col):
        d.polygon([P(p) for p in pts], fill=col)

    def circ(c, r, col):
        d.ellipse([(c[0] - r) * SS, (c[1] - r) * SS, (c[0] + r) * SS, (c[1] + r) * SS], fill=col)

    def line(a, b, col, w):
        d.line([P(a), P(b)], fill=col, width=max(1, int(w * SS)))

    c = (N / 2, N / 2 + 18)
    r = 118.0
    ocean, deep, land = C(.20, .47, .88), C(.13, .32, .66), C(.36, .72, .42)
    ring, ring_back = C(.98, .62, .22), C(.98, .62, .22, .45)
    tilt, a, b = -0.32, r * 1.75, r * 0.46

    def ring_point(t):
        return (c[0] + a * math.cos(t) * math.cos(tilt) - b * math.sin(t) * math.sin(tilt),
                c[1] + a * math.cos(t) * math.sin(tilt) + b * math.sin(t) * math.cos(tilt))

    n = 96
    for i in range(n // 2):
        line(ring_point(math.pi + i * math.pi / (n / 2)), ring_point(math.pi + (i + 1) * math.pi / (n / 2)), ring_back, 9)
    for s in (-1, 1):
        poly([(c[0] + s * r * .92, c[1] - r * .38), (c[0] + s * r * .80, c[1] - r * 1.42), (c[0] + s * r * .18, c[1] - r * .92)], deep)
        poly([(c[0] + s * r * .74, c[1] - r * .62), (c[0] + s * r * .76, c[1] - r * 1.18), (c[0] + s * r * .38, c[1] - r * .88)], C(.98, .66, .72))
    circ(c, r, deep)
    circ((c[0] - r * .06, c[1] - r * .06), r * .92, ocean)
    circ((c[0] - r * .42, c[1] + r * .38), r * .30, land)
    circ((c[0] + r * .48, c[1] - r * .52), r * .20, land)
    circ((c[0] - r * .38, c[1] - r * .42), r * .16, C(1, 1, 1, .35))
    for s in (-1, 1):
        eye = (c[0] + s * r * .36, c[1] - r * .02)
        circ(eye, r * .15, C(.06, .08, .14))
        circ((eye[0] - r * .05, eye[1] - r * .05), r * .05, C(1, 1, 1))
        for k in range(3):
            line((c[0] + s * r * .42, c[1] + r * (.30 + .10 * k)), (c[0] + s * r * 1.32, c[1] + r * (.16 + .22 * k)), C(1, 1, 1, .85), 4)
    poly([(c[0] - r * .09, c[1] + r * .20), (c[0] + r * .09, c[1] + r * .20), (c[0], c[1] + r * .31)], C(.98, .55, .65))
    for i in range(n // 2):
        line(ring_point(i * math.pi / (n / 2)), ring_point((i + 1) * math.pi / (n / 2)), ring, 10)

    # the rocket on the near side of the ring, pointing along its orbit, with a flame
    ct = 0.75
    craft = ring_point(ct)
    ahead = ring_point(ct + 0.05)
    f = (ahead[0] - craft[0], ahead[1] - craft[1])
    ln = math.hypot(*f)
    f = (f[0] / ln, f[1] / ln)
    nrm = (-f[1], f[0])
    length, wid = r * 0.62, r * 0.16

    def pt(along, side):
        return (craft[0] + f[0] * along + nrm[0] * side, craft[1] + f[1] * along + nrm[1] * side)

    flame = length * 0.42
    poly([pt(-length * .5, -wid * .6), pt(-length * .5, wid * .6), pt(-length * .5 - flame, 0)], C(1, .6, .15))
    poly([pt(-length * .5, -wid * .35), pt(-length * .5, wid * .35), pt(-length * .5 - flame * .55, 0)], C(1, .95, .6))
    hull, trim = C(.96, .96, .98), C(.98, .45, .2)
    poly([pt(-length * .5, -wid), pt(length * .25, -wid), pt(length * .25, wid), pt(-length * .5, wid)], hull)
    poly([pt(length * .25, -wid), pt(length * .5, 0), pt(length * .25, wid)], trim)
    poly([pt(-length * .5, -wid), pt(-length * .2, -wid), pt(-length * .55, -wid * 2)], trim)
    poly([pt(-length * .5, wid), pt(-length * .2, wid), pt(-length * .55, wid * 2)], trim)
    circ(pt(length * .02, 0), wid * .45, C(.3, .6, .95))

    img.resize((N, N), Image.LANCZOS).convert("RGB").save(out)


if __name__ == "__main__":
    main(sys.argv[1])
