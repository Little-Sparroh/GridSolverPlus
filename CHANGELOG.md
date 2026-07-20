# Changelog

## 1.1.0
- **Rewrote the auto-placer.** The old solver was a naive backtracker with no pruning that ran one placement per frame — on simple repeated shapes (e.g. 5 identical Y-shaped upgrades) it would leave 1-cell gaps and endlessly rotate the smaller pieces, and on tight sets it could appear frozen for minutes. It is now a bitmask packer with area, connected-region, most-constrained-piece, and exact-cover pruning. Typical layouts solve in milliseconds, and genuinely impossible sets are reported quickly instead of hanging.
- **Handheld Pocket Universe now moves.** HPU is positioned by the solver alongside the other upgrades instead of being forced into the first free slot, so it no longer defaults to the top-left corner.
- **Fixed a false "no solution" case.** Region pruning could reject layouts that actually fit when they left a small empty pocket; the packer now only rejects sets that are truly impossible.
- Boundary Incursion, Multiversal Thievery, Edge Fault, and Handheld Pocket Universe support is otherwise unchanged.

## 1.0.0
- Fork of Sparroh's GridSolverPlus with major enhancements
- **Handheld Pocket Universe support**: Inflates upgrade cell counts per rarity (Standard=1, Rare=2, Epic=3, Exotic=4)
- **Multiversal Thievery support**: Grid-expander, placed before all other expanders (adds up to 3 columns)
- **Edge Fault support**: Grid-expander, placed alongside Boundary Incursion (adds column/row)
- **Four-tier placement priority**: Major expanders (MT) → Minor expanders (BI, EF) → Cell modifiers (HPU) → Everything else
- **Unequip All button**: Clear all upgrades from the current gear with one click
- **Gear-change detection**: Auto-clears selection when switching gear inside an open window
- **Selection preserved on failure**: Failed solves keep your selection so you can deselect one or two and retry
- **Snapshot and restore**: Failed solves restore your previous upgrade layout exactly
- **Robust reflection**: Base-type walking for private field access, cross-gear ownership validation
