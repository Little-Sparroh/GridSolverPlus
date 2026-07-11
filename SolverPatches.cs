using HarmonyLib;
using System.Reflection;

public static class UpgradeSolverPatches
{
    private static readonly PropertyInfo _upgradablePrefabProperty = typeof(GearDetailsWindow).GetProperty("UpgradablePrefab", BindingFlags.Public | BindingFlags.Instance);
    private static readonly FieldInfo _selectedGearField = typeof(OuroGearWindow).GetField("selectedGear", BindingFlags.NonPublic | BindingFlags.Instance);

    [HarmonyPatch(typeof(GearDetailsWindow), "OnOpen")]
    public static class GearDetailsWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GearDetailsWindow __instance)
        {
            var gear = _upgradablePrefabProperty?.GetValue(__instance) as IUpgradable;
            UpgradeSolver.Instance.OnUpgradeWindowOpen(__instance, gear, GridSolverPlugin.Instance);
        }
    }

    [HarmonyPatch(typeof(GearDetailsWindow), "OnCloseCallback")]
    public static class GearDetailsWindowClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            UpgradeSolver.Instance.OnUpgradeWindowClosed();
        }
    }

    [HarmonyPatch(typeof(OuroGearWindow), "OnOpen")]
    public static class OuroGearWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(OuroGearWindow __instance)
        {
            var gear = _selectedGearField?.GetValue(__instance) as IUpgradable;
            UpgradeSolver.Instance.OnUpgradeWindowOpen(__instance, gear, GridSolverPlugin.Instance);
        }
    }

    [HarmonyPatch(typeof(OuroGearWindow), "OnCloseCallback")]
    public static class OuroGearWindowClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            UpgradeSolver.Instance.OnUpgradeWindowClosed();
        }
    }
}
