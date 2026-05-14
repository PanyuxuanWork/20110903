using System;
using UnityEngine;

[Serializable]
public struct TerrainViewStyle
{
    public Color fillColor;
    [Range(0f, 1f)] public float fillOpacity;

    public Color borderColor;
    [Range(0f, 1f)] public float borderOpacity;

    public static TerrainViewStyle Default(Color fill, Color border)
    {
        return new TerrainViewStyle
        {
            fillColor = fill,
            fillOpacity = 0.65f,
            borderColor = border,
            borderOpacity = 0.9f
        };
    }

    public Color32 ToColor32(TerrainViewStylePart part)
    {
        Color c = part == TerrainViewStylePart.Border ? borderColor : fillColor;
        float a = part == TerrainViewStylePart.Border ? borderOpacity : fillOpacity;
        c.a = Mathf.Clamp01(a);
        return (Color32)c;
    }
}
