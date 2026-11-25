"""Converts the documentation screenshots under docs/design from PNG to WebP.

Runs in three modes:
  probe    report the size at several qualities for a sample, write nothing
  apply    write the .webp files and delete the .png originals
  rewrite  point every markdown and HTML reference at the .webp files

Usage:
  python tools/convert-design-images.py probe
  python tools/convert-design-images.py apply [quality]
  python tools/convert-design-images.py rewrite
"""
import io
import os
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / "docs" / "design"
SKIP_DIRS = {"bin", "obj", "node_modules", "publish", "artifacts", ".git"}


def sources():
    return sorted(p for p in DESIGN.rglob("*.png"))


def encode(path, quality):
    with Image.open(path) as image:
        image.load()
        frame = image.convert("RGBA") if image.mode in ("RGBA", "LA", "P") else image.convert("RGB")
        buffer = io.BytesIO()
        frame.save(buffer, "WEBP", quality=quality, method=6)
        return buffer.getvalue()


def probe():
    files = sources()
    total = sum(p.stat().st_size for p in files)
    print(f"{len(files)} PNG files, {total / 1e6:.1f} MB")
    sample = [p for p in files if p.stat().st_size > 250_000][:6]
    if not sample:
        sample = files[:6]
    print(f"sample of {len(sample)}:")
    for quality in (90, 92, 95):
        size = 0
        for path in sample:
            size += len(encode(path, quality))
        png = sum(p.stat().st_size for p in sample)
        print(f"  q{quality}: {size / 1e6:.2f} MB for {png / 1e6:.2f} MB of PNG ({size / png:.0%})")


def apply(quality):
    files = sources()
    before = 0
    after = 0
    regressions = []
    for path in files:
        data = encode(path, quality)
        target = path.with_suffix(".webp")
        target.write_bytes(data)
        before += path.stat().st_size
        after += len(data)
        print(f"  {path.relative_to(ROOT)} -> {target.name}  {path.stat().st_size / 1024:7.0f} KB -> {len(data) / 1024:6.0f} KB")
        if len(data) > path.stat().st_size:
            regressions.append(target.name)
        path.unlink()
    print(f"\n{len(files)} files: {before / 1e6:.1f} MB -> {after / 1e6:.1f} MB")
    if regressions:
        print("larger as WebP than as PNG (keep or re-encode manually): " + ", ".join(regressions))


def rewrite():
    names = sorted((p.stem for p in DESIGN.rglob("*.webp")), key=len, reverse=True)
    changed = []
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file() or path.suffix not in (".md", ".html"):
            continue
        if SKIP_DIRS & set(path.parts):
            continue
        data = path.read_bytes()
        updated = data
        for name in names:
            updated = updated.replace(f"{name}.png".encode(), f"{name}.webp".encode())
        if updated != data:
            path.write_bytes(updated)
            changed.append(path)
    print(f"rewrote {len(changed)} files")
    for path in changed:
        print(f"  {path.relative_to(ROOT)}")


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "probe"
    if mode == "probe":
        probe()
    elif mode == "apply":
        apply(int(sys.argv[2]) if len(sys.argv) > 2 else 92)
    elif mode == "rewrite":
        rewrite()
    else:
        raise SystemExit("mode must be probe, apply or rewrite")

