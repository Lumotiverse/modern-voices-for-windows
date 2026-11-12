"""Draws the two programs' icons (needs Pillow): a white speech bubble with rounded corners holding a
black speaker (the program's own, and the Reader's: reader/reader.ico), a black gear (Modern Voices Config:
installer/manager.ico) or a black wrench (Modern Voices Repair: installer/repair.ico).
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


def wrench():
    image, d = bubble()
    cx, cy = 512, 435
    u = math.sqrt(0.5)                       # the wrench lies from bottom left to top right

    def along(t, across=0.0):                # a point t along the wrench from the middle, and across it
        return (cx + (t - across) * u, cy - (t + across) * u)

    d.line([along(-215), along(120)], fill=BLACK, width=96)                       # the handle
    foot = along(-215)
    d.ellipse((foot[0] - 48, foot[1] - 48, foot[0] + 48, foot[1] + 48), fill=BLACK)
    head = along(165)
    d.ellipse((head[0] - 150, head[1] - 150, head[0] + 150, head[1] + 150), fill=BLACK)
    d.polygon([along(150, -62), along(340, -62), along(340, 62), along(150, 62)], fill=WHITE)   # the jaws' gap
    gap = along(150)
    d.ellipse((gap[0] - 62, gap[1] - 62, gap[0] + 62, gap[1] + 62), fill=WHITE)
    return image


SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)]
for name, image in (('reader/reader.ico', speaker()), ('installer/manager.ico', gear()), ('installer/repair.ico', wrench())):
    # (as plain bitmaps: Windows does not show the small sizes of a program's icon when they are PNG)
    image.save(name, sizes=SIZES, bitmap_format='bmp')
    print(name)

if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1:                    # a picture to look at: python tools/make_icons.py preview.png
        sheet = Image.new('RGBA', (3 * 300, 300), (90, 120, 160, 255))
        for i, image in enumerate((speaker(), gear(), wrench())):
            small = image.resize((256, 256), Image.LANCZOS)
            sheet.paste(small, (22 + i * 300, 22), small)
            tiny = image.resize((32, 32), Image.LANCZOS)
            sheet.paste(tiny, (i * 300 + 4, 264), tiny)
        sheet.save(sys.argv[1])
