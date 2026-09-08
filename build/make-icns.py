"""Build a macOS .icns from the app's .ico.

macOS packaging normally uses iconutil/sips, which only exist on macOS. This project is built on
Windows, so the .icns is assembled directly: it is a documented container — an 8-byte header
('icns' + total length) followed by typed chunks, each being a 4-byte OSType, a big-endian
length covering the header, then the payload. Modern types take a PNG payload verbatim, so the
frames only need re-encoding from the ICO's uncompressed DIBs.

Run from the repo root:  python build/make-icns.py
"""

import io
import os
import struct
import sys

from PIL import Image

# The types Finder actually consults, paired with the pixel size each expects. The @2x retina
# entries carry the same artwork at double resolution — 'ic08' (256) doubles as 'ic13' (256@2x
# for a 128pt slot), which is why some sizes appear twice.
TYPES = [
    (b"icp4", 16),
    (b"icp5", 32),
    (b"ic11", 32),    # 16pt @2x
    (b"icp6", 64),
    (b"ic12", 64),    # 32pt @2x
    (b"ic07", 128),
    (b"ic08", 256),
    (b"ic13", 256),   # 128pt @2x
    (b"ic09", 512),
    (b"ic14", 512),   # 256pt @2x
    (b"ic10", 1024),  # 512pt @2x
]

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The *transparent* variant, not app-icon.ico which the Windows build uses. macOS draws the icon
# unmasked against the Dock and Finder background, so the rounded corners have to be transparent
# in the artwork itself; app-icon.ico is fully opaque (alpha is 255 everywhere) and would show as
# a hard black square. This one is also 1254px, so every icns size down-samples from real detail
# rather than being upscaled from 256.
SRC = os.path.join(
    REPO, "src", "VstManager.App",
    "a_clean_modern_app_icon_logo_design_on_a_dark_b-transparent.ico",
)
OUT = os.path.join(REPO, "src", "VstManager.Ui", "Assets", "app-icon.icns")


def load_largest(path):
    """The ICO's biggest frame, used as the master to resample every icns size from."""
    with Image.open(path) as im:
        sizes = getattr(im, "ico", None)
        best = None
        for size in sorted(im.info.get("sizes", []) or [], key=lambda s: s[0] * s[1]):
            im.size = size
            best = im.convert("RGBA").copy()
        if best is None:
            best = im.convert("RGBA").copy()
        return best


def main():
    if not os.path.exists(SRC):
        sys.exit(f"source icon not found: {SRC}")

    master = load_largest(SRC)
    print(f"master frame: {master.size[0]}x{master.size[1]}")

    chunks = []
    for ostype, px in TYPES:
        # LANCZOS down-samples cleanly; the 1254px master covers every size except 1024@2x,
        # which is a marginal upscale. All of them are declared because Finder falls back to the
        # nearest larger type, and a missing size leaves that preview blurrier than an explicit
        # resample would.
        frame = master.resize((px, px), Image.LANCZOS)
        buf = io.BytesIO()
        frame.save(buf, format="PNG", optimize=True)
        payload = buf.getvalue()
        chunks.append(ostype + struct.pack(">I", len(payload) + 8) + payload)
        print(f"  {ostype.decode()} {px:>4}px  {len(payload):>8} bytes")

    body = b"".join(chunks)
    icns = b"icns" + struct.pack(">I", len(body) + 8) + body

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as fh:
        fh.write(icns)

    print(f"wrote {OUT} ({len(icns)} bytes, {len(chunks)} entries)")


if __name__ == "__main__":
    main()
