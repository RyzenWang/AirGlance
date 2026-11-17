# Generates the AirGlance icon and the platform icon assets from one drawing.
# Requires Python 3 with Pillow.
#
# Usage: python tools/make-icon.py
import math
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_ASSETS = os.path.join(ROOT, "src", "AirGlance.App", "Assets")
MSIX_ASSETS = os.path.join(ROOT, "packaging", "msix", "Assets")
LINUX_ICONS = os.path.join(ROOT, "packaging", "linux", "icons")

CORAL = (227, 86, 59, 255)
WHITE = (255, 255, 255, 255)


def draw_icon(size):
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    radius = int(size * 0.2)
    draw.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=CORAL)

    stroke = max(1, int(size * 0.042))
    inset = int(size * 0.24)
    arm = int(size * 0.16)
    x0, y0 = inset, inset
    x1, y1 = size - inset, size - inset
    # Each corner is two lines that meet at the corner of the viewfinder frame.
    draw.line([(x0, y0 + arm), (x0, y0), (x0 + arm, y0)], fill=WHITE, width=stroke, joint="curve")
    draw.line([(x1 - arm, y0), (x1, y0), (x1, y0 + arm)], fill=WHITE, width=stroke, joint="curve")
    draw.line([(x0, y1 - arm), (x0, y1), (x0 + arm, y1)], fill=WHITE, width=stroke, joint="curve")
    draw.line([(x1 - arm, y1), (x1, y1), (x1, y1 - arm)], fill=WHITE, width=stroke, joint="curve")

    # One wind line through the frame: a smooth gust rising to the right.
    wind = []
    left = int(size * 0.29)
    right = int(size * 0.71)
    top = int(size * 0.49)
    bottom = int(size * 0.575)
    for step in range(0, 33):
        t = step / 32
        x = left + (right - left) * t
        y = bottom - (bottom - top) * t - int(size * 0.02) * math.sin(math.pi * t)
        wind.append((x, y))
    draw.line(wind, fill=WHITE, width=stroke, joint="curve")
    radius = stroke / 2
    for point in (wind[0], wind[-1]):
        draw.ellipse(
            [point[0] - radius, point[1] - radius, point[0] + radius, point[1] + radius],
            fill=WHITE,
        )
    return image


def main():
    os.makedirs(APP_ASSETS, exist_ok=True)
    os.makedirs(MSIX_ASSETS, exist_ok=True)
    os.makedirs(LINUX_ICONS, exist_ok=True)

    master = draw_icon(1024)
    master.save(os.path.join(APP_ASSETS, "airglance.png"))

    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [master.resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(
        os.path.join(APP_ASSETS, "airglance.ico"),
        format="ICO",
        sizes=[(s, s) for s in sizes],
    )

    # ICNS is a simple container of PNG chunks; writing it avoids needing macOS tooling.
    icns_chunks = []
    for icon_type, icon_size in (("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024)):
        import io

        buffer = io.BytesIO()
        master.resize((icon_size, icon_size), Image.LANCZOS).save(buffer, format="PNG")
        payload = buffer.getvalue()
        icns_chunks.append(icon_type.encode("ascii") + (len(payload) + 8).to_bytes(4, "big") + payload)
    body = b"".join(icns_chunks)
    with open(os.path.join(APP_ASSETS, "airglance.icns"), "wb") as handle:
        handle.write(b"icns" + (len(body) + 8).to_bytes(4, "big") + body)

    master.resize((44, 44), Image.LANCZOS).save(os.path.join(MSIX_ASSETS, "Square44x44Logo.png"))
    master.resize((150, 150), Image.LANCZOS).save(os.path.join(MSIX_ASSETS, "Square150x150Logo.png"))
    master.resize((50, 50), Image.LANCZOS).save(os.path.join(MSIX_ASSETS, "StoreLogo.png"))
    master.resize((256, 256), Image.LANCZOS).save(os.path.join(MSIX_ASSETS, "Square256x256Logo.png"))

    for icon_size in (16, 24, 32, 48, 64, 128, 256, 512):
        master.resize((icon_size, icon_size), Image.LANCZOS).save(
            os.path.join(LINUX_ICONS, f"airglance-{icon_size}.png")
        )

    print("icons written")


if __name__ == "__main__":
    main()
