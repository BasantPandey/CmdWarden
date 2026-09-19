# /// script
# requires-python = ">=3.11"
# dependencies = ["pywinpty", "pyte", "rich"]
# ///
"""Render `cw` commands to SVG screenshots for the docs.

Run:  uv run scripts/render-cli-shots.py [name ...]
Writes docs/images/cli-<name>.svg. Each command runs in a pseudo terminal so
colors stay on. Output goes through a terminal emulator so spinners and cursor
moves resolve to the final screen.
"""

import io
import re
import sys
from pathlib import Path

import pyte
from rich.console import Console
from rich.style import Style
from rich.terminal_theme import TerminalTheme
from rich.text import Text
from winpty import PtyProcess

COLS, ROWS = 90, 200
OUT = Path(__file__).resolve().parent.parent / "docs" / "images"

SHOTS = {
    "doctor": "cw doctor",
    "whoami": "cw whoami",
    "policy-list": "cw policy list",
    "harden-list": "cw harden --list",
    "scan": "cw scan",
    "audit": "cw audit -n 6",
}

# Windows Terminal "One Half Dark" palette.
THEME = TerminalTheme(
    (40, 44, 52),
    (220, 223, 228),
    [(40, 44, 52), (224, 108, 117), (152, 195, 121), (229, 192, 123),
     (97, 175, 239), (198, 120, 221), (86, 182, 194), (220, 223, 228)],
    [(90, 99, 116), (224, 108, 117), (152, 195, 121), (229, 192, 123),
     (97, 175, 239), (198, 120, 221), (86, 182, 194), (220, 223, 228)],
)


def capture(command: str) -> str:
    proc = PtyProcess.spawn(command, dimensions=(ROWS, COLS))
    chunks = []
    while True:
        try:
            chunks.append(proc.read())
        except EOFError:
            break
    return "".join(chunks)


def to_text(raw: str) -> Text:
    screen = pyte.Screen(COLS, ROWS)
    stream = pyte.Stream(screen)
    # A local dev build carries a prerelease tag; the docs show the release version.
    stream.feed(re.sub(r"(\d+\.\d+\.\d+)-dev\.\d+", r"\1", raw))
    lines = []
    for row in range(ROWS):
        line = Text()
        cells = screen.buffer[row]
        last = max((c for c in cells if cells[c].data.strip()), default=-1)
        run, run_style = "", None
        for col in range(last + 1):
            cell = cells[col]
            style = cell_style(cell)
            if style != run_style and run:
                line.append(run, style=run_style)
                run = ""
            run, run_style = run + (cell.data or " "), style
        if run:
            line.append(run, style=run_style)
        lines.append(line)
    while lines and not lines[-1].plain.strip():
        lines.pop()
    return Text("\n").join(lines)


def cell_style(cell) -> Style:
    fg = None if cell.fg == "default" else color(cell.fg)
    bg = None if cell.bg == "default" else color(cell.bg)
    return Style(color=fg, bgcolor=bg, bold=cell.bold, italic=cell.italics, underline=cell.underscore)


def color(name: str) -> str:
    # pyte names basic colors; 256-color and truecolor arrive as hex.
    return f"#{name}" if len(name) == 6 and all(c in "0123456789abcdef" for c in name) else name


def render(name: str, command: str) -> Path:
    text = to_text(capture(command))
    console = Console(record=True, width=COLS, file=io.StringIO(), force_terminal=True)
    console.print(text)
    path = OUT / f"cli-{name}.svg"
    path.write_text(console.export_svg(title=command, theme=THEME), encoding="utf-8")
    return path


if __name__ == "__main__":
    names = sys.argv[1:] or list(SHOTS)
    for n in names:
        print(render(n, SHOTS[n]))
