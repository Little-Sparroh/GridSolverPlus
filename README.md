# GridSolver+

A BepInEx mod for MycoPunk that automatically finds and places optimal upgrade arrangements on the hex grid.

Originally extracted from EnhancedUpgradeMenu as a standalone solver.

## Features

- **Hex Grid Solver**: Select upgrades and automatically place them in a valid layout
- **Rotation support**: Tries unique rotations when the player can rotate upgrades
- **Boundary Incursion aware**: Prioritizes and accounts for grid-expanding upgrades
- **Works in Gear Details and Ouro gear windows**
- **Solve / Cancel / Clear** controls while the upgrade window is open

## How to use

1. Open a gear upgrade window
2. Hover an unlocked upgrade and press **N** to select or deselect it
3. Click **Solve** to auto-place the selected upgrades
4. Use **Cancel** to stop a running solve, or **Clear** to deselect everything

Selected upgrades are highlighted. Non-stackable upgrades of the same type replace each other in the selection.

## Getting Started

### Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
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
1. Place the built `GridSolverPlus.dll` in your `<MycoPunk Directory>/BepInEx/plugins/` folder

### Executing program

The mod loads automatically through BepInEx when the game starts. Check the BepInEx console for a `GridSolverPlus` load message.

## Help

* **Mod not loading?** Verify BepInEx is installed correctly and check console logs for errors
* **Solve button missing?** Open a gear upgrade window (Gear Details or Ouro)
* **Selection not working?** Hover an unlocked upgrade and press **N**
* **No solution found?** The selected set may not fit; try fewer upgrades or include Boundary Incursion if needed

## Authors

- Sparroh
- funlennysub (original Hex Grid Solver)

## License

This project is licensed under the MIT License - see the LICENSE file for details
