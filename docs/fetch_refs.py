# /// script
# requires-python = ">=3.12"
# dependencies = ["pymupdf"]
# ///
"""Fetch the reference PDFs and extract them to searchable markdown.

The PDFs are copyrighted and are not committed to this repo. They are
downloaded from algorithmicbotany.org (free to download for personal use).

Usage:
    uv run docs/fetch_refs.py               # download (if missing) + extract all
    uv run docs/fetch_refs.py --png 24 27   # render ABOP book pages 24 and 27 to PNG

Output (git-ignored):
    docs/abop.pdf, docs/abop/*.md, docs/abop/pages/*.png
    docs/lpfgman.pdf, docs/lpfg/lpfgman.md
"""

import sys
import urllib.request
from pathlib import Path

import pymupdf

DOCS = Path(__file__).resolve().parent

ABOP_URL = "https://algorithmicbotany.org/papers/abop/abop.pdf"
ABOP_PDF = DOCS / "abop.pdf"
ABOP_OUT = DOCS / "abop"
ABOP_OFFSET = 12  # book page = pdf page - ABOP_OFFSET

LPFG_URL = "https://www.algorithmicbotany.org/lstudio/LPFGman.pdf"
LPFG_PDF = DOCS / "lpfgman.pdf"
LPFG_OUT = DOCS / "lpfg"

# ABOP sections: (slug, title, first pdf page, last pdf page), 1-indexed inclusive
ABOP_SECTIONS = [
    ("00-front", "Front matter, Preface, Contents", 1, 12),
    ("01-graphical-modeling", "Ch 1: Graphical modeling using L-systems", 13, 62),
    ("02-trees", "Ch 2: Modeling of trees", 63, 74),
    ("03-herbaceous", "Ch 3: Developmental models of herbaceous plants", 75, 110),
    ("04-phyllotaxis", "Ch 4: Phyllotaxis", 111, 130),
    ("05-organs", "Ch 5: Models of plant organs", 131, 144),
    ("06-animation", "Ch 6: Animation of plant development", 145, 156),
    ("07-cellular-layers", "Ch 7: Modeling of cellular layers", 157, 186),
    ("08-fractals", "Ch 8: Fractal properties of plants", 187, 202),
    ("09-epilogue", "Epilogue", 203, 204),
    ("10-appendix-a", "Appendix A: Software environment", 205, 212),
    ("11-appendix-b", "Appendix B: About the figures", 213, 222),
    ("12-bibliography", "Bibliography", 223, 236),
    ("13-index", "Index", 237, 240),
]

LIGATURES = {"\ufb00": "ff", "\ufb01": "fi", "\ufb02": "fl", "\ufb03": "ffi", "\ufb04": "ffl"}


def clean(text: str) -> str:
    for k, v in LIGATURES.items():
        text = text.replace(k, v)
    return text


def fetch(url: str, dest: Path) -> None:
    if dest.exists():
        return
    print(f"downloading {url}")
    tmp = dest.with_suffix(".part")
    urllib.request.urlretrieve(url, tmp)
    tmp.replace(dest)


def extract_abop() -> None:
    doc = pymupdf.open(ABOP_PDF)
    ABOP_OUT.mkdir(parents=True, exist_ok=True)
    index = ["# ABOP extracted text", "", f"book page = pdf page - {ABOP_OFFSET}", ""]
    for slug, title, first, last in ABOP_SECTIONS:
        parts = [f"# {title}", ""]
        for p in range(first, last + 1):
            parts.append(f"\n<!-- pdf p.{p} / book p.{p - ABOP_OFFSET} -->\n")
            parts.append(clean(doc[p - 1].get_text()))
        (ABOP_OUT / f"{slug}.md").write_text("\n".join(parts), encoding="utf-8")
        index.append(
            f"- `{slug}.md`: {title} (pdf {first}-{last}, book {first - ABOP_OFFSET}-{last - ABOP_OFFSET})"
        )
    (ABOP_OUT / "README.md").write_text("\n".join(index) + "\n", encoding="utf-8")
    print(f"extracted ABOP to {ABOP_OUT}")


def extract_lpfg() -> None:
    doc = pymupdf.open(LPFG_PDF)
    LPFG_OUT.mkdir(parents=True, exist_ok=True)
    parts = ["# LPFG manual (extracted text)", ""]
    for i, page in enumerate(doc, start=1):
        parts.append(f"\n<!-- page {i} -->\n")
        parts.append(clean(page.get_text()))
    (LPFG_OUT / "lpfgman.md").write_text("\n".join(parts), encoding="utf-8")
    print(f"extracted LPFG manual to {LPFG_OUT}")


def render_abop_page(book_page: int, zoom: float = 2.0) -> None:
    doc = pymupdf.open(ABOP_PDF)
    out = ABOP_OUT / "pages" / f"book-p{book_page:03d}.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    doc[book_page + ABOP_OFFSET - 1].get_pixmap(matrix=pymupdf.Matrix(zoom, zoom)).save(out)
    print(out)


def main(argv: list[str]) -> None:
    fetch(ABOP_URL, ABOP_PDF)
    fetch(LPFG_URL, LPFG_PDF)
    if len(argv) >= 2 and argv[0] == "--png":
        for n in argv[1:]:
            render_abop_page(int(n))
        return
    extract_abop()
    extract_lpfg()


if __name__ == "__main__":
    main(sys.argv[1:])
