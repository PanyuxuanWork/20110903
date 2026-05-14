/// <summary>
/// A style can provide different colors for region fill cells and border cells.
/// The first version writes the chosen color directly into the final RGBA32 mask.
/// </summary>
public enum TerrainViewStylePart : byte
{
    Fill = 0,
    Border = 1
}
