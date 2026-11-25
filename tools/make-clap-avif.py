#!/usr/bin/env python3
"""Generates tests/Assets/clap.avif from tests/Assets/sample.avif.

The fixture is the same AV1 payload as sample.avif with a clean-aperture (`clap`)
property added to the item property container and associated with the primary
item. Nothing is re-encoded, so the pixels of the coded frame are unchanged; the
only difference is that a conforming reader must crop 1440 x 900 down to
1438 x 898 before display.

Notes for anyone regenerating this by hand:

- `clap` must be associated in `ipma` as an **essential** property. libavif runs
  with strict clean-aperture validation and rejects a non-essential association.
- The box is appended to `ipco`, so its property index is len(properties) + 1.
- Inserting bytes before `mdat` moves the item extents, so every `iloc` extent
  offset is shifted by the number of bytes inserted. sample.avif uses
  `base_offset + extent_offset`, and only the extent is patched.
- Offsets are zero and the aperture is two pixels smaller than the frame, which
  centres it exactly: the reader drops the outer row and column.

Usage: python tools/make-clap-avif.py
"""

import struct
import sys
from pathlib import Path

CROP_WIDTH = 1438
CROP_HEIGHT = 898


def u32(data, pos):
    return struct.unpack('>I', data[pos:pos + 4])[0]


def boxes(data, offset, size, skip):
    out, pos, end = [], offset + skip, offset + size
    while pos + 8 <= end:
        child = u32(data, pos)
        out.append((data[pos + 4:pos + 8], pos, child))
        pos += child
    return out


def find(items, name):
    for box_type, offset, size in items:
        if box_type == name:
            return offset, size
    raise KeyError(f"missing box {name!r}")


def item_associations(data, ipma_offset):
    body = ipma_offset + 8
    version = data[body]
    flags = int.from_bytes(data[body + 1:body + 4], 'big')
    if version != 0 or flags & 1:
        raise SystemExit("only 8-bit ipma v0 is handled")
    count = u32(data, body + 4)
    pos = body + 8
    entries = []
    for _ in range(count):
        item_id = struct.unpack('>H', data[pos:pos + 2])[0]
        pos += 2
        associations = data[pos]
        pos += 1
        entries.append([item_id, list(data[pos:pos + associations])])
        pos += associations
    return version, flags, entries


def build_ipma(version, flags, entries):
    payload = struct.pack('>B', version) + flags.to_bytes(3, 'big') + struct.pack('>I', len(entries))
    for item_id, associations in entries:
        payload += struct.pack('>H', item_id) + struct.pack('>B', len(associations)) + bytes(associations)
    return struct.pack('>I', 8 + len(payload)) + b'ipma' + payload


def shift_iloc_extents(iloc, delta):
    if iloc[8] != 0:
        raise SystemExit("only iloc v0 is handled")
    offset_size = iloc[12] >> 4
    length_size = iloc[12] & 0xF
    base_size = iloc[13] >> 4
    pos = 14
    count = struct.unpack('>H', iloc[pos:pos + 2])[0]
    pos += 2
    for _ in range(count):
        pos += 4 + base_size
        extents = struct.unpack('>H', iloc[pos:pos + 2])[0]
        pos += 2
        for _ in range(extents):
            value = int.from_bytes(iloc[pos:pos + offset_size], 'big')
            iloc[pos:pos + offset_size] = (value + delta).to_bytes(offset_size, 'big')
            pos += offset_size + length_size


def main():
    root = Path(__file__).resolve().parent.parent
    source = root / 'tests' / 'Assets' / 'sample.avif'
    target = root / 'tests' / 'Assets' / 'clap.avif'
    data = source.read_bytes()

    top = boxes(data, 0, len(data), skip=0)
    ftyp_offset, ftyp_size = find(top, b'ftyp')
    meta_offset, meta_size = find(top, b'meta')
    mdat_offset, mdat_size = find(top, b'mdat')
    if mdat_offset != meta_offset + meta_size:
        raise SystemExit("mdat must follow meta for this rewrite")

    meta_children = boxes(data, meta_offset, meta_size, skip=12)
    iprp_offset, iprp_size = find(meta_children, b'iprp')
    iprp_children = boxes(data, iprp_offset, iprp_size, skip=8)
    ipco_offset, ipco_size = find(iprp_children, b'ipco')
    ipma_offset, _ = find(iprp_children, b'ipma')
    pitm_offset, _ = find(meta_children, b'pitm')

    properties = boxes(data, ipco_offset, ipco_size, skip=8)
    ispe_offset, _ = find(properties, b'ispe')
    frame_width, frame_height = struct.unpack('>II', data[ispe_offset + 12:ispe_offset + 20])
    if not (0 < CROP_WIDTH <= frame_width and 0 < CROP_HEIGHT <= frame_height):
        raise SystemExit("crop must fit inside the coded frame")
    if (frame_width - CROP_WIDTH) % 2 or (frame_height - CROP_HEIGHT) % 2:
        raise SystemExit("a zero-offset aperture must be centred on an even difference")

    clap_index = len(properties) + 1
    primary = struct.unpack('>H', data[pitm_offset + 12:pitm_offset + 14])[0]

    version, flags, entries = item_associations(data, ipma_offset)
    for entry in entries:
        if entry[0] == primary:
            entry[1].append(clap_index | 0x80)  # essential
            break
    else:
        raise SystemExit("primary item is not associated with any property")
    new_ipma = build_ipma(version, flags, entries)

    clap = struct.pack('>IIIIIIII', CROP_WIDTH, 1, CROP_HEIGHT, 1, 0, 1, 0, 1)
    clap_box = struct.pack('>I', 8 + len(clap)) + b'clap' + clap
    ipco_body = data[ipco_offset + 8:ipco_offset + ipco_size] + clap_box
    new_ipco = struct.pack('>I', 8 + len(ipco_body)) + b'ipco' + ipco_body
    new_iprp = struct.pack('>I', 8 + len(new_ipco) + len(new_ipma)) + b'iprp' + new_ipco + new_ipma
    delta = len(new_iprp) - iprp_size

    pieces = []
    for box_type, offset, size in meta_children:
        if box_type == b'iprp':
            pieces.append(new_iprp)
        elif box_type == b'iloc':
            iloc = bytearray(data[offset:offset + size])
            shift_iloc_extents(iloc, delta)
            pieces.append(bytes(iloc))
        else:
            pieces.append(data[offset:offset + size])

    new_meta = (struct.pack('>I', 12 + sum(len(piece) for piece in pieces))
                + b'meta' + data[meta_offset + 8:meta_offset + 12] + b''.join(pieces))
    output = (data[ftyp_offset:ftyp_offset + ftyp_size] + new_meta
              + data[mdat_offset:mdat_offset + mdat_size])
    target.write_bytes(output)
    print(f"wrote {target.relative_to(root)}: coded {frame_width}x{frame_height}, "
          f"aperture {CROP_WIDTH}x{CROP_HEIGHT}, {len(data)} -> {len(output)} bytes")


if __name__ == '__main__':
    sys.exit(main())
