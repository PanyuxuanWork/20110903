/// <summary>
/// Inclusive cell index range used by TerrainViewState to describe changed cells.
/// First version stores ranges only for future optimization and debug visibility.
/// Texture upload can still be full-frame until Backend supports partial upload.
/// </summary>
public readonly struct TerrainCellDirtyRange
{
    public readonly int startIndex;
    public readonly int endIndex;

    public TerrainCellDirtyRange(int startIndex, int endIndex)
    {
        this.startIndex = startIndex;
        this.endIndex = endIndex;
    }

    public bool IsValid => startIndex >= 0 && endIndex >= startIndex;
    public int Count => IsValid ? endIndex - startIndex + 1 : 0;
}
