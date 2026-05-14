using Sirenix.OdinInspector;
using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "CustomAsset", menuName = "GridAsset")]
[Serializable]
public class GridAsset : ScriptableObject
{
    public const byte NoAreaId = 0;
    public const byte MinAreaId = 1;
    public const byte MaxRecommendedAreaId = 50;

    [Header("度量")] public byte ID;

    [ReadOnly] public float CellWidth = 1.0f;   // 每格边长
    [ReadOnly] public Vector2 OriginXZ;         // 世界左下角（对齐到CellWidth的整数倍）
    [ReadOnly] public int Width;                // X方向格数
    [ReadOnly] public int Height;               // Z方向格数

    // 先保留，等 Area/AreaContext 改造完成后再决定是否删除
    public Area parent;

    [FormerlySerializedAs("Buildable")]
    [Header("一维规则网格：index = x + width * z")]

    [ReadOnly, Tooltip("0-外部区域,1-道路,7-建筑占用")]
    [HideInInspector]
    public byte[] passableType;

    [HideInInspector]
    [ReadOnly, Tooltip("每个格子的世界高度Y；按 index = x + width * z 存储")]
    public float[] heightY;

    [HideInInspector]
    [ReadOnly, Tooltip("每个格子的归属 AreaId；0=无归属，1..50=AreaId")]
    public byte[] ownerAreaId;

#if UNITY_EDITOR
    [PropertySpace(8)]
    [BoxGroup("批量工具")]
    [InfoBox("确保数组长度与 Width*Height 匹配；如不匹配会自动重建。", InfoMessageType = InfoMessageType.None)]
    [Button("重建数组尺寸（Width*Height）", ButtonSizes.Medium)]
    [GUIColor(0.4f, 0.8f, 1f)]
    private void RebuildArrays()
    {
        EnsureArraySize(forceRecreate: true);
        EditorUtility.SetDirty(this);
    }

    [BoxGroup("批量工具")]
    [Button("用指定值填充【通行类型 & 高度】", ButtonSizes.Medium)]
    private void FillAll(
        [LabelText("通行(byte)")] byte passable = 1,
        [LabelText("高度Y(float)")] float y = 0f)
    {
        EnsureArraySize();

        for (int i = 0, n = passableType.Length; i < n; i++) passableType[i] = passable;
        for (int i = 0, n = heightY.Length; i < n; i++) heightY[i] = y;

        EditorUtility.SetDirty(this);
    }

    [BoxGroup("批量工具")]
    [Button("仅填充通行类型", ButtonSizes.Small)]
    public void FillPassable([LabelText("通行(byte)")] byte passable = 1)
    {
        EnsureArraySize();
        for (int i = 0, n = passableType.Length; i < n; i++) passableType[i] = passable;
        EditorUtility.SetDirty(this);
    }

    [BoxGroup("批量工具")]
    [Button("仅填充高度Y", ButtonSizes.Small)]
    private void FillHeight([LabelText("高度Y(float)")] float y = 0f)
    {
        EnsureArraySize();
        for (int i = 0, n = heightY.Length; i < n; i++) heightY[i] = y;
        EditorUtility.SetDirty(this);
    }

