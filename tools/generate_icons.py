#!/usr/bin/env python3
"""Generates the application and tray icons (.ico) without external dependencies.

Shapes are rendered from signed distance functions with 4x4 supersampling, encoded as PNG
and packed into multi-resolution ICO files.

    python3 tools/generate_icons.py
"""
import math
import os
import struct
import zlib

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "SynapseMouse.App", "Assets")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 4  # supersampling per axis


def sd_round_box(px, py, cx, cy, hw, hh, r):
    qx = abs(px - cx) - hw + r
    qy = abs(py - cy) - hh + r
    outside = math.hypot(max(qx, 0.0), max(qy, 0.0))
    inside = min(max(qx, qy), 0.0)
    return outside + inside - r


def sd_segment(px, py, ax, ay, bx, by):
    pax, pay = px - ax, py - ay
    bax, bay = bx - ax, by - ay
    h = max(0.0, min(1.0, (pax * bax + pay * bay) / (bax * bax + bay * bay)))
    return math.hypot(pax - bax * h, pay - bay * h)


def hex_rgba(value, alpha=255):
    value = value.lstrip("#")
    return (int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16), alpha)


def render(size, layers):
    """layers: list of (coverage_fn(x, y) -> bool, rgba). Coordinates are normalized 0..1."""
    pixels = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]  # premultiplied
            for sy in range(SS):
                for sx in range(SS):
                    u = (x + (sx + 0.5) / SS) / size
                    v = (y + (sy + 0.5) / SS) / size
                    r = g = b = a = 0.0
                    for inside, (cr, cg, cb, ca) in layers:
                        if inside(u, v, size):
                            fa = ca / 255.0
                            r = cr / 255.0 * fa + r * (1 - fa)
                            g = cg / 255.0 * fa + g * (1 - fa)
                            b = cb / 255.0 * fa + b * (1 - fa)
                            a = fa + a * (1 - fa)
                    acc[0] += r
                    acc[1] += g
                    acc[2] += b
                    acc[3] += a
            n = SS * SS
            a = acc[3] / n
            i = (y * size + x) * 4
            if a > 0:
                pixels[i] = min(255, round(acc[0] / n / a * 255))
                pixels[i + 1] = min(255, round(acc[1] / n / a * 255))
                pixels[i + 2] = min(255, round(acc[2] / n / a * 255))
            pixels[i + 3] = min(255, round(a * 255))
    return bytes(pixels)


def png(size, rgba):
    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))

    def chunk(kind, data):
        c = struct.pack(">I", len(data)) + kind + data
        return c + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def write_ico(path, images):
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    data = b""
    for size, blob in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset + len(data))
        data += blob
    with open(path, "wb") as f:
        f.write(header + entries + data)


def mouse_layers(size, body, stroke_color, fill=None, background=None, scale=1.0):
    """A mouse glyph: capsule body, button split line and scroll wheel."""
    px = 1.0 / size
    cx, cy = 0.5, 0.5
    hw, hh = 0.25 * scale, 0.37 * scale
    stroke = max(0.075 * scale, 1.15 * px)
    layers = []

    if background is not None:
        bg_fill, bg_border = background

        def bg(u, v, s):
            return sd_round_box(u, v, 0.5, 0.5, 0.47, 0.47, 0.2) <= 0

        def bg_edge(u, v, s):
            d = sd_round_box(u, v, 0.5, 0.5, 0.47, 0.47, 0.2)
            return -max(1.0 / s, 0.012) <= d <= 0

        layers.append((bg, bg_fill))
        layers.append((bg_edge, bg_border))

    radius = hw * 0.98

    if fill is not None:
        def body_fill(u, v, s):
            return sd_round_box(u, v, cx, cy, hw, hh, radius) <= 0

        layers.append((body_fill, fill))

    def outline(u, v, s):
        d = sd_round_box(u, v, cx, cy, hw, hh, radius)
        return abs(d + stroke / 2) <= stroke / 2

    layers.append((outline, body))

    top = cy - hh
    split_y = cy - hh * 0.18

    inner = stroke * 0.9

    def split(u, v, s):
        return sd_segment(u, v, cx, top + inner, cx, split_y) <= stroke * 0.42

    def cross(u, v, s):
        return sd_segment(u, v, cx - hw + inner, split_y, cx + hw - inner, split_y) <= stroke * 0.42

    layers.append((split, stroke_color))
    layers.append((cross, stroke_color))

    wheel_h = hh * 0.2

    def wheel(u, v, s):
        return sd_round_box(u, v, cx, top + hh * 0.33, stroke * 0.62, wheel_h / 2, stroke * 0.6) <= 0

    layers.append((wheel, stroke_color))
    return layers


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    white = hex_rgba("#FFFFFF")
    dark = hex_rgba("#0B0D12")
    gray = hex_rgba("#7C828D")

    app = [(s, png(s, render(s, mouse_layers(s, white, white, background=(dark, hex_rgba("#3A3F48")), scale=0.82))))
           for s in SIZES]
    write_ico(os.path.join(OUT_DIR, "app.ico"), app)

    tray_sizes = [16, 20, 24, 32, 40, 48, 64]
    on = [(s, png(s, render(s, mouse_layers(s, white, dark, fill=white, scale=1.18)))) for s in tray_sizes]
    write_ico(os.path.join(OUT_DIR, "tray-on.ico"), on)

    off = [(s, png(s, render(s, mouse_layers(s, gray, gray, scale=1.18)))) for s in tray_sizes]
    write_ico(os.path.join(OUT_DIR, "tray-off.ico"), off)
    print("Icons written to", os.path.abspath(OUT_DIR))


if __name__ == "__main__":
    main()
