using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class HexMapUtils
{
    private static readonly FieldInfo _hexMapWidthField = typeof(HexMap).GetField("width", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _hexMapHeightField = typeof(HexMap).GetField("height", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _hexMapNodesField = typeof(HexMap).GetField("nodes", BindingFlags.NonPublic | BindingFlags.Instance);

    public static HexMap CloneHexMap(HexMap original)
    {
        if (original == null) return null;
        int width = (int)_hexMapWidthField.GetValue(original);
        int height = (int)_hexMapHeightField.GetValue(original);
        HexMap clone = new HexMap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                clone[x, y] = original[x, y];
            }
        }
        return clone;
    }

    public static HexMap CanonicalizeHexMap(HexMap map)
    {
        if (map == null) return null;
        int width = (int)_hexMapWidthField.GetValue(map);
        int height = (int)_hexMapHeightField.GetValue(map);

        int minX = int.MaxValue, minY = int.MaxValue;
        bool hasEnabled = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (map[x, y].enabled)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    hasEnabled = true;
                }
            }
        }
        if (!hasEnabled) return CloneHexMap(map);

        int newWidth = width - minX;
        int newHeight = height - minY;
        HexMap canonical = new HexMap(newWidth, newHeight);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (map[x, y].enabled)
                {
                    int newX = x - minX;
                    int newY = y - minY;
                    canonical[newX, newY] = map[x, y];
                }
            }
        }

        return canonical;
    }

    public static bool HexMapsEqual(HexMap a, HexMap b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        int widthA = (int)_hexMapWidthField.GetValue(a);
        int heightA = (int)_hexMapHeightField.GetValue(a);
        int widthB = (int)_hexMapWidthField.GetValue(b);
        int heightB = (int)_hexMapHeightField.GetValue(b);

        if (widthA != widthB || heightA != heightB) return false;

        for (int y = 0; y < heightA; y++)
        {
            for (int x = 0; x < widthA; x++)
            {
                var nodeA = a[x, y];
                var nodeB = b[x, y];
                if (nodeA.enabled != nodeB.enabled || (nodeA.enabled && nodeA.connections != nodeB.connections))
                    return false;
            }
        }
        return true;
    }
}

public class Offset(int offsetX, int offsetY)
{
    public readonly int OffsetX = offsetX;
    public readonly int OffsetY = offsetY;
}

