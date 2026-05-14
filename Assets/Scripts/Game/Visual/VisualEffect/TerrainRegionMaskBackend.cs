using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Low-level texture backend for Terrain Region display.
/// One cell = one RGBA32 pixel.
/// RGB = final resolved color, A = visibility / strength.
/// </summary>
public sealed class TerrainRegionMaskBackend
{
    private static readonly int RegionColorMaskId = Shader.PropertyToID("_RegionColorMask");
    private static readonly int RegionEnabledId = Shader.PropertyToID("_RegionEnabled");
    private static readonly int RegionWorldMinId = Shader.PropertyToID("_RegionWorldMin");
    private static readonly int RegionWorldSizeId = Shader.PropertyToID("_RegionWorldSize");
    private static readonly int RegionOverlayOpacityId = Shader.PropertyToID("_RegionOverlayOpacity");
    private static readonly int RegionHardOverrideId = Shader.PropertyToID("_RegionHardOverride");

    private readonly GridAsset grid;
    private readonly Material targetMaterial;

    private Texture2D texture;
    private Color32[] buffer;

    private int width;
    private int height;
    private float overlayOpacity = 1f;
    private bool initialized;

    public bool IsInitialized => initialized;
    public Texture2D Texture => texture;
    public int Width => width;
    public int Height => height;
    public int PixelCount => buffer?.Length ?? 0;

    public TerrainRegionMaskBackend(GridAsset grid, Material targetMaterial)
    {
        this.grid = grid;
        this.targetMaterial = targetMaterial;
    }

    public void Initialize()
    {
        if (initialized)
            return;

        if (grid == null)
        {
            Debug.LogError("[TerrainRegionMaskBackend] GridAsset is null.");
            return;
        }

        if (targetMaterial == null)
        {
            Debug.LogError("[TerrainRegionMaskBackend] Target material is null.");
            return;
        }

        width = grid.Width;
        height = grid.Height;

        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[TerrainRegionMaskBackend] Invalid mask size: {width}x{height}.");
            return;
        }

        buffer = new Color32[width * height];

        texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true)
        {
            name = "Runtime_RegionColorMask",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixels32(buffer);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        BindMaterialParameters();
        initialized = true;
    }

    public void Dispose()
    {
        if (texture != null)
        {
            Object.Destroy(texture);
            texture = null;
        }

        buffer = null;
        initialized = false;
    }

    public void BindMaterialParameters()
    {
        if (targetMaterial == null || grid == null)
            return;

        targetMaterial.SetTexture(RegionColorMaskId, texture);
        targetMaterial.SetFloat(RegionEnabledId, 1f);
        targetMaterial.SetFloat(RegionOverlayOpacityId, overlayOpacity);
        targetMaterial.SetFloat(RegionHardOverrideId, 1f);
        targetMaterial.SetVector(RegionWorldMinId, new Vector4(grid.OriginXZ.x, grid.OriginXZ.y, 0f, 0f));
        targetMaterial.SetVector(RegionWorldSizeId, new Vector4(grid.Width * grid.CellWidth, grid.Height * grid.CellWidth, 0f, 0f));
    }

    public void SetVisible(bool visible)
    {
        if (targetMaterial != null)
            targetMaterial.SetFloat(RegionEnabledId, visible ? 1f : 0f);
    }

    public void SetOverlayOpacity(float opacity)
    {
        overlayOpacity = Mathf.Clamp01(opacity);

        if (targetMaterial != null)
            targetMaterial.SetFloat(RegionOverlayOpacityId, overlayOpacity);
    }

    public void SetCell(int cellIndex, Color32 color, bool apply = true)
    {
        if (!EnsureReady())
            return;

        if ((uint)cellIndex >= (uint)buffer.Length)
            return;

        buffer[cellIndex] = color;

        int x = cellIndex % width;
        int y = cellIndex / width;
        texture.SetPixel(x, y, color);

        if (apply)
            Apply();
    }

    public void UploadFull(Color32[] colors, bool apply = true)
    {
        if (!EnsureReady())
            return;

        if (colors == null || colors.Length != buffer.Length)
        {
            Debug.LogError("[TerrainRegionMaskBackend] UploadFull colors is null or size mismatch.");
            return;
        }

        System.Array.Copy(colors, buffer, buffer.Length);
        texture.SetPixels32(buffer);

        if (apply)
            Apply();
    }

    /// <summary>
    /// Conservative practical partial upload:
    /// - <=64 dirty cells: SetPixel per cell + one Apply
    /// - medium dirty set: one bounding rect upload
    /// - huge rect: fallback to full upload
    /// </summary>
    public void UploadCells(Color32[] resolvedColors, IReadOnlyCollection<int> dirtyCells, bool apply = true)
    {
        if (!EnsureReady())
            return;

        if (resolvedColors == null || resolvedColors.Length != buffer.Length)
        {
            Debug.LogError("[TerrainRegionMaskBackend] UploadCells resolvedColors is null or size mismatch.");
            return;
        }

        if (dirtyCells == null || dirtyCells.Count == 0)
            return;

        if (dirtyCells.Count <= 64)
        {
            foreach (int cellIndex in dirtyCells)
            {
                if ((uint)cellIndex >= (uint)buffer.Length)
                    continue;

                Color32 color = resolvedColors[cellIndex];
                buffer[cellIndex] = color;

                int x = cellIndex % width;
                int y = cellIndex / width;
                texture.SetPixel(x, y, color);
            }

            if (apply)
                Apply();

            return;
        }

        UploadDirtyBoundingRect(resolvedColors, dirtyCells, apply);
    }

    public void UploadDirtyBoundingRect(Color32[] resolvedColors, IReadOnlyCollection<int> dirtyCells, bool apply = true)
    {
        if (!EnsureReady())
            return;

        if (resolvedColors == null || resolvedColors.Length != buffer.Length)
        {
            Debug.LogError("[TerrainRegionMaskBackend] UploadDirtyBoundingRect resolvedColors is null or size mismatch.");
            return;
        }

        if (dirtyCells == null || dirtyCells.Count == 0)
            return;

        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        foreach (int cellIndex in dirtyCells)
        {
            if ((uint)cellIndex >= (uint)buffer.Length)
                continue;

            int x = cellIndex % width;
            int y = cellIndex / width;

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        if (maxX < minX || maxY < minY)
            return;

        int rectWidth = maxX - minX + 1;
        int rectHeight = maxY - minY + 1;
        int rectPixelCount = rectWidth * rectHeight;

        if (rectPixelCount > buffer.Length / 4)
        {
            UploadFull(resolvedColors, apply);
            return;
        }

        Color32[] rectPixels = new Color32[rectPixelCount];

        int write = 0;
        for (int y = minY; y <= maxY; y++)
        {
            int rowStart = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                int index = rowStart + x;
                Color32 color = resolvedColors[index];
                buffer[index] = color;
                rectPixels[write++] = color;
            }
        }

        texture.SetPixels32(minX, minY, rectWidth, rectHeight, rectPixels);

        if (apply)
            Apply();
    }

    public void Clear(bool apply = true)
    {
        if (!EnsureReady())
            return;

        System.Array.Clear(buffer, 0, buffer.Length);
        texture.SetPixels32(buffer);

        if (apply)
            Apply();
    }

    public void Apply()
    {
        if (texture != null)
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private bool EnsureReady()
    {
        if (!initialized)
            Initialize();

        return initialized && texture != null && buffer != null;
    }
}
