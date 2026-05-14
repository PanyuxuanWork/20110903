using UnityEngine;

/// <summary>
/// Result of converting a screen/ray hit into a GridAsset cell.
/// </summary>
public readonly struct TerrainCellHit
{
    public readonly bool isValid;
    public readonly int cellIndex;
    public readonly int x;
    public readonly int z;
    public readonly Vector3 worldPosition;
    public readonly RaycastHit raycastHit;

    public TerrainCellHit(bool isValid, int cellIndex, int x, int z, Vector3 worldPosition, RaycastHit raycastHit)
    {
        this.isValid = isValid;
        this.cellIndex = cellIndex;
        this.x = x;
        this.z = z;
        this.worldPosition = worldPosition;
        this.raycastHit = raycastHit;
    }

    public static TerrainCellHit Invalid => new TerrainCellHit(false, -1, -1, -1, default, default);
}
