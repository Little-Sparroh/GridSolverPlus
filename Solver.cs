using System.Collections;
using UnityEngine;

public class UpgradeSolver
{
    public Coroutine? SolverCoroutine;

    internal static UpgradeSolver Instance;
    internal SolverUI SolverUI = new(null, null);

    public void Update()
    {
        SolverUI.Update();
        // Register Solve/Cancel/Deselect even if window-open hook was missed
        if (Sparroh.UI.GearActionBar.IsGearMenuOpen())
            SolverUI.EnsureBarIfNeeded();
    }


    internal void OnUpgradeWindowOpen(IUpgradeWindow window, IUpgradable gear, MonoBehaviour mono)
    {
        // Drop any selection left over from a previous window/gear. If a close
        // event was ever missed, this prevents old selections from being solved
        // onto (and equipped on) a different piece of gear.
        SolverUI.ResetSelectionState();

        SolverUI.UpgradeWindow = window;
        SolverUI.CurrentGear = gear;
        SolverUI.OnWindowOpened();
        SolverUI.PatchUpgradeClick();
        mono.StartCoroutine(DelayPatch());
    }

    private IEnumerator DelayPatch()
    {
        yield return null;
        SolverUI.PatchUpgradeClick();
    }

    internal void OnUpgradeWindowClosed()
    {
        SolverUI.Close();
    }

    public void OnGUI()
    {
        SolverUI.OnGUI();
    }
}
