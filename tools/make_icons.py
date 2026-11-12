"""Draws the two programs' icons (needs Pillow): a white speech bubble with rounded corners holding a
black speaker (the Reader: reader/reader.ico) or a black gear (the Voice manager: installer/manager.ico).
Run from the repository's root: python tools/make_icons.py"""
import math
from PIL import Image, ImageDraw

S = 1024
EDGE = (40, 40, 40, 255)        # a dark edge, so the white bubble shows on light backgrounds too
WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)


def bubble():
    image = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    for grow, colour in ((0, EDGE), (-34, WHITE)):
        d.rounded_rectangle((60 - grow, 70 - grow, 964 + grow, 800 + grow), radius=170 + grow, fill=colour)
        d.polygon([(210 - grow, 760), (210 - grow, 968 + grow * 2), (470 + grow * 2, 780)], fill=colour)
    return image, d


def speaker():
    image, d = bubble()
    cx, cy = 470, 435
    d.rectangle((cx - 190, cy - 80, cx - 80, cy + 80), fill=BLACK)
    d.polygon([(cx - 90, cy - 80), (cx + 60, cy - 200), (cx + 60, cy + 200), (cx - 90, cy + 80)], fill=BLACK)
    for radius in (130, 230):
        d.arc((cx + 60 - radius, cy - radius, cx + 60 + radius, cy + radius), -42, 42, fill=BLACK, width=52)
    return image


def gear():
    image, d = bubble()
    cx, cy = 512, 435
    teeth, inner, outer = 8, 185, 262
    for k in range(teeth):
        a = 2 * math.pi * k / teeth
        half_in, half_out = 0.26, 0.17
        points = [(cx + r * math.cos(a + s), cy + r * math.sin(a + s))
                  for r, s in ((inner - 20, -half_in), (outer, -half_out), (outer, half_out), (inner - 20, half_in))]
        d.polygon(points, fill=BLACK)
    d.ellipse((cx - inner, cy - inner, cx + inner, cy + inner), fill=BLACK)
    d.ellipse((cx - 82, cy - 82, cx + 82, cy + 82), fill=WHITE)
    return image


SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)]
for name, image in (('reader/reader.ico', speaker()), ('installer/manager.ico', gear())):
    # (as plain bitmaps: Windows does not show the small sizes of a program's icon when they are PNG)
    image.save(name, sizes=SIZES, bitmap_format='bmp')
    print(name)