public class Solver
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

    private static readonly FieldInfo _modifiedMapWidthField = GetPrivateField("width", typeof(HexMap));
    private static readonly FieldInfo _modifiedMapHeightField = GetPrivateField("height", typeof(HexMap));
    private static readonly FieldInfo _hexMapWidthField = GetPrivateField("width", typeof(HexMap));
    private static readonly FieldInfo _hexMapHeightField = GetPrivateField("height", typeof(HexMap));

    private readonly IUpgradeWindow _upgradeWindow;
    private readonly List<UpgradeInstance> _upgrades;
    private readonly IUpgradable _gear;
    private readonly ModuleEquipSlots _equipSlots;
    private readonly HexMap _hexMap;

    private readonly Dictionary<Tuple<UpgradeInstance, int>, Offset> _offsetCache = new();
    private readonly Dictionary<UpgradeInstance, List<int>> _uniqueRotationsCache = new();
    private readonly List<Tuple<UpgradeInstance, sbyte, sbyte, byte>> _previousLayout = new();

    private bool _foundSolution;

    /// <summary>Why the last solve failed, for display to the user.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>
    /// Rotation is unlocked per rarity tier (PlayerData.CanRotateUpgrade checks the
    /// master canRotateUpgrades flag OR the per-rarity upgradeRarityRotations bitmask),
    /// so compute the allowed rotations for each upgrade individually.
    /// </summary>
    private static int MaxRotationsFor(UpgradeInstance upgrade)
    {
        try
        {
            return PlayerData.Instance.CanRotateUpgrade(upgrade.Upgrade.Rarity) ? 6 : 1;
        }
        catch
        {
            // Fallback if the game API changes shape.
            return PlayerData.Instance.canRotateUpgrades ? 6 : 1;
        }
    }

    /// <summary>Checks that an upgrade instance actually belongs to the given gear.</summary>
    internal static bool UpgradeBelongsToGear(IUpgradable gear, UpgradeInstance instance)
    {
        if (gear is null || instance is null) return false;

        // Primary: the game's own ownership resolution (instance.Gear derives
        // from the instance's stored gearID).
        try
        {
            object owner = instance.Gear;
            if (owner is not null)
            {
                if (ReferenceEquals(owner, gear)) return true;
                // Shared player-pool upgrades are usable on character gear.
                if (gear.GearType == GearType.Character && ReferenceEquals(owner, Global.Instance)) return true;
            }
        }
        catch
        {
            // fall through to enumeration
        }

        // Fallback: scan everything equippable on this gear.
        try
        {
            foreach (var inst in EnumerateInstances(gear))
            {
                if (ReferenceEquals(inst, instance) || inst.InstanceID == instance.InstanceID)
                    return true;
            }
            return false;
        }
        catch
        {
            // If the game API changes shape, don't hard-block solving.
            return true;
        }
    }

    public Solver(IUpgradeWindow upgradeWindow, IUpgradable gear, List<UpgradeInstance> upgrades)
    {
        _upgradeWindow = upgradeWindow;
        _gear = gear;

        // Defense in depth: even if a caller hands us a stale or mixed selection,
        // never equip an upgrade onto gear it doesn't belong to.
        _upgrades = upgrades.Where(u =>
        {
            var ok = UpgradeBelongsToGear(gear, u);
            if (!ok)
                GridSolverPlugin.Logger.LogWarning($"Solver rejected '{u?.Upgrade?.Name}' — it does not belong to the target gear.");
            return ok;
        }).ToList();
        _equipSlots = GetPrivateField("equipSlots", upgradeWindow.GetType())?.GetValue(upgradeWindow) as ModuleEquipSlots;

        var hxmpField = GetPrivateField("hexMap", typeof(ModuleEquipSlots));
        if (_equipSlots != null && hxmpField != null)
            _hexMap = hxmpField.GetValue(_equipSlots) as HexMap;

        var canonicalPatterns = new Dictionary<string, List<UpgradeInstance>>();
        foreach (var upgrade in _upgrades)
        {
            var basePattern = upgrade.GetPattern();
            var canonicalKey = GetHexMapKey(HexMapUtils.CanonicalizeHexMap(basePattern));
            if (!canonicalPatterns.ContainsKey(canonicalKey))
                canonicalPatterns[canonicalKey] = new List<UpgradeInstance>();
            canonicalPatterns[canonicalKey].Add(upgrade);

            _uniqueRotationsCache[upgrade] = GetUniqueRotations(upgrade);
        }

        foreach (var kvp in canonicalPatterns)
        {
            if (kvp.Value.Count > 1)
            {
                var names = string.Join(", ", kvp.Value.Select(u => u.Upgrade.Name));
            }
        }
    }

    private List<int> GetUniqueRotations(UpgradeInstance upgrade)
    {
        var pattern = upgrade.GetPattern();
        var canonicalMaps = new Dictionary<string, int>();
        var uniqueRotations = new List<int>();
        var maxRotations = MaxRotationsFor(upgrade);

        for (int rot = 0; rot < maxRotations; rot++)
        {
            var rotatedMap = pattern.GetModifiedMap(rot);
            var canonical = HexMapUtils.CanonicalizeHexMap(rotatedMap);
            var key = GetHexMapKey(canonical);

            if (!canonicalMaps.ContainsKey(key))
            {
                canonicalMaps[key] = rot;
                uniqueRotations.Add(rot);
            }
            else
            {
            }
        }

        return uniqueRotations;
    }

    private string GetHexMapKey(HexMap map)
    {
        if (map == null) return "";
        int width = (int)_hexMapWidthField.GetValue(map);
        int height = (int)_hexMapHeightField.GetValue(map);
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var node = map[x, y];
                sb.Append(node.enabled ? '1' : '0');
                sb.Append((int)node.connections);
                sb.Append(',');
            }
            sb.Append(';');
        }
        return sb.ToString();
    }

    private Offset GetOffsetsCached(UpgradeInstance upgrade, int rotation, UpgradeEquipCell cell)
    {
        var key = new Tuple<UpgradeInstance, int>(upgrade, rotation);
        if (_offsetCache.TryGetValue(key, out var baseOffset))
            return new Offset(cell.X + baseOffset.OffsetX, cell.Y + baseOffset.OffsetY);
        var pattern = upgrade.GetPattern();
        var modifiedMap = pattern.GetModifiedMap(rotation);
        var modifiedMapWidth = _modifiedMapWidthField != null ? (int)_modifiedMapWidthField.GetValue(modifiedMap) : 8;
        var patternWidthHalf = modifiedMapWidth / 2;

        var sumY = 0;
        var enabledCount = 0;

        for (var row = 0; row < (_modifiedMapHeightField != null ? (int)_modifiedMapHeightField.GetValue(modifiedMap) : 8); row++)
        {
            var node = modifiedMap[patternWidthHalf, row];
            if (node.enabled != true) continue;
            sumY += row;
            enabledCount++;
        }

        var avgY = enabledCount > 0 ? sumY / enabledCount : 0;
        var offsetY = -avgY;
        if (patternWidthHalf % 2 == 1) offsetY--;

        baseOffset = new Offset(-patternWidthHalf, offsetY);
        _offsetCache[key] = baseOffset;

        return new Offset(cell.X + baseOffset.OffsetX, cell.Y + baseOffset.OffsetY);
    }

    /// <summary>All upgrade instances that can be equipped on this gear (including the shared player pool for characters).</summary>
    internal static IEnumerable<UpgradeInstance> EnumerateInstances(IUpgradable gear)
    {
        // IMPORTANT: PlayerData.GetAllUpgrades clears and refills a single shared
        // buffer and returns that same list every call. Copy the first result out
        // BEFORE making a second call, or the second call destroys the first.
        // (For character gear this previously erased the character's own upgrades,
        // leaving only the shared pool — making them unselectable and unclearable.)
        var merged = new List<UpgradeInfo>(PlayerData.GetAllUpgrades(gear));

        if (gear.GearType == GearType.Character)
        {
            IUpgradable playerUpgradesPrefab = Global.Instance;
            merged.AddRange(PlayerData.GetAllUpgrades(playerUpgradesPrefab));
        }

        foreach (var upgradeInfo in merged)
        {
            if (upgradeInfo?.Instances is null) continue;
            foreach (var upgradeInstance in upgradeInfo.Instances.OfType<UpgradeInstance>().ToList())
                yield return upgradeInstance;
        }
    }

    /// <summary>
    /// Records the position and rotation of everything currently equipped on the gear,
    /// so a failed solve can put the player's build back exactly as it was.
    /// </summary>
    private void SnapshotEquipped()
    {
        _previousLayout.Clear();
        foreach (var inst in EnumerateInstances(_gear))
        {
            if (!inst.IsEquipped(_gear)) continue;
            if (inst.GetPosition(_gear, out sbyte x, out sbyte y))
                _previousLayout.Add(new Tuple<UpgradeInstance, sbyte, sbyte, byte>(inst, x, y, inst.GetRotation(_gear)));
        }

        // Grid-expanding upgrades must be re-equipped first so the cells the
        // other mods sat in exist again when we restore.
        _previousLayout.Sort((a, b) =>
            (b.Item1.Upgrade.Name == "Boundary Incursion" ? 1 : 0)
                .CompareTo(a.Item1.Upgrade.Name == "Boundary Incursion" ? 1 : 0));
    }

    /// <summary>Re-equips the snapshotted layout after a failed solve.</summary>
    private void RestoreEquipped()
    {
        if (_equipSlots == null) return;
        foreach (var entry in _previousLayout)
        {
            if (!_equipSlots.EquipModule(_gear, entry.Item1, entry.Item2, entry.Item3, entry.Item4, true))
                GridSolverPlugin.Logger.LogWarning($"Could not restore '{entry.Item1.Upgrade.Name}' to its previous slot.");
        }
    }

    private void ClearSlots()
    {
        if (_equipSlots == null)
        {
            return;
        }

        // Unequip grid-expanding upgrades last so removing them doesn't
        // strand other mods in cells that stop existing.
        var instances = EnumerateInstances(_gear)
            .OrderBy(i => i.Upgrade.Name == "Boundary Incursion" ? 1 : 0)
            .ToList();
        foreach (var upgradeInstance in instances)
        {
            _equipSlots.Unequip(_gear, upgradeInstance);
        }
    }

    private IEnumerator SolveDfs(int index)
    {
        if (_equipSlots == null)
        {
            _foundSolution = false;
            yield break;
        }

        if (index >= _upgrades.Count)
        {
            _foundSolution = true;
            yield break;
        }

        var upgrade = _upgrades[index];
        var uniqueRotations = _uniqueRotationsCache[upgrade];

        for (var y = 0; y < (_hexMap != null && _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8); y++)
        for (var x = 0; x < (_hexMap != null && _hexMapWidthField != null ? (int)_hexMapWidthField.GetValue(_hexMap) : 8); x++)
        foreach (var rotation in uniqueRotations)
        {
            UpgradeEquipCell cell = null;
            try { cell = _equipSlots.GetCell(x, y); } catch { }
            if (cell is null || cell.Upgrade is not null)
                continue;

            var offset = GetOffsetsCached(upgrade, rotation, cell);
            var (offsetX, offsetY) = (offset.OffsetX, offset.OffsetY);

            if (!_equipSlots.EquipModule(_gear, upgrade, offsetX, offsetY, (byte)rotation, true))
                continue;

            yield return SolveDfs(index + 1);

            if (_foundSolution)
                yield break;

            _equipSlots.Unequip(_gear, upgrade);
        }
    }

    private IEnumerator SolveAndNotify(Action<bool> onComplete)
    {
        yield return SolveDfs(0);

        if (!_foundSolution)
        {
            FailureMessage = "No valid arrangement found for the selected upgrades. Your previous layout has been restored — deselect one or more upgrades and try again.";
            RestoreEquipped();
        }

        onComplete.Invoke(_foundSolution);
    }

    public void TrySolve(Action<bool> onComplete)
    {
        _foundSolution = false;
        FailureMessage = null;

        // If the ownership filter removed everything, there is nothing to place;
        // don't touch the grid at all.
        if (_upgrades.Count == 0)
        {
            FailureMessage = "None of the selected upgrades belong to this gear.";
            onComplete.Invoke(false);
            return;
        }

        // Snapshot the current build, then clear the grid so the fit check and
        // DFS both run against empty space. Checking fit BEFORE clearing was the
        // old behavior, and it wrongly refused solvable sets (especially with
        // Boundary Incursion, whose test placement failed on an occupied grid)
        // until the player manually removed every equipped mod.
        SnapshotEquipped();
        ClearSlots();

        if (!CanFitAll())
        {
            FailureMessage = "The selected upgrades cannot fit in the available space. Your previous layout has been restored.";
            RestoreEquipped();
            onComplete.Invoke(false);
            return;
        }

        UpgradeSolver.Instance.SolverCoroutine = GridSolverPlugin.Instance.StartCoroutine(SolveAndNotify(onComplete));
    }

    /// <summary>
    /// Called when the player cancels a running solve: removes whatever the DFS
    /// had partially placed and restores the pre-solve layout.
    /// </summary>
    public void AbortAndRestore()
    {
        if (_equipSlots == null) return;
        foreach (var upgrade in _upgrades.OrderBy(u => u.Upgrade.Name == "Boundary Incursion" ? 1 : 0))
            _equipSlots.Unequip(_gear, upgrade);
        RestoreEquipped();
    }

    public bool CanFitAll()
    {
        if (_hexMap == null)
        {
            return true;
        }

        var boundary = _upgrades.FirstOrDefault(u => u.Upgrade.Name == "Boundary Incursion");
        if (boundary != null)
        {
            bool boundaryPlaced = false;
            var originalHeight = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
            var originalWidth = _hexMapWidthField != null ? (int)_hexMapWidthField.GetValue(_hexMap) : 8;

            for (int y = 0; y < originalHeight && !boundaryPlaced; y++)
            {
                for (int x = 0; x < originalWidth && !boundaryPlaced; x++)
                {
                    var uniqueRots = _uniqueRotationsCache.ContainsKey(boundary) ? _uniqueRotationsCache[boundary] : new List<int> { 0 };
                    foreach (var rot in uniqueRots)
                    {
                        UpgradeEquipCell cell = null;
                        try { cell = _equipSlots.GetCell(x, y); } catch { }
                        if (cell != null && cell.Upgrade == null)
                        {
                            var offset = GetOffsetsCached(boundary, rot, cell);
                            if (_equipSlots.EquipModule(_gear, boundary, offset.OffsetX, offset.OffsetY, (byte)rot, true))
                            {
                                boundaryPlaced = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (!boundaryPlaced)
            {
                return false;
            }

            var newHeight = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
            var newWidth = _hexMapWidthField != null ? (int)_hexMapWidthField.GetValue(_hexMap) : 8;
            var newGridCells = newHeight * newWidth;

            var sumOthers = _upgrades.Where(u => u != boundary).Select(u => u.GetPattern().GetCellCount()).Sum();
            bool canFit = sumOthers <= newGridCells;

            _equipSlots.Unequip(_gear, boundary);

            return canFit;
        }
        else
        {
            var height = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
            var width = _hexMapWidthField != null ? (int)_hexMapWidthField.GetValue(_hexMap) : 8;
            var gridCells = height * width;
            var sumOfPatternCells = _upgrades.Select(u => u.GetPattern().GetCellCount()).Sum();
            return sumOfPatternCells <= gridCells;
        }
    }
}
