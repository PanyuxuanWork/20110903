using UnityEngine;

public struct TerrainCellViewRequest
{
    public TerrainViewLayer layer;
    public TerrainViewType viewType;
    public TerrainViewStylePart stylePart;
    public byte requestPriority;
    public long sequence;

    public bool useDirectColor;
    public Color32 directColor;

    public static TerrainCellViewRequest FromType(
        TerrainViewLayer layer,
        TerrainViewType viewType,
        TerrainViewStylePart stylePart,
        byte requestPriority,
        long sequence)
    {
        return new TerrainCellViewRequest
        {
            layer = layer,
            viewType = viewType,
            stylePart = stylePart,
            requestPriority = requestPriority,
            sequence = sequence,
            useDirectColor = false,
            directColor = default
        };
    }

    public static TerrainCellViewRequest FromDirectColor(
        TerrainViewLayer layer,
        Color32 color,
        byte requestPriority,
        long sequence)
    {
        return new TerrainCellViewRequest
        {
            layer = layer,
            viewType = TerrainViewType.None,
            stylePart = TerrainViewStylePart.Fill,
            requestPriority = requestPriority,
            sequence = sequence,
            useDirectColor = true,
            directColor = color
        };
    }
}
