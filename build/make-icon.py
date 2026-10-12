"""Draw the VST Manager app icon and write the Windows .ico plus a master PNG.

The mark is a plug whose body is a "V", with an audio pulse through it, on a rounded accent tile.
It is drawn here with Pillow at 4x and scaled down, so the icon needs no SVG renderer and every
size comes from the same geometry. Coordinates are in the logo's 120-unit design space.

Run from the repo root:  python build/make-icon.py
Then, for macOS:         python build/make-icns.py   (reads the master PNG this writes)
"""

import math
import os

from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "src", "VstManager.Ui", "Assets")
MASTER_PNG = os.path.join(REPO, "build", "app-icon-master.png")
ICO = os.path.join(ASSETS, "app-icon.ico")
SETUP_ICO = os.path.join(REPO, "build", "setup-icon.ico")   # Inno Setup's SetupIconFile

ACCENT = (249, 115, 22, 255)    # #F97316, the app's accent
INK = (27, 18, 6, 255)          # #1B1206, plug body
CREAM = (255, 247, 237, 255)    # #FFF7ED, prongs

# Tile background: the accent's own family, light to deep, at the design's 160deg angle.
GRADIENT = [(0.0, (251, 146, 60)), (0.55, (249, 115, 22)), (1.0, (194, 65, 12))]  # FB923C, F97316, C2410C
GRADIENT_ANGLE = 160

MASTER = 1024
SUPERSAMPLE = 4
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]

TILE_INSET = 0.02       # transparent margin so the rounded corners anti-alias cleanly
TILE_RADIUS = 0.225     # of the tile edge
MARK_HEIGHT = 0.66      # plug's visible height as a share of the tile

# Visible extent of the mark in design units: prong tops at y=5, body stroke bottom at y=110.
BBOX_TOP, BBOX_BOTTOM, CENTER_X = 5.0, 110.0, 60.0


def lerp_stops(t):
    for (t0, c0), (t1, c1) in zip(GRADIENT, GRADIENT[1:]):
        if t <= t1:
            f = 0.0 if t1 == t0 else max(0.0, (t - t0) / (t1 - t0))
            return tuple(round(a + (b - a) * f) for a, b in zip(c0, c1)) + (255,)
    return GRADIENT[-1][1] + (255,)


def gradient_square(edge):
    """A CSS-style linear-gradient(160deg, ...) filling an edge x edge square."""
    rad = math.radians(GRADIENT_ANGLE)
    line = edge * (abs(math.sin(rad)) + abs(math.cos(rad)))   # CSS gradient-line length
    big = math.ceil(edge * 1.5)
    start = (big - line) / 2
    column = Image.new("RGBA", (1, big))
    column.putdata([lerp_stops(min(1.0, max(0.0, (y - start) / line))) for y in range(big)])
    # Vertical is 180deg ("to bottom"); rotating counter-clockwise by 20 gives 160deg.
    square = column.resize((big, big), Image.NEAREST).rotate(180 - GRADIENT_ANGLE, Image.BICUBIC)
    off = (big - edge) // 2
    return square.crop((off, off, off + edge, off + edge))


