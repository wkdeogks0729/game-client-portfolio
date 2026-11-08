using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아이템의 실제 칸 점유 상태를 관리한다.
/// 각 점유 칸은 아이템과 원점 좌표를 함께 보관하며, 배치 정보와 칸 배열은 항상 같은 상태를 유지해야 한다.
/// </summary>
public sealed class ItemGridCore
{
    public int Width { get; }
    public int Height { get; }
    public int UsableCellCount { get; }

    private readonly ItemInstance[,] occupant;
    private readonly Vector2Int[,] originOf;

    public struct Placement
    {
        public Vector2Int origin;
        public int rot;

        public Placement(Vector2Int origin, int rot)
        {
            this.origin = origin;
            this.rot = rot;
        }
    }

    private readonly Dictionary<ItemInstance, Placement> placements = new();

    public ItemGridCore(int width, int height, int usableCellCount = -1)
    {
        Width = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        int maximumCellCount = Width * Height;
        UsableCellCount = usableCellCount < 0
            ? maximumCellCount
            : Mathf.Clamp(usableCellCount, 1, maximumCellCount);

        occupant = new ItemInstance[Width, Height];
        originOf = new Vector2Int[Width, Height];
    }

    public bool IsCellAvailable(int x, int y)
    {
        return InRange(x, y) && y * Width + x < UsableCellCount;
    }

    /// <summary>
    /// 범위 안의 칸을 점유한 아이템을 반환한다.
    /// </summary>
    public ItemInstance GetOccupant(int x, int y)
    {
        if (!IsCellAvailable(x, y))
            return null;

        return occupant[x, y];
    }

    /// <summary>
    /// 아이템의 원점과 회전값을 조회한다.
    /// </summary>
    public bool TryGetPlacement(ItemInstance item, out Placement placement)
    {
        return placements.TryGetValue(item, out placement);
    }

    /// <summary>
    /// 지정한 칸의 아이템과 그 아이템의 원점을 함께 조회한다.
    /// </summary>
    public bool TryGetItemAtCell(int x, int y, out ItemInstance item, out Vector2Int itemOrigin)
    {
        item = null;
        itemOrigin = default;

        if (!IsCellAvailable(x, y))
            return false;

        item = occupant[x, y];
        if (item == null)
            return false;

        itemOrigin = originOf[x, y];
        return true;
    }

    /// <summary>
    /// 회전된 모든 점유 칸이 범위 안이며 비어 있는지 검사한다.
    /// </summary>
    /// <param name="ignore">같은 그리드 안에서 이동할 때 기존 자기 점유 칸을 충돌 검사에서 제외할 아이템</param>
    public bool CanPlace(ItemInstance item, Vector2Int origin, int rot, ItemInstance ignore = null)
        => CanPlaceIgnoring(item, origin, rot, ignore, null);

    private bool CanPlaceIgnoring(ItemInstance item, Vector2Int origin, int rot, ItemInstance ignore, ItemInstance otherIgnore)
    {
        if (!HasValidShape(item))
            return false;

        var cells = ItemUtility.GetRotatedCells(item.Data, rot);
        if (cells == null || cells.Count == 0)
            return false;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int c = origin + cells[i];

            if (!IsCellAvailable(c.x, c.y))
                return false;

            ItemInstance occ = occupant[c.x, c.y];
            if (occ != null && occ != ignore && occ != otherIgnore)
                return false;
        }

        return true;
    }

    internal static bool PlacementsOverlap(ItemInstance first, Vector2Int firstOrigin, int firstRot,
        ItemInstance second, Vector2Int secondOrigin, int secondRot)
    {
        var firstCells = ItemUtility.GetRotatedCells(first.Data, firstRot);
        var secondCells = ItemUtility.GetRotatedCells(second.Data, secondRot);
        foreach (var a in firstCells)
            foreach (var b in secondCells)
                if (firstOrigin + a == secondOrigin + b) return true;
        return false;
    }

    internal bool CanSwap(ItemInstance first, ItemInstance second, Vector2Int target, int rotation)
    {
        return first?.Data != null && second?.Data != null && first != second && placements.TryGetValue(first, out var a) && placements.TryGetValue(second, out var b) &&
            CanPlaceIgnoring(first, target, rotation, first, second) &&
            CanPlaceIgnoring(second, a.origin, b.rot, first, second) &&
            !PlacementsOverlap(first, target, rotation, second, a.origin, b.rot);
    }

    internal bool TrySwap(ItemInstance first, ItemInstance second, Vector2Int target, int rotation)
    {
        if (!CanSwap(first, second, target, rotation)) return false;
        var a = placements[first]; var b = placements[second];
        Remove(first); Remove(second);
        InternalPlace(first, target, rotation);
        InternalPlace(second, a.origin, b.rot);
        return true;
    }

    /// <summary>
    /// 검증을 통과한 아이템을 배치하고 모든 점유 칸과 배치 정보를 갱신한다.
    /// </summary>
    public bool Place(ItemInstance item, Vector2Int origin, int rot)
    {
        if (!HasValidShape(item))
            return false;

        bool alreadyPlaced = placements.TryGetValue(item, out Placement oldPlacement);
        ItemInstance ignore = alreadyPlaced ? item : null;

        if (!CanPlace(item, origin, rot, ignore))
            return false;

        if (alreadyPlaced)
            ClearPlacementCells(item, oldPlacement);

        InternalPlace(item, origin, rot);
        return true;
    }

    private static bool HasValidShape(ItemInstance item)
    {
        return item?.Data != null &&
               item.Data.shape != null &&
               item.Data.shape.Count > 0;
    }

    /// <summary>
    /// 아이템의 좌표를 찾고 그 위치에 있는 아이템을 제거
    /// </summary>
    public bool Remove(ItemInstance item)
    {
        if (item == null)
            return false;

        if (!placements.TryGetValue(item, out Placement placement))
            return false;

        ClearPlacementCells(item, placement);
        placements.Remove(item);
        return true;
    }

    public void ClearAll()
    {
        placements.Clear();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                occupant[x, y] = null;
                originOf[x, y] = default;
            }
        }
    }

    /// <summary>
    /// 갖고있는 아이템과 배치정보
    /// </summary>
    public IEnumerable<KeyValuePair<ItemInstance, Placement>> EnumeratePlacements()
    {
        return placements;
    }

    /// <summary>
    /// 셀의 정보 정리
    /// </summary>
    private void ClearPlacementCells(ItemInstance item, Placement placement)
    {
        var cells = ItemUtility.GetRotatedCells(item.Data, placement.rot);

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int c = placement.origin + cells[i];

            if (InRange(c.x, c.y) && occupant[c.x, c.y] == item)
            {
                occupant[c.x, c.y] = null;
                originOf[c.x, c.y] = default;
            }
        }
    }

    private void InternalPlace(ItemInstance item, Vector2Int origin, int rot)
    {
        var cells = ItemUtility.GetRotatedCells(item.Data, rot);

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int c = origin + cells[i];
            occupant[c.x, c.y] = item;
            originOf[c.x, c.y] = origin;
        }

        placements[item] = new Placement(origin, rot);
    }

    private bool InRange(int x, int y)
    {
        return 0 <= x && x < Width && 0 <= y && y < Height;
    }
}
