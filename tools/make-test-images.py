# Generates the large benchmark images. Requires Python 3 with Pillow and numpy.
# Usage: python tools/make-test-images.py [output-directory]
import os
import sys

import numpy as np
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "testdata")
os.makedirs(out, exist_ok=True)


def gradient(width, height):
    gx = np.linspace(0.0, 1.0, width, dtype=np.float32)[None, :]
    gy = np.linspace(0.0, 1.0, height, dtype=np.float32)[:, None]
    red = 30 + 210 * gx + 0 * gy
    green = 40 + 160 * gy + 0 * gx
    blue = 200 - 150 * gx * gy
    rgb = np.empty((height, width, 3), dtype=np.uint8)
    rgb[:, :, 0] = np.broadcast_to(red, (height, width)).astype(np.uint8)
    rgb[:, :, 1] = np.broadcast_to(green, (height, width)).astype(np.uint8)
    rgb[:, :, 2] = blue.astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


print("24 MP 6000x4000 ...")
gradient(6000, 4000).save(os.path.join(out, "photo-24mp.jpg"), quality=85, subsampling=1)

print("100 MP 10000x10000 ...")
gradient(10000, 10000).save(os.path.join(out, "photo-100mp.jpg"), quality=85, subsampling=1)

print("500 image folder ...")
folder = os.path.join(out, "folder-500")
os.makedirs(folder, exist_ok=True)
for index in range(1, 501):
    color = ((index * 37) % 256, (index * 73) % 256, (index * 109) % 256)
    Image.new("RGB", (320, 240), color).save(os.path.join(folder, f"img{index}.jpg"), quality=80)

print("done:", out)
