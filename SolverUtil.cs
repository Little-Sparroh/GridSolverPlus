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
    private readonly int _maxRotations = PlayerData.Instance.canRotateUpgrades ? 6 : 1;

    private readonly Dictionary<Tuple<UpgradeInstance, int>, Offset> _offsetCache = new();
    private readonly Dictionary<UpgradeInstance, List<int>> _uniqueRotationsCache = new();

    private bool _foundSolution;

    public Solver(IUpgradeWindow upgradeWindow, IUpgradable gear, List<UpgradeInstance> upgrades)
    {
        _upgradeWindow = upgradeWindow;
        _upgrades = upgrades;
        _gear = gear;
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

        for (int rot = 0; rot < _maxRotations; rot++)
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

    private void ClearSlots()
    {
        if (_equipSlots == null)
        {
            return;
        }

        var prefab = _gear;

        var gearUpgrades = PlayerData.GetAllUpgrades(prefab);

        var playerUpgrades = new List<UpgradeInfo>();
        if (prefab.GearType == GearType.Character)
        {
            IUpgradable playerUpgradesPrefab = Global.Instance;
            playerUpgrades = PlayerData.GetAllUpgrades(playerUpgradesPrefab);
        }

        var allUpgrades = gearUpgrades.Concat(playerUpgrades).Where(u => u is not null);
        foreach (var upgradeInfo in allUpgrades)
        {
            if (upgradeInfo.Instances is null) continue;
            foreach (var upgradeInstance in upgradeInfo.Instances.OfType<UpgradeInstance>())
            {
                _equipSlots.Unequip(prefab, upgradeInstance);
            }
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

        onComplete.Invoke(_foundSolution);
    }

    public void TrySolve(Action<bool> onComplete)
    {
        _foundSolution = false;
        ClearSlots();

        if (!CanFitAll())
        {
            onComplete.Invoke(false);
            return;
        }

        UpgradeSolver.Instance.SolverCoroutine = GridSolverPlugin.Instance.StartCoroutine(SolveAndNotify(onComplete));
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
