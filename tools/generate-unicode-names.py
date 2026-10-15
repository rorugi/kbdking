#!/usr/bin/env python3
"""Regenerates src/Kbdking.Core/Unicode/UnicodeNames.txt.gz from Python's unicodedata module.

Format (UTF-8, one line per character): <hex code point>;<name>[;<alias>...]
Algorithmically named ranges (CJK ideographs, Hangul syllables, ...) are left out; the C# side
generates those names itself. Run with the newest Python available to pick up the newest Unicode version.
"""
import gzip
import unicodedata
from pathlib import Path

SKIP = ("CJK UNIFIED IDEOGRAPH", "HANGUL SYLLABLE", "TANGUT IDEOGRAPH", "CJK COMPATIBILITY IDEOGRAPH",
        "KHITAN SMALL SCRIPT", "NUSHU CHARACTER", "EGYPTIAN HIEROGLYPH")

out = Path(__file__).resolve().parent.parent / "src/Kbdking.Core/Unicode/UnicodeNames.txt.gz"
lines = [f"#unicode {unicodedata.unidata_version}"]
for cp in range(0x110000):
    if 0xD800 <= cp <= 0xDFFF or 0xE000 <= cp <= 0xF8FF or cp >= 0xF0000:
        continue
    try:
        name = unicodedata.name(chr(cp))
    except ValueError:
        continue
    if name.startswith(SKIP):
        continue
    lines.append(f"{cp:04X};{name}")
data = ("\n".join(lines) + "\n").encode("utf-8")
out.write_bytes(gzip.compress(data, 9, mtime=0))
print(f"{len(lines) - 1} names, {len(data)} bytes, {out.stat().st_size} compressed -> {out}")