    [BoxGroup("批量工具")]
    [Button("仅填充 Area 归属", ButtonSizes.Small)]
    public void FillOwnerArea([LabelText("AreaId(byte)")] byte areaId = NoAreaId)
    {
        EnsureArraySize();

        if (!IsAreaIdAllowed(areaId))
        {
            Debug.LogWarning($"[GridAsset] 非法 AreaId={areaId}，允许值为 0 或 {MinAreaId}..{MaxRecommendedAreaId}");
            return;
        }

        for (int i = 0, n = ownerAreaId.Length; i < n; i++) ownerAreaId[i] = areaId;
        EditorUtility.SetDirty(this);
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToIndex(int x, int z)
    {
        return x + Width * z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool WorldToCell(Vector3 world, out int x, out int z)
    {
        x = (int)Mathf.Floor((world.x - OriginXZ.x) / CellWidth);
        z = (int)Mathf.Floor((world.z - OriginXZ.y) / CellWidth);
        return (uint)x < (uint)Width && (uint)z < (uint)Height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int WorldToIndex(Vector3 world)
    {
        int x, z;
        if (!WorldToCell(world, out x, out z)) return -1;
        return ToIndex(x, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 IndexToWorldCenter(int index)
    {
        int x = index % Width;
        int z = index / Width;
        float cx = OriginXZ.x + (x + 0.5f) * CellWidth;
        float cz = OriginXZ.y + (z + 0.5f) * CellWidth;
        float cy = (heightY != null && (uint)index < (uint)heightY.Length) ? heightY[index] : 0f;
        return new Vector3(cx, cy, cz);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsWorld(in Vector3 world)
    {
        int x, z;
        return WorldToCell(world, out x, out z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsWorldXZ(float worldX, float worldZ)
    {
        int x = (int)Mathf.Floor((worldX - OriginXZ.x) / CellWidth);
        int z = (int)Mathf.Floor((worldZ - OriginXZ.y) / CellWidth);
        return (uint)x < (uint)Width && (uint)z < (uint)Height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int TryWorldToIndex(in Vector3 world) => WorldToIndex(world);

    public Bounds GetWorldBoundsXZ()
    {
        Vector3 min = new Vector3(OriginXZ.x, 0f, OriginXZ.y);
        Vector3 size = new Vector3(Width * CellWidth, 0f, Height * CellWidth);
        Vector3 center = min + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        return new Bounds(center, new Vector3(size.x, 0.01f, size.z));
    }

    // 高度读写

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetHeight(int x, int z)
    {
        int idx = ToIndex(x, z);
        if (heightY == null || (uint)idx >= (uint)heightY.Length) return 0f;
        return heightY[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetHeight(int x, int z, float y)
    {
        int idx = ToIndex(x, z);
        if (heightY == null || (uint)idx >= (uint)heightY.Length) return;
        heightY[idx] = y;
    }

    // 通行读写

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetPassable(int x, int z)
    {
        int idx = ToIndex(x, z);
        if (passableType == null || (uint)idx >= (uint)passableType.Length) return 0;
        return passableType[idx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPassable(int x, int z, byte v)
    {
        int idx = ToIndex(x, z);
        if (passableType == null || (uint)idx >= (uint)passableType.Length) return;
        passableType[idx] = v;
    }

    // Area 归属读写

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetOwnerAreaIdByIndex(int index)
    {
        if (ownerAreaId == null || (uint)index >= (uint)ownerAreaId.Length) return NoAreaId;
        return ownerAreaId[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetOwnerAreaId(int x, int z)
    {
        int idx = ToIndex(x, z);
        return GetOwnerAreaIdByIndex(idx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetOwnerAreaId(Vector3 world)
    {
        int idx = WorldToIndex(world);
        return idx >= 0 ? GetOwnerAreaIdByIndex(idx) : NoAreaId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOwnedByArea(int index, byte areaId)
    {
        return GetOwnerAreaIdByIndex(index) == areaId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOwnerAreaIdByIndex(int index, byte areaId)
    {
        if (!IsAreaIdAllowed(areaId)) return;
        if (ownerAreaId == null || (uint)index >= (uint)ownerAreaId.Length) return;
        ownerAreaId[index] = areaId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOwnerAreaId(int x, int z, byte areaId)
    {
        int idx = ToIndex(x, z);
        SetOwnerAreaIdByIndex(idx, areaId);
    }

    private static bool IsAreaIdAllowed(byte areaId)
    {
        return areaId == NoAreaId || (areaId >= MinAreaId && areaId <= MaxRecommendedAreaId);
    }

    private void OnValidate()
    {
        EnsureArraySize();
    }

    private void EnsureArraySize(bool forceRecreate = false)
    {
        int n = Mathf.Max(Width * Height, 0);

        if (forceRecreate || passableType == null || passableType.Length != n)
            passableType = (n > 0) ? new byte[n] : Array.Empty<byte>();

        if (forceRecreate || heightY == null || heightY.Length != n)
            heightY = (n > 0) ? new float[n] : Array.Empty<float>();

        if (forceRecreate || ownerAreaId == null || ownerAreaId.Length != n)
            ownerAreaId = (n > 0) ? new byte[n] : Array.Empty<byte>();
    }

    public void FillPassableRuntime(byte passable)
    {
        EnsureArraySize();
        for (int i = 0, n = passableType.Length; i < n; i++)
            passableType[i] = passable;
    }

    public bool IsPassableByIndex(int cellIndex)
    {
        return passableType[cellIndex] != 7;
    }
}