def render(supersampled=False):
    size = MASTER * SUPERSAMPLE
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    margin = size * TILE_INSET
    tile = size - 2 * margin
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, margin + tile, margin + tile],
                                           radius=tile * TILE_RADIUS, fill=255)
    img.paste(gradient_square(MASTER).resize((size, size), Image.BICUBIC), (0, 0), mask)

    k = MARK_HEIGHT * tile / (BBOX_BOTTOM - BBOX_TOP)
    cx = cy = size / 2
    ox = cx - CENTER_X * k
    oy = cy - (BBOX_TOP + BBOX_BOTTOM) / 2 * k

    def p(x, y):
        return (ox + x * k, oy + y * k)

    def dot(x, y, r, fill):
        px, py = p(x, y)
        draw.ellipse([px - r * k, py - r * k, px + r * k, py + r * k], fill=fill)

    # Prongs
    for x in (33, 77):
        x0, y0 = p(x, 5)
        x1, y1 = p(x + 10, 33)
        draw.rounded_rectangle([x0, y0, x1, y1], radius=5 * k, fill=CREAM)

    # V-shaped body: a triangle stroked 12 wide with round joins
    corners = [(24, 38), (96, 38), (60, 104)]
    draw.polygon([p(*c) for c in corners], fill=INK)
    outline = [p(*c) for c in corners + [corners[0], corners[1]]]
    draw.line(outline, fill=INK, width=round(12 * k), joint="curve")
    for c in corners:
        dot(*c, 6, INK)

    # Audio pulse through the body, cut out in the accent colour
    pulse = [(37, 52), (47, 52), (53, 41), (61, 66), (68, 47), (72, 52), (83, 52)]
    draw.line([p(*q) for q in pulse], fill=ACCENT, width=round(5 * k), joint="curve")
    for q in pulse:
        dot(*q, 2.5, ACCENT)

    return img if supersampled else img.resize((MASTER, MASTER), Image.LANCZOS)


# Installer badge, in the design's px: a 150 tile with a 62 badge hanging 14 off its corner,
# so the whole icon spans 164. The badge's 6 px ring is cut out of the tile as transparency.
SETUP_SPAN, SETUP_TILE, BADGE, BADGE_RING, BADGE_OVERHANG = 164.0, 150.0, 62.0, 6.0, 14.0
ARROW_BOX, ARROW_STROKE = 30.0, 3.2   # arrow drawn in a 24-unit box, 30 px wide in the badge
# An .ico can't follow the theme: cream vanished on white and ink on dark, the deep gradient
# stop reads on both.
BADGE_FILL = (194, 65, 12, 255)       # #C2410C
BADGE_ARROW = CREAM


def render_setup(app_icon_ss):
    """The app tile with a download badge, for VstManagerSetup.exe."""
    size = MASTER * SUPERSAMPLE
    u = size / SETUP_SPAN                    # px per design px
    tile_px = round(SETUP_TILE * u)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    img.paste(app_icon_ss.resize((tile_px, tile_px), Image.LANCZOS), (0, 0))

    c = (SETUP_SPAN - BADGE / 2) * u         # badge centre (x == y)
    outer, inner = BADGE / 2 * u, (BADGE / 2 - BADGE_RING) * u
    alpha = img.getchannel("A")
    ImageDraw.Draw(alpha).ellipse([c - outer, c - outer, c + outer, c + outer], fill=0)
    img.putalpha(alpha)

    draw = ImageDraw.Draw(img)
    draw.ellipse([c - inner, c - inner, c + inner, c + inner], fill=BADGE_FILL)

    a = ARROW_BOX / 24 * u                   # px per arrow unit
    def q(x, y):
        return (c + (x - 12) * a, c + (y - 12) * a)
    w = round(ARROW_STROKE * a)
    draw.line([q(12, 5), q(12, 17)], fill=BADGE_ARROW, width=w)
    draw.line([q(6.5, 11.5), q(12, 17), q(17.5, 11.5)], fill=BADGE_ARROW, width=w, joint="curve")
    for x, y in [(12, 5), (6.5, 11.5), (17.5, 11.5), (12, 17)]:
        px, py = q(x, y)
        draw.ellipse([px - w / 2, py - w / 2, px + w / 2, py + w / 2], fill=BADGE_ARROW)

    return img.resize((MASTER, MASTER), Image.LANCZOS)


def write_ico(image, path):
    frames = [image.resize((s, s), Image.LANCZOS) for s in ICO_SIZES]
    frames[-1].save(path, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    print(f"wrote {path} ({', '.join(str(s) for s in ICO_SIZES)} px)")


def main():
    master_ss = render(supersampled=True)
    master = master_ss.resize((MASTER, MASTER), Image.LANCZOS)
    master.save(MASTER_PNG, optimize=True)
    print(f"wrote {MASTER_PNG}")
    write_ico(master, ICO)
    write_ico(render_setup(master_ss), SETUP_ICO)


if __name__ == "__main__":
    main()
