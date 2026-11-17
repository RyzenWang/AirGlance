# Generates the small test images committed under tests/Assets.
# Requires Python 3 with Pillow and numpy.
#
# Usage: python tools/make-test-assets.py
import os

import numpy as np
from PIL import Image, ImageCms, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tests", "Assets")
PROFILES = os.path.join(OUT, "profiles")

P3_PROFILE = os.path.join(PROFILES, "DisplayP3.icc")
ADOBE_PROFILE = os.path.join(PROFILES, "AdobeCompat.icc")


def make_orientation_source():
    """80x40 with a unique colour in each corner, so every flip/rotation is visible."""
    image = Image.new("RGB", (80, 40), (245, 245, 245))
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, 9, 9], fill=(255, 0, 0))       # top-left: red
    draw.rectangle([70, 0, 79, 9], fill=(0, 180, 60))    # top-right: green
    draw.rectangle([0, 30, 9, 39], fill=(0, 90, 255))    # bottom-left: blue
    draw.rectangle([70, 30, 79, 39], fill=(255, 220, 0))  # bottom-right: yellow
    return image


def make_exif_source():
    image = Image.new("RGB", (120, 80), (40, 80, 120))
    draw = ImageDraw.Draw(image)
    draw.rectangle([10, 10, 60, 40], fill=(230, 80, 60))
    exif = Image.Exif()
    exif[271] = "AirWorks"                    # Make
    exif[272] = "AirGlance Test Camera"       # Model
    exif[274] = 1                             # Orientation
    exif[305] = "AirGlance 0.1"               # Software
    exif[306] = "2026:11:17 10:30:00"         # DateTime
    exif[36867] = "2026:11:17 10:25:00"       # DateTimeOriginal
    exif[33434] = (1, 250)                    # ExposureTime
    exif[33437] = (28, 10)                    # FNumber
    exif[34855] = 200                         # ISO
    exif[37386] = (35, 1)                     # FocalLength
    exif[42036] = "AW 35mm f/1.8"             # LensModel
    exif[37380] = (-1, 3)                     # ExposureBias
    return image, exif


def make_colour_image(profile_path):
    image = Image.new("RGB", (64, 32))
    pixels = image.load()
    for y in range(32):
        for x in range(64):
            if x < 32 and y < 16:
                pixels[x, y] = (200, 30, 30)
            elif x >= 32 and y < 16:
                pixels[x, y] = (30, 180, 60)
            elif x < 32:
                pixels[x, y] = (30, 60, 200)
            else:
                pixels[x, y] = (230, 220, 210)
    profile = open(profile_path, "rb").read()
    return image, profile


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PROFILES, exist_ok=True)

    # ICC profiles: Compact ICC Profiles, CC0. Downloaded once by the repository owner.
    for filename, url in (
        ("DisplayP3.icc", "DisplayP3-v4.icc"),
        ("AdobeCompat.icc", "AdobeCompat-v2.icc"),
    ):
        target = os.path.join(PROFILES, filename)
        if not os.path.exists(target):
            raise SystemExit(
                f"missing {target}; download {url} from the Compact ICC Profiles project "
                "(CC0) into tests/Assets/profiles first"
            )

    source = make_orientation_source()
    for orientation in range(1, 9):
        exif = Image.Exif()
        exif[274] = orientation
        source.save(
            os.path.join(OUT, f"orientation-{orientation}.jpg"),
            quality=95,
            exif=exif.tobytes(),
        )

    exif_image, exif = make_exif_source()
    exif_image.save(os.path.join(OUT, "exif-full.jpg"), quality=95, exif=exif.tobytes())

    srgb = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
    p3_image, p3_profile = make_colour_image(P3_PROFILE)
    p3_image.save(os.path.join(OUT, "display-p3.jpg"), quality=95, icc_profile=p3_profile)
    p3_image.save(os.path.join(OUT, "display-p3.png"), icc_profile=p3_profile)

    adobe_image, adobe_profile = make_colour_image(ADOBE_PROFILE)
    adobe_image.save(os.path.join(OUT, "adobe-rgb.jpg"), quality=95, icc_profile=adobe_profile)

    p3_image.save(os.path.join(OUT, "srgb.jpg"), quality=95, icc_profile=srgb)

    alpha = Image.new("RGBA", (48, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(alpha)
    draw.rectangle([8, 8, 40, 24], fill=(227, 86, 59, 255))
    alpha.save(os.path.join(OUT, "alpha.png"))
    alpha.save(os.path.join(OUT, "alpha.webp"), lossless=True)

    frames = []
    for colour in ((227, 86, 59), (46, 111, 99), (255, 214, 0)):
        frame = Image.new("RGB", (40, 40), colour)
        frames.append(frame)
    frames[0].save(
        os.path.join(OUT, "animated.gif"),
        save_all=True,
        append_images=frames[1:],
        duration=100,
        loop=0,
    )

    gradient = np.linspace(0, 65535, 64 * 32, dtype=np.uint16).reshape(32, 64)
    Image.fromarray(gradient, mode="I;16").save(os.path.join(OUT, "gradient-16bit.png"))

    # A file with a supported extension and no image inside: the decoder must report
    # "corrupt", not crash.
    with open(os.path.join(OUT, "corrupt.jpg"), "wb") as handle:
        handle.write(b"this is not a jpeg")

    print("test assets written to", OUT)


if __name__ == "__main__":
    main()
