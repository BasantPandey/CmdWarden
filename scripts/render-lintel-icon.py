"""Render the accepted Lintel product mark to a multi-size .ico.

Geometry is the 16x16 prototype (Fluent cyan tile, two posts + lintel).
Sizes: 16, 24, 32, 48, 256. Written as PNG-in-ICO so every size is stored.
"""
import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw

CYAN = (0x60, 0xCD, 0xFF, 255)
BLUE = (0x00, 0x78, 0xD4, 255)
WHITE = (0xF3, 0xF3, 0xF3, 255)
SIZES = (16, 24, 32, 48, 256)


def lerp(a: tuple[int, int, int, int], b: tuple[int, int, int, int], t: float) -> tuple[int, int, int, int]:
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))  # type: ignore[return-value]


def render(size: int) -> Image.Image:
    scale = size / 16.0
    gradient = Image.new("RGBA", (size, size))
    px = gradient.load()
    denom = 2 * max(size - 1, 1)
    for y in range(size):
        for x in range(size):
            px[x, y] = lerp(CYAN, BLUE, (x + y) / denom)

    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=3.5 * scale, fill=255)
    tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    tile.paste(gradient, mask=mask)

    draw = ImageDraw.Draw(tile)
    rr = max(0.6 * scale, 1)

    def box(x: float, y: float, w: float, h: float) -> list[float]:
        return [x * scale, y * scale, (x + w) * scale - 0.01, (y + h) * scale - 0.01]

    draw.rounded_rectangle(box(3, 4, 10, 3), radius=rr, fill=WHITE)
    draw.rounded_rectangle(box(3, 4, 3, 9), radius=rr, fill=WHITE)
    draw.rounded_rectangle(box(10, 4, 3, 9), radius=rr, fill=WHITE)
    return tile


def write_ico(path: Path, images: list[Image.Image]) -> None:
    pngs: list[bytes] = []
    for im in images:
        buf = BytesIO()
        im.save(buf, format="PNG")
        pngs.append(buf.getvalue())
    offset = 6 + 16 * len(pngs)
    parts = [struct.pack("<HHH", 0, 1, len(pngs))]
    blobs = b""
    for im, data in zip(images, pngs, strict=True):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        parts.append(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
        blobs += data
    path.write_bytes(b"".join(parts) + blobs)


def main() -> None:
    repo = Path(__file__).resolve().parents[1]
    out = repo / "src" / "CmdWarden.ApprovalGate" / "CmdWarden.ico"
    write_ico(out, [render(s) for s in SIZES])
    print(out)


if __name__ == "__main__":
    main()
