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
    private static readonly HashSet<string> GridExpandingUpgradeNames = new()
    {
        "Boundary Incursion",
        "Edge Fault",
        "Multiversal Thievery"
    };

    private static readonly HashSet<string> MajorGridExpanderNames = new()
    {
        "Multiversal Thievery"
    };

    private static readonly HashSet<string> CellCountModifyingUpgradeNames = new()
    {
        "Handheld Pocket Universe"
    };

    private static bool HasName(Upgrade u) => u?.Name != null;
    private static bool HasName(UpgradeInstance i) => i?.Upgrade?.Name != null;

    internal static bool IsMajorGridExpander(Upgrade upgrade) =>
        HasName(upgrade) && MajorGridExpanderNames.Contains(upgrade.Name);

    internal static bool IsMajorGridExpander(UpgradeInstance inst) =>
        HasName(inst) && MajorGridExpanderNames.Contains(inst.Upgrade.Name);

    internal static bool IsGridExpander(Upgrade upgrade) =>
        HasName(upgrade) && GridExpandingUpgradeNames.Contains(upgrade.Name);

    internal static bool IsGridExpander(UpgradeInstance inst) =>
        HasName(inst) && GridExpandingUpgradeNames.Contains(inst.Upgrade.Name);

    internal static bool IsCellCountModifier(Upgrade upgrade) =>
        HasName(upgrade) && CellCountModifyingUpgradeNames.Contains(upgrade.Name);

    internal static bool IsCellCountModifier(UpgradeInstance inst) =>
        HasName(inst) && CellCountModifyingUpgradeNames.Contains(inst.Upgrade.Name);

    private static int GetEffectiveCellCount(UpgradeInstance upgrade, bool hasHPU)
    {
        int baseCount = upgrade.GetPattern().GetCellCount();
        if (!hasHPU) return baseCount;
        if (IsCellCountModifier(upgrade)) return baseCount;

        string rarityName = upgrade.Upgrade.RarityName;
        switch (rarityName)
        {
            case "Standard": return 1;
            case "Rare":     return 2;
            case "Epic":     return 3;
            case "Exotic":   return 4;
            default:         return baseCount;
        }
    }

    internal static int GetPlacementPriority(UpgradeInstance inst)
    {
        if (IsMajorGridExpander(inst)) return 3;
        if (IsGridExpander(inst))      return 2;
        if (IsCellCountModifier(inst)) return 1;
        return 0;
    }

    internal static int GetPlacementPriority(Upgrade upgrade)
    {
        if (IsMajorGridExpander(upgrade)) return 3;
        if (IsGridExpander(upgrade))      return 2;
        if (IsCellCountModifier(upgrade)) return 1;
        return 0;
    }

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

    public string? FailureMessage { get; private set; }

    private static int MaxRotationsFor(UpgradeInstance upgrade)
    {
        try
        {
            return PlayerData.Instance.CanRotateUpgrade(upgrade.Upgrade.Rarity) ? 6 : 1;
        }
        catch
        {
            return PlayerData.Instance.canRotateUpgrades ? 6 : 1;
        }
    }

    internal static bool UpgradeBelongsToGear(IUpgradable gear, UpgradeInstance instance)
    {
        if (gear is null || instance is null) return false;

        try
        {
            object owner = instance.Gear;
            if (owner is not null)
            {
                if (ReferenceEquals(owner, gear)) return true;
                if (gear.GearType == GearType.Character && ReferenceEquals(owner, Global.Instance)) return true;
            }
        }
        catch { }

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
            return true;
        }
    }

    public Solver(IUpgradeWindow upgradeWindow, IUpgradable gear, List<UpgradeInstance> upgrades)
    {
        _upgradeWindow = upgradeWindow;
        _gear = gear;

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

    internal static IEnumerable<UpgradeInstance> EnumerateInstances(IUpgradable gear)
    {
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

    private void SnapshotEquipped()
    {
        _previousLayout.Clear();
        foreach (var inst in EnumerateInstances(_gear))
        {
            if (!inst.IsEquipped(_gear)) continue;
            if (inst.GetPosition(_gear, out sbyte x, out sbyte y))
                _previousLayout.Add(new Tuple<UpgradeInstance, sbyte, sbyte, byte>(inst, x, y, inst.GetRotation(_gear)));
        }

        // Major expanders (MT) first, then minor expanders (BI/EF), then HPU, then normal.
        _previousLayout.Sort((a, b) =>
            GetPlacementPriority(b.Item1).CompareTo(GetPlacementPriority(a.Item1)));
    }

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
        if (_equipSlots == null) return;

        var instances = EnumerateInstances(_gear)
            .OrderBy(i => GetPlacementPriority(i))
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

        if (_upgrades.Count == 0)
        {
            FailureMessage = "None of the selected upgrades belong to this gear.";
            onComplete.Invoke(false);
            return;
        }

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

    public void AbortAndRestore()
    {
        if (_equipSlots == null) return;
        foreach (var upgrade in _upgrades.OrderBy(u => GetPlacementPriority(u)))
            _equipSlots.Unequip(_gear, upgrade);
        RestoreEquipped();
    }

    public bool CanFitAll()
    {
        if (_hexMap == null)
            return true;

        bool hasHPU = _upgrades.Any(u => IsCellCountModifier(u));
        var expanders = _upgrades.Where(u => IsGridExpander(u)).ToList();
        var cellModifiers = _upgrades.Where(u => IsCellCountModifier(u)).ToList();
        var normal = _upgrades.Where(u => !IsGridExpander(u) && !IsCellCountModifier(u)).ToList();

        // Phase 1: Place all grid-expanders (major first via DFS sort, but here
        // the loop order IS the sort order — major expanders are placed first)
        foreach (var expander in expanders)
        {
            bool placed = false;
            var originalHeight = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
            var originalWidth  = _hexMapWidthField  != null ? (int)_hexMapWidthField .GetValue(_hexMap) : 8;
            var uniqueRots = _uniqueRotationsCache.TryGetValue(expander, out var rots)
                ? rots : new List<int> { 0 };

            for (int y = 0; y < originalHeight && !placed; y++)
            for (int x = 0; x < originalWidth && !placed; x++)
            foreach (var rot in uniqueRots)
            {
                UpgradeEquipCell cell = null;
                try { cell = _equipSlots.GetCell(x, y); } catch { }
                if (cell == null || cell.Upgrade != null) continue;

                var offset = GetOffsetsCached(expander, rot, cell);
                if (_equipSlots.EquipModule(_gear, expander,
                        offset.OffsetX, offset.OffsetY, (byte)rot, true))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                foreach (var pe in expanders)
                {
                    if (ReferenceEquals(pe, expander)) break;
                    _equipSlots.Unequip(_gear, pe);
                }
                return false;
            }
        }

        // Phase 2: Place cell-count modifiers (HPU)
        foreach (var mod in cellModifiers)
        {
            bool placed = false;
            var currentHeight = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
            var currentWidth  = _hexMapWidthField  != null ? (int)_hexMapWidthField .GetValue(_hexMap) : 8;
            var uniqueRots = _uniqueRotationsCache.TryGetValue(mod, out var rots)
                ? rots : new List<int> { 0 };

            for (int y = 0; y < currentHeight && !placed; y++)
            for (int x = 0; x < currentWidth && !placed; x++)
            foreach (var rot in uniqueRots)
            {
                UpgradeEquipCell cell = null;
                try { cell = _equipSlots.GetCell(x, y); } catch { }
                if (cell == null || cell.Upgrade != null) continue;

                var offset = GetOffsetsCached(mod, rot, cell);
                if (_equipSlots.EquipModule(_gear, mod,
                        offset.OffsetX, offset.OffsetY, (byte)rot, true))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                foreach (var pe in expanders)
                    _equipSlots.Unequip(_gear, pe);
                foreach (var pm in cellModifiers)
                {
                    if (ReferenceEquals(pm, mod)) break;
                    _equipSlots.Unequip(_gear, pm);
                }
                return false;
            }
        }

        // Phase 3: Count unoccupied cells
        var height = _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;
        var width  = _hexMapWidthField  != null ? (int)_hexMapWidthField .GetValue(_hexMap) : 8;
        int unoccupied = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            try
            {
                var cell = _equipSlots.GetCell(x, y);
                if (cell != null && cell.Upgrade == null)
                    unoccupied++;
            }
            catch { }
        }

        // Phase 4: Sum inflated cell counts
        var sumOfPatternCells = normal.Select(u => GetEffectiveCellCount(u, hasHPU)).Sum();

        // Cleanup
        foreach (var pe in expanders)
            _equipSlots.Unequip(_gear, pe);
        foreach (var pm in cellModifiers)
            _equipSlots.Unequip(_gear, pm);

        return sumOfPatternCells <= unoccupied;
    }
}