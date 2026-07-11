using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sparroh.UI;

public class SolverUI
{
    private static FieldInfo? GetPrivateField(string name, System.Type type)
    {
        try
        {
            return type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        }
        catch
        {
            return null;
        }
    }

    private readonly Color _defaultSecondaryColor = new(0.9434f, 0.9434f, 0.9434f, 1);
    private readonly Color _grayedOutColor = new(0.9434f, 0.9434f, 0.9434f, 0.1484f);

    internal IUpgradeWindow? UpgradeWindow;
    internal IUpgradable? CurrentGear;

    private readonly Dictionary<int, UnityEvent> _originalOnHoverEnters = new();
    private Dictionary<int, GearUpgradeUI> _selectedUpgrades = new();
    private GearUpgradeUI? _hoveredUpgrade;
    private bool _showSolveButton;
    private bool _solveButtonEnabled;
    private readonly HashSet<UpgradeInstance> _toggledThisSession = new();

    private readonly InputActionMap _solverControls;
    private readonly InputAction _addForSolve;

    private bool _barRegistered;

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

        var upgrade = _hoveredUpgrade.Upgrade;
        var rarity = Global.Instance.Rarities[(int)upgrade.Upgrade.Rarity];

        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));
        var idField = GetPrivateField("upgradeID", typeof(Upgrade));
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
            var conflictingUpgrade = _selectedUpgrades.Values
                .FirstOrDefault(u =>
                    (!upgrade.CanStack && idField != null && (int)idField.GetValue(u.Upgrade) == (int)idField.GetValue(upgrade.Upgrade)));

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

        _originalOnHoverEnters.Clear();
        _selectedUpgrades.Clear();
        var coroutine = UpgradeSolver.Instance.SolverCoroutine;
        if (coroutine is not null) GridSolverPlugin.Instance.StopCoroutine(coroutine);
        // Keep slots registered; GearActionBar hides the whole bar when gear menu closes.
        // Only disable interactable state.
        RefreshButtonStates();
    }



    internal void Update()
    {
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

        GearActionBar.Register("solve", "Solve", GearActionBar.OrderSolve, OnSolveClicked, UIButtonStyle.Primary);
        GearActionBar.Register("cancel_solve", "Cancel", GearActionBar.OrderCancelSolve, OnCancelClicked, UIButtonStyle.Danger);
        GearActionBar.Register("clear_selection", "Deselect", GearActionBar.OrderClearSelection, OnClearClicked, UIButtonStyle.Default);
        // Always keep all three visible while registered; interactable reflects state
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
        GearActionBar.SetSlotVisible("solve", true);
        GearActionBar.SetSlotVisible("cancel_solve", true);
        GearActionBar.SetSlotVisible("clear_selection", true);
        GearActionBar.SetInteractable("solve", _solveButtonEnabled && !solving);
        GearActionBar.SetInteractable("cancel_solve", solving);
        GearActionBar.SetInteractable("clear_selection", _selectedUpgrades.Count > 0 && !solving);
    }



    private void OnSolveClicked()
    {
        if (UpgradeWindow == null || !_solveButtonEnabled || UpgradeSolver.Instance.SolverCoroutine != null)
            return;

        foreach (var selectedUpgrade in _selectedUpgrades.Values)
            GridSolverPlugin.Logger.LogInfo($"\t • {FormatUpgrade(selectedUpgrade)}");

        var upgrades = _selectedUpgrades
            .Select(u => u.Value.Upgrade)
            .OrderByDescending(u => u.Upgrade.Name == "Boundary Incursion" ? int.MaxValue : u.GetPattern().GetCellCount())
            .ToList();

        var solver = new Solver(UpgradeWindow, CurrentGear, upgrades);
        if (!solver.CanFitAll())
        {
            UIDialog.Alert("Solve Error", "The selected upgrades cannot fit in the available space.");
            return;
        }

        solver.TrySolve(success =>
        {
            GridSolverPlugin.Logger.LogInfo(success ? "Found a solution" : "No solution");
            ResetSelectionColors();
            _selectedUpgrades.Clear();
            _solveButtonEnabled = false;
            UpgradeSolver.Instance.SolverCoroutine = null;
            RefreshButtonStates();
        });
        RefreshButtonStates();
    }

    private void OnCancelClicked()
    {
        if (UpgradeSolver.Instance.SolverCoroutine != null)
        {
            GridSolverPlugin.Instance.StopCoroutine(UpgradeSolver.Instance.SolverCoroutine);
            UpgradeSolver.Instance.SolverCoroutine = null;
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

    private void ResetSelectionColors()
    {
        var buttonField = GetPrivateField("button", typeof(GearUpgradeUI));
        foreach (var upgradeUI in _selectedUpgrades.Values)
        {
            var rarity = Global.Instance.Rarities[(int)upgradeUI.Upgrade.Upgrade.Rarity];
            var btn = buttonField?.GetValue(upgradeUI) as Pigeon.UI.DefaultButton;
            if (btn != null) btn.SetDefaultColor(rarity.backgroundColor);
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

            var instanceID = upgradeUI.Upgrade.InstanceID;
            if (_originalOnHoverEnters.ContainsKey(instanceID)) continue;

            var originalOnHoverEnter = btn.OnHoverEnter;
            _originalOnHoverEnters[instanceID] = originalOnHoverEnter;

            btn.OnHoverExit.AddListener(() => _hoveredUpgrade = null);
            btn.OnHoverEnter.AddListener(() => _hoveredUpgrade = upgradeUI);
        }
    }

    // Kept for plugin compatibility; UI is now uGUI
    public void OnGUI() { }
}
