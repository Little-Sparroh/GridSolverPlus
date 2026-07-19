using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sparroh.UI;

public class SolverUI
{
    private static FieldInfo? GetPrivateField(string name, System.Type? type)
    {
        try
        {
            // Walk base types: GetField does not return private fields
            // declared on base classes.
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field;
                type = type.BaseType;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private readonly Color _defaultSecondaryColor = new(0.9434f, 0.9434f, 0.9434f, 1);
    private readonly Color _grayedOutColor = new(0.9434f, 0.9434f, 0.9434f, 0.1484f);

    internal IUpgradeWindow? UpgradeWindow;
    internal IUpgradable? CurrentGear;

    private readonly HashSet<Pigeon.UI.DefaultButton> _patchedButtons = new();
    private Dictionary<int, GearUpgradeUI> _selectedUpgrades = new();
    private GearUpgradeUI? _hoveredUpgrade;
    private bool _showSolveButton;
    private bool _solveButtonEnabled;
    private readonly HashSet<UpgradeInstance> _toggledThisSession = new();

    private readonly InputActionMap _solverControls;
    private readonly InputAction _addForSolve;

    private bool _barRegistered;
    private bool _lastSolveEnabled;
    private bool _lastCancelEnabled;
    private bool _lastClearEnabled;
    private bool _lastClearGridEnabled;
    private bool _buttonStateInitialized;

    public SolverUI(IUpgradeWindow? upgradeWindow, IUpgradable? currentGear)

    {

        UpgradeWindow = upgradeWindow;
        CurrentGear = currentGear;

        _solverControls = new InputActionMap("SolverControls");
        _addForSolve = _solverControls.AddAction("AddForSolve");
        _addForSolve.AddBinding("<Keyboard>/n");
    }

    internal void OnWindowOpened()
    {
        _showSolveButton = true;
        _solveButtonEnabled = false;
        EnsureButtons();
        RefreshButtonStates();
    }

    /// <summary>
    /// Clears the current selection and all per-gear transient state.
    /// Called when the window opens, closes, or the displayed gear changes,
    /// so a selection made for one gear can never be solved onto another.
    /// </summary>
    internal void ResetSelectionState()
    {
        ResetSelectionColors();
        _selectedUpgrades.Clear();
        _toggledThisSession.Clear();
        _hoveredUpgrade = null;
        _solveButtonEnabled = false;
        RefreshButtonStates();
    }

    private static readonly PropertyInfo? _detailsGearProperty =
        typeof(GearDetailsWindow).GetProperty("UpgradablePrefab", BindingFlags.Public | BindingFlags.Instance);
    private static readonly FieldInfo? _ouroGearField =
        typeof(OuroGearWindow).GetField("selectedGear", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Reads the gear currently displayed by the open upgrade window.</summary>
    private IUpgradable? GetGearFromWindow()
    {
        if (UpgradeWindow is null) return null;
        try
        {
            if (UpgradeWindow is GearDetailsWindow details)
                return _detailsGearProperty?.GetValue(details) as IUpgradable;
            if (UpgradeWindow is OuroGearWindow ouro)
                return _ouroGearField?.GetValue(ouro) as IUpgradable;
        }
        catch
        {
            // fall through
        }
        return CurrentGear;
    }

    /// <summary>
    /// Detects when the player switches gear inside an already-open window
    /// (no OnOpen/OnClose fires for that), and resets stale selection state
    /// so mods selected for one weapon can't be equipped onto another.
    /// Returns true if the gear changed.
    /// </summary>
    internal bool SyncGearWithWindow()
    {
        if (UpgradeWindow is null) return false;

        var windowGear = GetGearFromWindow();
        if (windowGear is null || ReferenceEquals(windowGear, CurrentGear))
            return false;

        GridSolverPlugin.Logger.LogInfo("Gear changed in open window; clearing solver selection.");
        CurrentGear = windowGear;
        ResetSelectionState();
        PatchUpgradeClick();
        return true;
    }

    /// <summary>True if the given upgrade UI element belongs to the currently open window.</summary>
    private bool IsUpgradeUIInCurrentWindow(GearUpgradeUI ui)
    {
        if (UpgradeWindow is null || ui == null) return false;

        // Check against the whole window, not just upgradeListParent: windows can
        // have multiple upgrade sections (e.g. GearDetailsWindow's temp storage),
        // and upgrades there must remain selectable.
        if (UpgradeWindow is Component windowComponent && windowComponent != null)
            return ui.transform.IsChildOf(windowComponent.transform);

        var upgradeListParentField = GetPrivateField("upgradeListParent", UpgradeWindow.GetType());
        var upgradeListParent = upgradeListParentField?.GetValue(UpgradeWindow) as Transform;
        if (upgradeListParent != null)
            return ui.transform.IsChildOf(upgradeListParent);

        // Can't resolve the window's transform at all: don't block selection —
        // UpgradeBelongsToGear still guards against cross-gear equips.
        return true;
    }

    /// <summary>Ensure bar slots exist even if OnWindowOpened was missed.</summary>
    internal void EnsureBarIfNeeded()
    {
        if (GearActionBar.IsGearMenuOpen())
        {
            _showSolveButton = true;
            EnsureButtons();
            RefreshButtonStates();
        }
    }



    private void SelectUpgrade()
    {
        if (_hoveredUpgrade == null || !_hoveredUpgrade.Upgrade.IsUnlocked)
            return;

        // Never select an upgrade that isn't part of the currently open window's
        // list, or that doesn't belong to the gear this window is editing.
        // This prevents cross-gear selections that would equip mods onto the wrong item.
        if (!IsUpgradeUIInCurrentWindow(_hoveredUpgrade) ||
            !Solver.UpgradeBelongsToGear(CurrentGear, _hoveredUpgrade.Upgrade))
            return;

        var upgrade = _hoveredUpgrade.Upgrade;
        var rarity = Global.Instance.Rarities[(int)upgrade.Upgrade.Rarity];

        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));
        var hoverColorField = GetPrivateField("hoverColor", typeof(Pigeon.UI.DefaultButton));

        if (buttonField == null)
            return;

        if (_selectedUpgrades.TryGetValue(upgrade.InstanceID, out var selectedUI))
        {
            var btn = buttonField.GetValue(selectedUI) as Pigeon.UI.DefaultButton;
            if (btn != null) btn.SetDefaultColor(rarity.backgroundColor);
            _selectedUpgrades.Remove(upgrade.InstanceID);
            _solveButtonEnabled = _selectedUpgrades.Count > 0;
            RefreshButtonStates();
            return;
        }

        if (!upgrade.CanStack)
        {
            // Deselect any other selected copy of this same upgrade type.
            // (The old comparison reflected on an 'upgradeID' field that doesn't
            // exist on Upgrade, so this dedup never actually ran.)
            var conflictingUpgrade = _selectedUpgrades.Values
                .FirstOrDefault(u =>
                    u != null && u.Upgrade != null &&
                    u.Upgrade.SourceUpgradeID.Equals(upgrade.SourceUpgradeID));

            if (conflictingUpgrade != null)
            {
                var backgroundColor = Global.Instance.Rarities[(int)conflictingUpgrade.Upgrade.Upgrade.Rarity]
                    .backgroundColor;
                var btn = buttonField.GetValue(conflictingUpgrade) as Pigeon.UI.DefaultButton;
                if (btn != null) btn.SetDefaultColor(backgroundColor);
                _selectedUpgrades.Remove(conflictingUpgrade.Upgrade.InstanceID);
            }
        }

        var btn2 = buttonField.GetValue(_hoveredUpgrade) as Pigeon.UI.DefaultButton;
        if (btn2 != null && hoverColorField != null)
        {
            var hoverColor = hoverColorField.GetValue(btn2) as Color?;
            if (hoverColor.HasValue) btn2.SetDefaultColor(hoverColor.Value);
        }
        _selectedUpgrades[upgrade.InstanceID] = _hoveredUpgrade;
        _solveButtonEnabled = true;
        RefreshButtonStates();
    }

    internal void RebuildSelectedUpgrades()
    {
        if (UpgradeWindow is null) return;

        var upgradeListParentField = GetPrivateField("upgradeListParent", UpgradeWindow.GetType());
        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));
        var hoverColorField = GetPrivateField("hoverColor", typeof(Pigeon.UI.DefaultButton));

