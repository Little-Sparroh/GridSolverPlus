using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency("sparroh.uilibrary")]
[MycoMod(null, ModFlags.IsClientSide)]
public class GridSolverPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.gridsolverplus";
    public const string PluginName = "GridSolverCoru";
    public const string PluginVersion = "1.1.0";

    internal static ManualLogSource Logger;
    public static GridSolverPlugin Instance;

    private UpgradeSolver upgradeSolver;

    private void Awake()
    {
        Logger = base.Logger;
        Instance = this;

        upgradeSolver = new UpgradeSolver();
        UpgradeSolver.Instance = upgradeSolver;

        var harmony = new Harmony(PluginGUID);
        harmony.PatchAll();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        try
        {
            upgradeSolver?.SolverUI?.Close();
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Failed to close UpgradeSolver UI: {ex.Message}");
        }
    }

    private void Update()
    {
        upgradeSolver?.Update();
    }

    private void OnGUI()
    {
        // Buttons are uGUI now; OnGUI retained as no-op for compatibility
        upgradeSolver?.OnGUI();
    }
}
