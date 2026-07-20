# GridSolverCoru

A BepInEx mod for MycoPunk that automatically finds and places optimal upgrade arrangements on the hex grid.

A fork of Sparroh's **GridSolverPlus**, itself built on funlennysub's original Hex Grid Solver, with broader upgrade support and a rewritten solver.

## Features

- **Fast constraint-based solver**: Packs your selected upgrades into a valid layout using a bitmask search with area, connected-region, most-constrained-piece, and exact-cover pruning. Typical layouts solve in milliseconds, and genuinely impossible sets are reported quickly instead of hanging.
- **Rotation support**: Tries unique rotations when the player can rotate upgrades
- **Grid-expander aware**: Handles Boundary Incursion, Multiversal Thievery, and Edge Fault, expanding the board before packing the rest
- **Handheld Pocket Universe support**: Applies the rarity-based pattern shrink and positions the HPU alongside everything else rather than dumping it in a corner
- **Unequip All**: Clear every upgrade from the current gear with one click
- **Gear-change detection**: Auto-clears your selection when you switch gear inside an open window
- **Works in Gear Details and Ouro gear windows**
- **Solve / Cancel / Deselect / Unequip All** controls while the upgrade window is open

## How to use

1. Open a gear upgrade window
2. Hover an unlocked upgrade and press **N** to select or deselect it
3. Click **Solve** to auto-place the selected upgrades
4. Use **Cancel** to stop a running solve, or **Deselect** to clear your selection

Selected upgrades are highlighted. Non-stackable upgrades of the same type replace each other in the selection. A failed solve keeps your selection and restores your previous layout, so you can deselect one or two upgrades and try again.

## Getting Started

### Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* [SparrohUILib](https://thunderstore.io/c/mycopunk/p/Sparroh/SparrohUILib/) - Version 1.1.1 or compatible
* .NET Framework 4.8
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via NuGet)

### Building/Compiling

1. Clone this repository
2. Open the solution file in Visual Studio, Rider, or your preferred C# IDE
3. Build the project in Release mode to generate the .dll file

Alternatively, use dotnet CLI:
```bash
dotnet build --configuration Release
```

### Installing

**Via Thunderstore (Recommended)**:
1. Download and install via Thunderstore Mod Manager
2. The mod will be automatically installed to the correct directory

**Manual Installation**:
1. Place the built `GridSolverCoru.dll` in your `<MycoPunk Directory>/BepInEx/plugins/` folder

### Executing program

The mod loads automatically through BepInEx when the game starts. Check the BepInEx console for a `GridSolverCoru` load message.

## Help

* **Mod not loading?** Verify BepInEx and SparrohUILib are installed correctly and check console logs for errors
* **Solve button missing?** Open a gear upgrade window (Gear Details or Ouro)
* **Selection not working?** Hover an unlocked upgrade and press **N**
* **No solution found?** The selected set genuinely does not fit the available cells; deselect one or two upgrades, or include a grid expander (Boundary Incursion, Multiversal Thievery, or Edge Fault)

## Authors

- Coruscnium (GridSolverCoru fork)
- Sparroh (GridSolverPlus)
- funlennysub (original Hex Grid Solver)

## License

This project is licensed under the MIT License - see the LICENSE file for details