        if (upgradeListParentField == null || buttonField == null) return;

        var upgradeListParent = upgradeListParentField.GetValue(UpgradeWindow) as Transform;
        if (upgradeListParent == null) return;

        var upgradeUIs = upgradeListParent.GetComponentsInChildren<GearUpgradeUI>().Where(x => _selectedUpgrades.ContainsKey(x.Upgrade.InstanceID)).ToList();

        _selectedUpgrades = upgradeUIs.ToDictionary(ui => ui.Upgrade.InstanceID);

        foreach (var selectedUpgradeKv in _selectedUpgrades)
        {
            var btn = buttonField.GetValue(selectedUpgradeKv.Value) as Pigeon.UI.DefaultButton;
            var hoverColor = hoverColorField?.GetValue(btn) as Color?;
            if (btn != null && hoverColor.HasValue) btn.SetDefaultColor(hoverColor.Value);
        }

        _solveButtonEnabled = _selectedUpgrades.Count > 0;
        RefreshButtonStates();
    }

    internal void Close()
    {
        _solverControls.Disable();
        _showSolveButton = false;

        _patchedButtons.Clear();
        _selectedUpgrades.Clear();
        _toggledThisSession.Clear();
        _hoveredUpgrade = null;
        _solveButtonEnabled = false;
        var coroutine = UpgradeSolver.Instance.SolverCoroutine;
        if (coroutine is not null)
        {
            GridSolverPlugin.Instance.StopCoroutine(coroutine);
            UpgradeSolver.Instance.SolverCoroutine = null;
            _activeSolver?.AbortAndRestore();
        }
        _activeSolver = null;
        // Keep slots registered; GearActionBar hides the whole bar when gear menu closes.
        // Only disable interactable state.
        RefreshButtonStates();
    }



    internal void Update()
    {
        // If the player switched gear inside the open window, drop the stale
        // selection before processing any input this frame.
        SyncGearWithWindow();

        if (Keyboard.current != null)
        {
            bool isKeyPressed = Keyboard.current.nKey.isPressed;

            if (!isKeyPressed)
            {
                if (_toggledThisSession.Count > 0)
                    _toggledThisSession.Clear();
            }
            else
            {
                GearUpgradeUI hoveredUI = null;
                if (UIRaycaster.RaycastForComponent<GearUpgradeUI>(out hoveredUI))
                {
                    var upgrade = hoveredUI.Upgrade;
                    if (upgrade != null && !_toggledThisSession.Contains(upgrade))
                    {
                        _hoveredUpgrade = hoveredUI;
                        SelectUpgrade();
                        _toggledThisSession.Add(upgrade);
                    }
                }
            }
        }

        GearActionBar.Tick();
        EnsureBarIfNeeded();
    }

    private void EnsureButtons()
    {
        if (_barRegistered)
            return;

        GearActionBar.Register("clear_grid", "Unequip All", GearActionBar.OrderClearGrid, OnClearGridClicked, UIButtonStyle.Danger);
        GearActionBar.Register("solve", "Solve", GearActionBar.OrderSolve, OnSolveClicked, UIButtonStyle.Primary);
        GearActionBar.Register("cancel_solve", "Cancel", GearActionBar.OrderCancelSolve, OnCancelClicked, UIButtonStyle.Danger);
        GearActionBar.Register("clear_selection", "Deselect", GearActionBar.OrderClearSelection, OnClearClicked, UIButtonStyle.Default);
        // Always keep all four visible while registered; interactable reflects state
        GearActionBar.SetSlotVisible("clear_grid", true);
        GearActionBar.SetSlotVisible("solve", true);
        GearActionBar.SetSlotVisible("cancel_solve", true);
        GearActionBar.SetSlotVisible("clear_selection", true);
        _barRegistered = true;
    }

    private void RefreshButtonStates()
    {
        if (!_barRegistered)
            return;

        bool solving = UpgradeSolver.Instance != null && UpgradeSolver.Instance.SolverCoroutine != null;
        bool solveEnabled = _solveButtonEnabled && !solving;
        bool cancelEnabled = solving;
        bool clearEnabled = _selectedUpgrades.Count > 0 && !solving;
        bool clearGridEnabled = _showSolveButton && !solving;

        if (_buttonStateInitialized
            && solveEnabled == _lastSolveEnabled
            && cancelEnabled == _lastCancelEnabled
            && clearEnabled == _lastClearEnabled
            && clearGridEnabled == _lastClearGridEnabled)
            return;

        _buttonStateInitialized = true;
        _lastSolveEnabled = solveEnabled;
        _lastCancelEnabled = cancelEnabled;
        _lastClearEnabled = clearEnabled;
        _lastClearGridEnabled = clearGridEnabled;

        GearActionBar.SetInteractable("solve", solveEnabled);
        GearActionBar.SetInteractable("cancel_solve", cancelEnabled);
        GearActionBar.SetInteractable("clear_selection", clearEnabled);
        GearActionBar.SetInteractable("clear_grid", clearGridEnabled);
    }




    private Solver? _activeSolver;

    private void OnSolveClicked()
    {
        if (UpgradeWindow == null || !_solveButtonEnabled || UpgradeSolver.Instance.SolverCoroutine != null)
            return;

        // Final safety net: if the displayed gear changed since selection, the
        // selection was just cleared and there is nothing valid to solve.
        if (SyncGearWithWindow())
            return;

        if (CurrentGear is null)
        {
            UIDialog.Alert("Solve Error", "Could not determine which gear this window is editing.");
            return;
        }

        // Prune destroyed UI entries and any upgrade that doesn't belong to the
        // current gear, so the solver can never equip mods onto the wrong item.
        var staleKeys = _selectedUpgrades
            .Where(kv => kv.Value == null || !Solver.UpgradeBelongsToGear(CurrentGear, kv.Value.Upgrade))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in staleKeys)
        {
            GridSolverPlugin.Logger.LogWarning($"Dropping stale/foreign selected upgrade (InstanceID {key}) before solve.");
            _selectedUpgrades.Remove(key);
        }

        if (_selectedUpgrades.Count == 0)
        {
            _solveButtonEnabled = false;
            RefreshButtonStates();
            return;
        }

        foreach (var selectedUpgrade in _selectedUpgrades.Values)
            GridSolverPlugin.Logger.LogInfo($"\t • {FormatUpgrade(selectedUpgrade)}");

        var upgrades = _selectedUpgrades
            .Select(u => u.Value.Upgrade)
            .OrderByDescending(u => u.Upgrade.Name == "Boundary Incursion" ? int.MaxValue : u.GetPattern().GetCellCount())
            .ToList();

        // Note: no fit check here. The old pre-check ran against the still-occupied
        // grid and wrongly refused solvable sets until every mod was removed by hand.
        // TrySolve snapshots the build, clears the grid, then checks fit properly.
        var solver = new Solver(UpgradeWindow, CurrentGear, upgrades);
        _activeSolver = solver;

        solver.TrySolve(success =>
        {
            GridSolverPlugin.Logger.LogInfo(success ? "Found a solution" : "No solution");
            UpgradeSolver.Instance.SolverCoroutine = null;
            _activeSolver = null;

            if (success)
            {
                ResetSelectionColors();
                _selectedUpgrades.Clear();
                _solveButtonEnabled = false;
            }
            else
            {
                // Keep the selection so the player can just deselect one or two
                // upgrades and retry, instead of re-picking everything.
                UIDialog.Alert("Solve Failed",
                    solver.FailureMessage ?? "No valid arrangement found for the selected upgrades.");
                RebuildSelectedUpgrades();
                GridSolverPlugin.Instance.StartCoroutine(RebuildSelectionNextFrame());
            }
            RefreshButtonStates();
        });
        RefreshButtonStates();
    }

    /// <summary>
    /// Equip/unequip during a failed solve can rebuild the window's upgrade list,
    /// destroying the UI objects our selection points at. Re-resolve them one
    /// frame later so highlights survive.
    /// </summary>
    private System.Collections.IEnumerator RebuildSelectionNextFrame()
    {
        yield return null;
        RebuildSelectedUpgrades();
    }

    private void OnCancelClicked()
    {
        if (UpgradeSolver.Instance.SolverCoroutine != null)
        {
            GridSolverPlugin.Instance.StopCoroutine(UpgradeSolver.Instance.SolverCoroutine);
            UpgradeSolver.Instance.SolverCoroutine = null;

            // Undo the partial placement and put the pre-solve build back.
            _activeSolver?.AbortAndRestore();
            _activeSolver = null;
            RebuildSelectedUpgrades();
            GridSolverPlugin.Instance.StartCoroutine(RebuildSelectionNextFrame());

            RefreshButtonStates();
        }
    }

    private void OnClearClicked()
    {
        ResetSelectionColors();
        _selectedUpgrades.Clear();
        _solveButtonEnabled = false;
        RefreshButtonStates();
    }

    /// <summary>
    /// Unequips every mod from the gear currently shown in the window
    /// (weapon, character, grenade, etc). Does not touch the solve selection.
    /// </summary>
    private void OnClearGridClicked()
    {
        if (UpgradeWindow == null || UpgradeSolver.Instance.SolverCoroutine != null)
            return;

        SyncGearWithWindow();
        if (CurrentGear is null)
            return;

        var equipSlotsField = GetPrivateField("equipSlots", UpgradeWindow.GetType());
        var equipSlots = equipSlotsField?.GetValue(UpgradeWindow) as ModuleEquipSlots;
        if (equipSlots == null)
        {
            GridSolverPlugin.Logger.LogWarning("Unequip All: could not access the window's equip slots.");
            return;
        }

        // Unequip grid-expanding upgrades last so removing them doesn't strand
        // other mods in cells that stop existing.
        var equipped = Solver.EnumerateInstances(CurrentGear)
            .Where(inst => inst.IsEquipped(CurrentGear))
            .OrderBy(inst => inst.Upgrade.Name == "Boundary Incursion" ? 1 : 0)
            .ToList();

        var removed = 0;
        foreach (var inst in equipped)
        {
            if (equipSlots.Unequip(CurrentGear, inst))
                removed++;
        }
        GridSolverPlugin.Logger.LogInfo($"Unequip All: removed {removed} mod(s) from the current gear.");
    }

    private void ResetSelectionColors()
    {
        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));
        foreach (var upgradeUI in _selectedUpgrades.Values)
        {
            try
            {
                if (upgradeUI == null) continue; // pooled UI may have been destroyed/re-bound
                var rarity = Global.Instance.Rarities[(int)upgradeUI.Upgrade.Upgrade.Rarity];
                var btn = buttonField?.GetValue(upgradeUI) as Pigeon.UI.DefaultButton;
                if (btn != null) btn.SetDefaultColor(rarity.backgroundColor);
            }
            catch
            {
                // Stale UI entry; nothing to restore.
            }
        }
    }

    private static string FormatUpgrade(GearUpgradeUI upgrade)
    {
        var upgradeInstance = upgrade.Upgrade;
        return $"[{upgradeInstance.Upgrade.RarityName}] {upgradeInstance.Upgrade.Name} ({upgradeInstance.InstanceID})";
    }

    internal void PatchUpgradeClick()
    {
        if (UpgradeWindow is null) return;

        _solverControls.Enable();

        var upgradeListParentField = GetPrivateField("upgradeListParent", UpgradeWindow.GetType());
        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));

        if (upgradeListParentField == null || buttonField == null)
            return;

        var upgradeListParent = upgradeListParentField.GetValue(UpgradeWindow) as Transform;
        if (upgradeListParent == null)
            return;

        var upgradeUIs = upgradeListParent.GetComponentsInChildren<GearUpgradeUI>().Where(x => x.gameObject.activeSelf).ToList();

        foreach (var upgradeUI in upgradeUIs)
        {
            var btn = buttonField.GetValue(upgradeUI) as Pigeon.UI.DefaultButton;
            if (btn is null)
                continue;

            // Key by the button reference, not the upgrade instance: the game
            // pools these buttons and re-binds them when gear changes, which
            // would otherwise stack duplicate hover listeners.
            if (!_patchedButtons.Add(btn)) continue;

            btn.OnHoverExit.AddListener(() => _hoveredUpgrade = null);
            btn.OnHoverEnter.AddListener(() => _hoveredUpgrade = upgradeUI);
        }
    }

    // Kept for plugin compatibility; UI is now uGUI
    public void OnGUI() { }
}
