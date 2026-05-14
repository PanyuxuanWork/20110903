using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds fill/border cell lists for grid-shaped areas.
/// Border rule:
/// A cell is a border cell if any of its 4-neighbors is outside the area.
/// Border occupies one whole cell.
/// </summary>
public static class TerrainAreaCellBuilder
{
    public static void BuildFromOwnerArea(
        GridAsset grid,
        byte areaId,
        List<int> fillCells,
        List<int> borderCells)
    {
        fillCells?.Clear();
        borderCells?.Clear();

        if (grid == null)
        {
            Debug.LogError("[TerrainAreaCellBuilder] GridAsset is null.");
            return;
        }

        if (areaId == GridAsset.NoAreaId)
            return;

        EnsureLists(fillCells, borderCells);

        int width = grid.Width;
        int height = grid.Height;
        int cellCount = width * height;

        for (int index = 0; index < cellCount; index++)
        {
            if (grid.GetOwnerAreaIdByIndex(index) != areaId)
                continue;

            int x = index % width;
            int z = index / width;

            bool isBorder =
                !IsSameArea(grid, x - 1, z, areaId) ||
                !IsSameArea(grid, x + 1, z, areaId) ||
                !IsSameArea(grid, x, z - 1, areaId) ||
                !IsSameArea(grid, x, z + 1, areaId);

            if (isBorder)
                borderCells.Add(index);
            else
                fillCells.Add(index);
        }
    }

    public static void BuildFromCellSet(
        GridAsset grid,
        IEnumerable<int> areaCells,
        List<int> fillCells,
        List<int> borderCells)
    {
        fillCells?.Clear();
        borderCells?.Clear();

        if (grid == null)
        {
            Debug.LogError("[TerrainAreaCellBuilder] GridAsset is null.");
            return;
        }

        EnsureLists(fillCells, borderCells);

        if (areaCells == null)
            return;

        int width = grid.Width;
        int height = grid.Height;
        int cellCount = width * height;

        HashSet<int> set = new HashSet<int>();
        foreach (int index in areaCells)
        {
            if ((uint)index < (uint)cellCount)
                set.Add(index);
        }

        foreach (int index in set)
        {
            int x = index % width;
            int z = index / width;

            bool isBorder =
                !Contains(set, x - 1, z, width, height) ||
                !Contains(set, x + 1, z, width, height) ||
                !Contains(set, x, z - 1, width, height) ||
                !Contains(set, x, z + 1, width, height);

            if (isBorder)
                borderCells.Add(index);
            else
                fillCells.Add(index);
        }
    }

    private static bool IsSameArea(GridAsset grid, int x, int z, byte areaId)
    {
        if ((uint)x >= (uint)grid.Width || (uint)z >= (uint)grid.Height)
            return false;

        return grid.GetOwnerAreaId(x, z) == areaId;
    }

    private static bool Contains(HashSet<int> set, int x, int z, int width, int height)
    {
        if ((uint)x >= (uint)width || (uint)z >= (uint)height)
            return false;

        return set.Contains(x + width * z);
    }

    private static void EnsureLists(List<int> fillCells, List<int> borderCells)
    {
        if (fillCells == null || borderCells == null)
            throw new System.ArgumentNullException("fillCells/borderCells cannot be null.");
    }
}
