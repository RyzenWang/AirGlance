"""Generates the format fixtures added for the limitation review.

Writes into tests/Assets:
  orientation-6.png   PNG with an eXIf orientation tag (stored 40 x 20, displayed 20 x 40)
  animated.webp       3 frames, 40 x 40, distinct colours and delays
  icc-exif.webp       WebP carrying the Display P3 profile and camera EXIF fields
  sample.bmp          32 x 16 BMP, four colour quadrants
  sample.ico          32 x 32 ICO with a transparent corner
  bigtiff-lzw.tif     BigTIFF (64-bit offsets) 64 x 32 quadrants, LZW

Deterministic: same Pillow output for the same Pillow version. The ICC profile is
read from tests/Assets/profiles/DisplayP3.icc, which is already committed.

Usage: python tools/make-extra-fixtures.py
"""

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "tests" / "Assets"

RED = (220, 40, 40)
GREEN = (40, 200, 80)
BLUE = (40, 80, 220)
YELLOW = (240, 210, 60)


def quadrants(width, height, colours=(RED, GREEN, BLUE, YELLOW)):
    image = Image.new("RGB", (width, height))
    half_w, half_h = width // 2, height // 2
    image.paste(colours[0], (0, 0, half_w, half_h))
    image.paste(colours[1], (half_w, 0, width, half_h))
    image.paste(colours[2], (0, half_h, half_w, height))
    image.paste(colours[3], (half_w, half_h, width, height))
    return image


def orientation_png():
    exif = Image.Exif()
    exif[274] = 6  # Orientation: rotate 90 CW on display
    image = quadrants(40, 20)
    # Left half red, right half green so the rotation is visible in the pixels.
    image.paste(GREEN, (20, 0, 40, 20))
    target = ASSETS / "orientation-6.png"
    image.save(target, format="PNG", exif=exif.tobytes())
    print(f"wrote {target.name}")


def animated_webp():
    frames = [
        Image.new("RGB", (40, 40), RED),
        Image.new("RGB", (40, 40), GREEN),
        Image.new("RGB", (40, 40), BLUE),
    ]
    target = ASSETS / "animated.webp"
    frames[0].save(
        target,
        format="WEBP",
        save_all=True,
        append_images=frames[1:],
        duration=[100, 200, 300],
        loop=0,
    )
    print(f"wrote {target.name}")


def icc_exif_webp():
    profile = (ASSETS / "profiles" / "DisplayP3.icc").read_bytes()
    exif = Image.Exif()
    exif[271] = "AirGlance"          # Make
    exif[272] = "Fixture Camera"     # Model
    exif[274] = 1
    image = quadrants(48, 32)
    target = ASSETS / "icc-exif.webp"
    image.save(target, format="WEBP", icc_profile=profile, exif=exif.tobytes())
    print(f"wrote {target.name}")


def bitmaps():
    quadrants(32, 16).save(ASSETS / "sample.bmp", format="BMP")
    print("wrote sample.bmp")

    image = Image.new("RGBA", (32, 32), (40, 120, 200, 255))
    image.paste((0, 0, 0, 0), (0, 0, 8, 8))  # transparent corner
    image.save(ASSETS / "sample.ico", format="ICO", sizes=[(32, 32)])
    print("wrote sample.ico")


def bigtiff():
    target = ASSETS / "bigtiff-lzw.tif"
    quadrants(64, 32).save(target, format="TIFF", compression="tiff_lzw", bigtiff=True)
    print(f"wrote {target.name}")


if __name__ == "__main__":
    orientation_png()
    animated_webp()
    icc_exif_webp()
    bitmaps()
    bigtiff()
