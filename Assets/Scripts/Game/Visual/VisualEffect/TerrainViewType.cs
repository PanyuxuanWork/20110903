/// <summary>
/// Semantic display type. Normal gameplay systems should use this instead of raw colors.
/// Raw colors are reserved for Override/debug APIs.
/// </summary>
public enum TerrainViewType : byte
{
    None = 0,

    PlayerTerritory = 10,
    EnemyTerritory = 11,
    NeutralTerritory = 12,

    Buildable = 20,
    Unbuildable = 21,
    Blocked = 22,

    PreviewValid = 30,
    PreviewInvalid = 31,
    PreviewRoad = 32,

    Selected = 40,
    Hovered = 50,

    DebugPath = 90,
    DebugBlocked = 91,
    DebugArea = 92
}
