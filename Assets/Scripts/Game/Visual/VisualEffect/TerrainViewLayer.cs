/// <summary>
/// Higher numeric value means higher visual priority.
/// Do not use color blending: the highest-priority request wins.
/// </summary>
public enum TerrainViewLayer : byte
{
    Territory = 10,
    Buildability = 20,
    Preview = 30,
    Selection = 40,
    Hover = 50,
    Debug = 90,
    Override = 100
}
