# ABOP Verification Notes & Errata Candidates

Findings recorded while implementing *The Algorithmic Beauty of Plants* (ABOP) Chapters 1–3 and cross-checking RubberPlant against `cpfg` (the software that produced ABOP's figures) and the ABOP PDF.

---

## 1. Semantics Resolved Against `cpfg`

RubberPlant's 3D turtle geometry matches `cpfg` for all 12 Chapter 2 figures (to rounding):

- **Roll (`/` and `\`):** `cpfg`'s turtle frame starts with `U = (0, 0, -1)` and `/` turns `L` toward that `U`. RubberPlant keeps a right-handed frame (`U = (0, 0, 1)`) and turns `/` toward `-U` ([`language.md`](language.md) §5.1).
- **Tropism:** `cpfg`'s old `bend()` applies two sequential rotations with sines `e · (T̂ · U)` then `e · (T̂ · L)` after each move ([`language.md`](language.md) §5.5). This agrees with ABOP's errata for p. 61 (the printed formula gives the *sine* of the bend, not the angle).

---

## 2. Book Findings (Errata Candidates)

### Chapter 2

- **Table 2.1, row b (Fig 2.6b):** `r1 = 0.8`, not the printed `0.9`. With `r1 = 0.8`, `cpfg` matches Fig 2.6b line for line. Used in [`examples/fig2-6b.rp`](../examples/fig2-6b.rp).
- **Table 2.3, row c (Fig 2.8c):** The printed `lr = 1.790` grows the trunk 105-fold over 8 steps into a tall column rather than the book's figure. Free-parameter fits against the book's strokes settle near `lr = 1.2` (`1.18–1.21`), while `1.079` fits worse. [`examples/fig2-8c.rp`](../examples/fig2-8c.rp) keeps Table 2.3 as printed; [`examples/fig2-8c-figure.rp`](../examples/fig2-8c-figure.rp) is the marked reconstruction with `lr = 1.2` (main limbs fall near the figure, though not line for line).
- **Figs 2.8c & 2.8d width ratio:** Neither matches the printed L-system line for line even in `cpfg`; empirical fits prefer a width ratio near `√2` over the printed `√3` (`vr = 1.732`). Unexplained.
- **Per-figure viewing angles:** ABOP selected a custom camera view per figure rather than one fixed front view:
  - Figs 2.6d and 2.7 are viewed from `+X` (examples prepend `/(90)`).
  - Figs 2.8a and 2.8c are viewed from below (about 45–54° under the horizon).
  - Fig 2.8b is viewed about 20° below the horizon.
  - Fig 2.8d's view remains unresolved.
  - Camera/view settings in `.rp` are deferred ([`language.md`](language.md) §7).

### Chapter 3

- **p. 68 (signal model, §3.1.2):** Printed derivation strings 9–10 do not follow from the printed productions (`p8` yields `I[L]I[L]I[L]I[L]A`). RubberPlant's tests keep the printed productions.
- **Mycelis model III (L-system 3.3):** As printed, L-system 3.3 does not reproduce model II's development (`cpfg` agrees).

### Chapters 4–6

- **`.s` canonical alignment (Fig 5.1) & `PRECISION`:** RubberPlant reads both `S:` and `T:` in `PRECISION` (fixing `cpfg/patches.c`'s `s`-twice typo) and applies the exact transpose $B_s^T$ when transforming `.s` control points into canonical surface space (`v0.3-organs-animation.md` §4.2).
- **Compound leaves (Figs 5.11b, 5.12a):** Verified segment-by-segment against `cpfg` (`87,380` segments within `1.95e-6` of height for `fig5-11b-compound-leaf`; `35,420` segments within `2.13e-6` of height for `fig5-12a-compound-leaf`).
- **Rose leaf (Fig 5.8):** The printed L-system (p. 126) omits tropism and view parameters; `fig5-8-rose-leaf.rp` adds lateral tropism `(1, 0, 0)` with `elasticity = 0.03` (marked as a RubberPlant addition) to match the book's curved midrib (p. 127).
- **Timed DOL-systems (Ch 6):** Verified `fig6-3-anabaena.rp` (ABOP eq. 6.2 & eq. 6.7–6.9) and `fig6-4-timed-branching.rp` (eqs. 6.10–6.15) derivations and continuous developmental sequences.

### Reporting Status

- **Reported:** p. 68 (§3.1.2) was emailed to Prof. Prusinkiewicz.
- **Not yet sent:** The Chapter 2 (Tables 2.1/2.3, Figs 2.8a–d) and Mycelis model III findings have not been sent to the authors yet.

---

## 3. Modeling Conventions & Open Design Notes

- **Printed files vs. reconstructions:** Primary example files preserve the book's printed constants. When reproducing an illustration requires altered constants or extra rules, the fitted version lives in a separate, marked `*-figure.rp` file. L-system schemata (e.g., ABOP §3.1) are completed with explicitly commented RubberPlant additions.
- **Context-sensitive precedence:** Examples list context-sensitive productions above context-free ones for each predecessor so they derive identically under RubberPlant's ABOP §1.8 precedence and `cpfg`'s source-order rule. ABOP §1.8 precedence stays for now and may be revisited once all of ABOP is implemented.
