# docs

- [`language.md`](language.md): the RubberPlant L-system language specification.
- [`backends.md`](backends.md): geometry in 3D, mesh construction, and the output backends (SVG, OBJ, glTF).
- [`roadmap.md`](roadmap.md): milestones.
- [`fetch_refs.py`](fetch_refs.py): downloads and extracts the reference material.

## Reference material

The language follows *The Algorithmic Beauty of Plants* (Prusinkiewicz & Lindenmayer, 1990, "ABOP") and draws on the lpfg manual. Both are copyrighted and free to download from [algorithmicbotany.org](https://algorithmicbotany.org), so they're **not committed** here. To fetch and extract them locally:

```
uv run docs/fetch_refs.py               # download (if missing) + extract to markdown
uv run docs/fetch_refs.py --png 24 27   # render ABOP book pages 24 and 27 to PNG
```

Output (git-ignored):

| Path | Content |
|---|---|
| `docs/abop.pdf` | ABOP |
| `docs/abop/*.md` | ABOP text, one file per chapter, with `<!-- pdf p.N / book p.M -->` page markers |
| `docs/abop/pages/*.png` | rendered ABOP pages |
| `docs/lpfgman.pdf` | lpfg manual |
| `docs/lpfg/lpfgman.md` | lpfg manual text, with page markers |

The source of cpfg and lpfg (the software that made ABOP's figures, and its successor) is public in the [vlab repository](https://github.com/AlgorithmicBotany/vlab), and so is the [cpfg manual](https://algorithmicbotany.org/virtual_laboratory/docs/CPFGmanual.pdf). Use them to check how the original tools behave (`cpfg/interpret.c`, `cpfg/generate.c`, `lpfg/turtle.cpp`, `lpfg/lderive.cpp`). Clone them outside this repository; never commit them.
