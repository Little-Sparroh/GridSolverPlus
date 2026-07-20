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

    // Upgrades placed live before the bitmask packer runs, because equipping them
    // changes the grid's size (expanders) and, for the multi-HPU edge case, other
    // pieces' patterns. Everything else is packed together by the bitmask solver.
    private List<UpgradeInstance> _specials;
    private List<UpgradeInstance> _normals;
    private List<UpgradeInstance> _expanders;

    // A single Handheld Pocket Universe is folded into the joint packer so it gets
    // positioned alongside the normal pieces instead of dumped first-fit. Null when
    // there is no HPU, or more than one (they alter each other's patterns).
    private UpgradeInstance _hpu;

    private readonly Dictionary<Tuple<UpgradeInstance, int>, Offset> _offsetCache = new();
    private readonly Dictionary<UpgradeInstance, List<int>> _uniqueRotationsCache = new();
    private readonly List<Tuple<UpgradeInstance, sbyte, sbyte, byte>> _previousLayout = new();

    private bool _foundSolution;

    // Safety net so a pathological search can never hard-freeze the game.
    private const long NormalSolveNodeCap = 6_000_000;
    private const long NormalSolveTimeCapMs = 4000;

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

        // Grid expanders must be equipped before the grid geometry is known, so
        // they are always placed live first (major expanders first).
        _expanders = _upgrades.Where(u => IsGridExpander(u))
                              .OrderByDescending(u => GetPlacementPriority(u))
                              .ToList();
        var hpus = _upgrades.Where(u => IsCellCountModifier(u)).ToList();
        _normals = _upgrades.Where(u => GetPlacementPriority(u) == 0).ToList();

        if (hpus.Count == 1)
        {
            // Fold the lone HPU into the joint packer.
            _hpu = hpus[0];
            _specials = _expanders;
        }
        else
        {
            // No HPU, or several (which interact) — keep HPU(s) in the live phase.
            _hpu = null;
            _specials = _expanders.Concat(hpus)
                                  .OrderByDescending(u => GetPlacementPriority(u))
                                  .ToList();
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
        foreach (var inst in _upgrades)
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

        foreach (var upgradeInstance in _upgrades.OrderBy(i => GetPlacementPriority(i)))
        {
            _equipSlots.Unequip(_gear, upgradeInstance);
        }
    }

    private int GridWidth() =>
        _hexMap != null && _hexMapWidthField != null ? (int)_hexMapWidthField.GetValue(_hexMap) : 8;

    private int GridHeight() =>
        _hexMap != null && _hexMapHeightField != null ? (int)_hexMapHeightField.GetValue(_hexMap) : 8;

    /// <summary>
    /// Places the grid-affecting upgrades (expanders + HPU) live and recursively,
    /// because equipping them changes the grid's size and other pieces' patterns.
    /// Each expander layout that succeeds hands the now-final grid to the fast
    /// bitmask packer for all the normal pieces; if that fails we backtrack the
    /// expander and try the next spot. With no specials this reduces to a single
    /// bitmask solve.
    /// </summary>
    private IEnumerator SolveDfs(int index)
    {
        if (_equipSlots == null)
        {
            _foundSolution = false;
            yield break;
        }

        if (index >= _specials.Count)
        {
            _foundSolution = SolveNormals();
            yield break;
        }

        var upgrade = _specials[index];
        var uniqueRotations = _uniqueRotationsCache[upgrade];

        int h = GridHeight(), w = GridWidth();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        foreach (var rotation in uniqueRotations)
        {
            UpgradeEquipCell cell = null;
            try { cell = _equipSlots.GetCell(x, y); } catch { }
            if (cell is null || cell.Upgrade is not null)
                continue;

            var offset = GetOffsetsCached(upgrade, rotation, cell);

            if (!_equipSlots.EquipModule(_gear, upgrade, offset.OffsetX, offset.OffsetY, (byte)rotation, true))
                continue;

            yield return SolveDfs(index + 1);

            if (_foundSolution)
                yield break;

            _equipSlots.Unequip(_gear, upgrade);
        }
    }

    // ------------------------------------------------------------------
    // Fast bitmask packer for the normal pieces.
    //
    // Runs entirely in memory against a snapshot of the (now final) grid, so it
    // needs no per-frame coroutine yielding. Once a full packing is found we
    // apply it to the live grid with EquipModule. Placement cells are computed
    // with the exact math ModuleEquipSlots.EquipModule uses, so the masks match
    // what the game will actually occupy.
    // ------------------------------------------------------------------

    private struct Placement
    {
        public ulong[] Mask;
        public int OffsetX;
        public int OffsetY;
        public byte Rotation;
    }

    private static int WordCount(int cellCount) => (cellCount + 63) / 64;

    private static int PopCount(ulong[] a)
    {
        int c = 0;
        for (int i = 0; i < a.Length; i++)
        {
            ulong v = a[i];
            while (v != 0) { v &= v - 1; c++; }
        }
        return c;
    }

    private static bool Intersects(ulong[] a, ulong[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if ((a[i] & b[i]) != 0) return true;
        return false;
    }

    /// <summary>
    /// Computes the grid cells a rotated pattern covers at the given offset, as
    /// linear indices y*W+x, or null if any cell would fall off the board. Mirrors
    /// ModuleEquipSlots.EquipModule / CanEquip exactly.
    /// </summary>
    private List<int> ComputeCells(HexMap modMap, int offsetX, int offsetY, int W, int H)
    {
        int modW = _modifiedMapWidthField != null ? (int)_modifiedMapWidthField.GetValue(modMap) : 8;
        int modH = _modifiedMapHeightField != null ? (int)_modifiedMapHeightField.GetValue(modMap) : 8;
        int num = offsetX + modW / 2;

        var cells = new List<int>(8);
        for (int row = 0; row < modH; row++)
        for (int col = 0; col < modW; col++)
        {
            bool enabled;
            try { enabled = modMap[col, row].enabled; } catch { continue; }
            if (!enabled) continue;

            int cx = col + offsetX;
            int cy = row + offsetY;
            if (col % 2 == 1 && cx % 2 == 0) cy++;
            if (num % 2 == 1) cy++;
            if (num % 2 == 1 && (modW / 2) % 2 == 0) cy--;

            if (cx < 0 || cy < 0 || cx >= W || cy >= H) return null;
            cells.Add(cy * W + cx);
        }
        return cells;
    }

    /// <summary>All valid, de-duplicated placements of one piece on the current grid.</summary>
    private List<Placement> EnumeratePlacements(UpgradeInstance upgrade, int W, int H, int words, ulong[] baseOccupied)
    {
        var result = new List<Placement>();
        var seen = new HashSet<string>();
        // Recompute from the CURRENT pattern rather than the startup cache: when an
        // HPU is equipped the pattern is a smaller rarity shape whose set of unique
        // rotations can differ from the base shape cached in the constructor.
        var rotations = GetUniqueRotations(upgrade);
        var pattern = upgrade.GetPattern();

        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        foreach (var rotation in rotations)
        {
            UpgradeEquipCell cell = null;
            try { cell = _equipSlots.GetCell(x, y); } catch { }
            if (cell is null) continue;

            var offset = GetOffsetsCached(upgrade, rotation, cell);
            // GetOffsetsCached may leave the shared rotated map on a different
            // rotation (cache hits skip the rebuild), so re-fetch it here.
            var modMap = pattern.GetModifiedMap(rotation);
            var cells = ComputeCells(modMap, offset.OffsetX, offset.OffsetY, W, H);
            if (cells == null) continue;

            var mask = new ulong[words];
            foreach (var idx in cells)
                mask[idx >> 6] |= 1UL << (idx & 63);

            if (Intersects(mask, baseOccupied)) continue; // overlaps an expander/HPU already down

            var key = MaskKey(mask);
            if (!seen.Add(key)) continue;

            result.Add(new Placement { Mask = mask, OffsetX = offset.OffsetX, OffsetY = offset.OffsetY, Rotation = (byte)rotation });
        }

        return result;
    }

    private static string MaskKey(ulong[] mask)
    {
        var sb = new System.Text.StringBuilder(mask.Length * 17);
        for (int i = 0; i < mask.Length; i++) { sb.Append(mask[i].ToString("x16")); sb.Append(','); }
        return sb.ToString();
    }

    private long _normalNodes;
    private System.Diagnostics.Stopwatch _normalTimer;

    private bool SolveNormals()
    {
        if (_equipSlots == null) return false;

        // Joint piece list: the folded HPU (if any) goes first so it is equipped
        // before the normals during apply — that activates the rarity-pattern
        // shrink the normals were enumerated with — and so its own footprint is
        // read from its base pattern (an HPU never shrinks itself).
        var pieces = new List<UpgradeInstance>();
        if (_hpu != null) pieces.Add(_hpu);
        pieces.AddRange(_normals);
        if (pieces.Count == 0) return true;

        int W = GridWidth(), H = GridHeight();
        int N = W * H;
        if (N <= 0) return false;
        int words = WordCount(N);

        // Cells already taken (expanders and anything pre-existing). The folded HPU
        // is NOT equipped here, so it does not count toward the base occupancy.
        var baseOccupied = new ulong[words];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            UpgradeEquipCell cell = null;
            try { cell = _equipSlots.GetCell(x, y); } catch { }
            if (cell != null && cell.Upgrade != null)
            {
                int idx = y * W + x;
                baseOccupied[idx >> 6] |= 1UL << (idx & 63);
            }
        }

        int pieceCount = pieces.Count;
        var placements = new List<Placement>[pieceCount];
        var sizes = new int[pieceCount];
        var signatures = new string[pieceCount];

        int startNormal = 0;
        if (_hpu != null)
        {
            // Enumerate the HPU's own footprint while it is NOT equipped (base shape).
            placements[0] = EnumeratePlacements(_hpu, W, H, words, baseOccupied);
            if (placements[0].Count == 0) return false;
            sizes[0] = PopCount(placements[0][0].Mask);
            signatures[0] = PieceSignature(placements[0]);
            startNormal = 1;
        }

        // For the normal pieces to be enumerated with their shrunk (rarity) shapes,
        // the HPU must be equipped. Park it in any legal spot; its position here does
        // not matter (it does not affect the masks we build) — only that it is on the
        // grid so GetPattern() returns the rarity shapes.
        UpgradeInstance parkedHpu = null;
        if (_hpu != null)
        {
            if (!ParkHpu(W, H, out parkedHpu))
                return false; // no room to even activate it -> unsolvable
        }

        try
        {
            for (int i = startNormal; i < pieceCount; i++)
            {
                placements[i] = EnumeratePlacements(pieces[i], W, H, words, baseOccupied);
                if (placements[i].Count == 0)
                    return false; // this piece cannot go anywhere -> unsolvable
                sizes[i] = PopCount(placements[i][0].Mask);
                signatures[i] = PieceSignature(placements[i]);
            }
        }
        finally
        {
            if (parkedHpu != null)
                _equipSlots.Unequip(_gear, parkedHpu);
        }

        // Hex adjacency lookup (matches HexMap.AddBidirectionalConnections), used
        // for connected-region pruning.
        var neighbors = BuildNeighbors(W, H, words);

        var chosen = new int[pieceCount];
        for (int i = 0; i < pieceCount; i++) chosen[i] = -1;

        var remaining = new List<int>(pieceCount);
        for (int i = 0; i < pieceCount; i++) remaining.Add(i);

        _normalNodes = 0;
        _normalTimer = System.Diagnostics.Stopwatch.StartNew();

        bool ok = NormalDfs(remaining, baseOccupied, placements, sizes, signatures, neighbors, N, words, chosen);
        if (!ok) return false;

        // Apply the packing to the live grid, HPU first (see above).
        var applied = new List<UpgradeInstance>();
        for (int i = 0; i < pieceCount; i++)
        {
            var p = placements[i][chosen[i]];
            if (!_equipSlots.EquipModule(_gear, pieces[i], p.OffsetX, p.OffsetY, p.Rotation, true))
            {
                GridSolverPlugin.Logger.LogWarning($"Bitmask solution failed to apply '{pieces[i].Upgrade?.Name}'; rolling back.");
                foreach (var done in applied)
                    _equipSlots.Unequip(_gear, done);
                return false;
            }
            applied.Add(pieces[i]);
        }
        return true;
    }

    /// <summary>Equips the folded HPU in the first legal spot so it activates the
    /// rarity-pattern shrink; returns false if it will not fit anywhere.</summary>
    private bool ParkHpu(int W, int H, out UpgradeInstance parked)
    {
        parked = null;
        var rotations = GetUniqueRotations(_hpu);
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            UpgradeEquipCell cell = null;
            try { cell = _equipSlots.GetCell(x, y); } catch { }
            if (cell is null || cell.Upgrade is not null) continue;

            foreach (var rotation in rotations)
            {
                var offset = GetOffsetsCached(_hpu, rotation, cell);
                if (_equipSlots.EquipModule(_gear, _hpu, offset.OffsetX, offset.OffsetY, (byte)rotation, true))
                {
                    parked = _hpu;
                    return true;
                }
            }
        }
        return false;
    }

    private static string PieceSignature(List<Placement> placements)
    {
        var keys = new List<string>(placements.Count);
        foreach (var p in placements) keys.Add(MaskKey(p.Mask));
        keys.Sort(System.StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static ulong[][] BuildNeighbors(int W, int H, int words)
    {
        int N = W * H;
        var nb = new ulong[N][];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var m = new ulong[words];
            (int dx, int dy)[] offs = (x % 2 == 0)
                ? new[] { (0, -1), (1, -1), (1, 0), (0, 1), (-1, 0), (-1, -1) }
                : new[] { (0, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };
            foreach (var (dx, dy) in offs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < W && ny >= 0 && ny < H)
                {
                    int idx = ny * W + nx;
                    m[idx >> 6] |= 1UL << (idx & 63);
                }
            }
            nb[y * W + x] = m;
        }
        return nb;
    }

    private bool NormalDfs(
        List<int> remaining, ulong[] occupied,
        List<Placement>[] placements, int[] sizes, string[] signatures,
        ulong[][] neighbors, int N, int words, int[] chosen)
    {
        if (++_normalNodes > NormalSolveNodeCap || _normalTimer.ElapsedMilliseconds > NormalSolveTimeCapMs)
            return false;

        if (remaining.Count == 0)
            return true;

        // Area pruning.
        int avail = N - PopCount(occupied);
        int req = 0, minSize = int.MaxValue;
        for (int k = 0; k < remaining.Count; k++)
        {
            int s = sizes[remaining[k]];
            req += s;
            if (s < minSize) minSize = s;
        }
        if (avail < req) return false;

        // Connected-region pruning: a piece is connected, so it can only be placed
        // inside a single empty region of at least its own size. Cells in regions
        // smaller than the smallest remaining piece can never be used, so if the
        // usable cells fall short of what the pieces need, bail. (Sound for slack
        // packing: small empty pockets are allowed to stay empty.)
        if (minSize > 1 && UsableCells(occupied, neighbors, N, words, minSize) < req)
            return false;

        bool exact = avail == req;

        if (exact)
        {
            // Must fill the board exactly: branch only on placements covering the
            // first empty cell, and skip identical pieces at this node.
            int anchor = FirstEmpty(occupied, N);
            if (anchor < 0) return false;
            ulong anchorWord = 1UL << (anchor & 63);
            int anchorIdx = anchor >> 6;

            var tried = new HashSet<string>();
            for (int k = 0; k < remaining.Count; k++)
            {
                int piece = remaining[k];
                if (!tried.Add(signatures[piece])) continue;

                var pls = placements[piece];
                for (int j = 0; j < pls.Count; j++)
                {
                    var mask = pls[j].Mask;
                    if ((mask[anchorIdx] & anchorWord) == 0) continue;
                    if (Intersects(mask, occupied)) continue;

                    Or(occupied, mask);
                    chosen[piece] = j;
                    remaining.RemoveAt(k);

                    if (NormalDfs(remaining, occupied, placements, sizes, signatures, neighbors, N, words, chosen))
                        return true;

                    remaining.Insert(k, piece);
                    chosen[piece] = -1;
                    AndNot(occupied, mask);

                    if (_normalNodes > NormalSolveNodeCap || _normalTimer.ElapsedMilliseconds > NormalSolveTimeCapMs)
                        return false;
                }
            }
            return false;
        }

        // MRV: expand the piece with the fewest currently-legal placements.
        int bestK = -1, bestCount = int.MaxValue;
        for (int k = 0; k < remaining.Count; k++)
        {
            int piece = remaining[k];
            int cnt = 0;
            var pls = placements[piece];
            for (int j = 0; j < pls.Count; j++)
                if (!Intersects(pls[j].Mask, occupied)) cnt++;

            if (cnt == 0) return false;
            if (cnt < bestCount) { bestCount = cnt; bestK = k; if (cnt == 1) break; }
        }

        int chosenPiece = remaining[bestK];
        remaining.RemoveAt(bestK);
        var placementsForPiece = placements[chosenPiece];
        for (int j = 0; j < placementsForPiece.Count; j++)
        {
            var mask = placementsForPiece[j].Mask;
            if (Intersects(mask, occupied)) continue;

            Or(occupied, mask);
            chosen[chosenPiece] = j;

            if (NormalDfs(remaining, occupied, placements, sizes, signatures, neighbors, N, words, chosen))
                return true;

            chosen[chosenPiece] = -1;
            AndNot(occupied, mask);

            if (_normalNodes > NormalSolveNodeCap || _normalTimer.ElapsedMilliseconds > NormalSolveTimeCapMs)
            {
                remaining.Insert(bestK, chosenPiece);
                return false;
            }
        }
        remaining.Insert(bestK, chosenPiece);
        return false;
    }

    private static void Or(ulong[] target, ulong[] mask)
    {
        for (int i = 0; i < target.Length; i++) target[i] |= mask[i];
    }

    private static void AndNot(ulong[] target, ulong[] mask)
    {
        for (int i = 0; i < target.Length; i++) target[i] &= ~mask[i];
    }

    private static int TrailingZeros(ulong v)
    {
        if (v == 0) return 64;
        int n = 0;
        while ((v & 1UL) == 0) { v >>= 1; n++; }
        return n;
    }

    private static int FirstEmpty(ulong[] occupied, int N)
    {
        for (int idx = 0; idx < N; idx++)
            if ((occupied[idx >> 6] & (1UL << (idx & 63))) == 0) return idx;
        return -1;
    }

    /// <summary>
    /// Total empty cells that live in a connected region large enough to hold at
    /// least the smallest remaining piece. Cells in tinier pockets are unusable.
    /// </summary>
    private static int UsableCells(ulong[] occupied, ulong[][] neighbors, int N, int words, int minSize)
    {
        var visited = new ulong[words];
        int usable = 0;
        for (int start = 0; start < N; start++)
        {
            int w = start >> 6; ulong bit = 1UL << (start & 63);
            if ((occupied[w] & bit) != 0) continue;   // filled
            if ((visited[w] & bit) != 0) continue;     // already counted

            // Flood-fill this empty region.
            int size = 0;
            var stack = new List<int> { start };
            visited[w] |= bit;
            while (stack.Count > 0)
            {
                int cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                size++;
                var nb = neighbors[cur];
                for (int wi = 0; wi < words; wi++)
                {
                    ulong bits = nb[wi] & ~occupied[wi] & ~visited[wi];
                    while (bits != 0)
                    {
                        ulong lsb = bits & (~bits + 1);
                        int b = TrailingZeros(lsb);
                        bits &= bits - 1;
                        int cell = (wi << 6) + b;
                        visited[wi] |= lsb;
                        stack.Add(cell);
                    }
                }
            }
            if (size >= minSize) usable += size;
        }
        return usable;
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