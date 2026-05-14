using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves sparse layer requests into one final RGBA32 color per cell.
/// No color blending: higher priority wins.
/// 
/// First optimized version:
/// - ResolveAll() rebuilds the whole color buffer.
/// - ResolveCells() rebuilds only the supplied dirty cells by scanning visible layers for those cells.
/// </summary>
public sealed class TerrainViewResolver
{
    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    private readonly GridAsset grid;
    private readonly TerrainViewStyleConfig styleConfig;

    private Color32[] resolvedColors;
    private int[] resolvedPriorityKeys;
    private long[] resolvedSequences;

    public TerrainViewResolver(GridAsset grid, TerrainViewStyleConfig styleConfig)
    {
        this.grid = grid;
        this.styleConfig = styleConfig;
    }

    public Color32[] Resolve(TerrainViewState state)
    {
        return ResolveAll(state);
    }

    public Color32[] ResolveAll(TerrainViewState state)
    {
        if (!TryPrepare(out int cellCount))
            return null;

        EnsureBuffers(cellCount);
        ClearBuffers();

        if (state == null)
            return resolvedColors;

        foreach (var layerPair in state.EnumerateVisibleLayers())
        {
            foreach (var requestPair in layerPair.Value)
                TryApplyRequest(requestPair.Key, requestPair.Value);
        }

        return resolvedColors;
    }

    public Color32[] ResolveCells(TerrainViewState state, IReadOnlyCollection<int> cellIndices)
    {
        if (!TryPrepare(out int cellCount))
            return null;

        EnsureBuffers(cellCount);

        if (state == null || cellIndices == null || cellIndices.Count == 0)
            return resolvedColors;

        foreach (int cellIndex in cellIndices)
        {
            if ((uint)cellIndex >= (uint)cellCount)
                continue;

            resolvedColors[cellIndex] = Clear;
            resolvedPriorityKeys[cellIndex] = -1;
            resolvedSequences[cellIndex] = -1;

            foreach (var layerPair in state.EnumerateVisibleLayers())
            {
                if (layerPair.Value.TryGetValue(cellIndex, out TerrainCellViewRequest request))
                    TryApplyRequest(cellIndex, request);
            }
        }

        return resolvedColors;
    }

    public Color32 GetResolvedColor(int cellIndex)
    {
        if (resolvedColors == null || (uint)cellIndex >= (uint)resolvedColors.Length)
            return Clear;

        return resolvedColors[cellIndex];
    }

    private bool TryPrepare(out int cellCount)
    {
        cellCount = 0;

        if (grid == null)
        {
            Debug.LogError("[TerrainViewResolver] GridAsset is null.");
            return false;
        }

        cellCount = grid.Width * grid.Height;
        return cellCount > 0;
    }

    private void TryApplyRequest(int cellIndex, TerrainCellViewRequest request)
    {
        if ((uint)cellIndex >= (uint)resolvedColors.Length)
            return;

        Color32 color = ResolveRequestColor(request);
        if (color.a == 0)
            return;

        int priorityKey = ComposePriorityKey(request.layer, request.requestPriority);

        if (priorityKey > resolvedPriorityKeys[cellIndex] ||
            (priorityKey == resolvedPriorityKeys[cellIndex] && request.sequence >= resolvedSequences[cellIndex]))
        {
            resolvedPriorityKeys[cellIndex] = priorityKey;
            resolvedSequences[cellIndex] = request.sequence;
            resolvedColors[cellIndex] = color;
        }
    }

    private Color32 ResolveRequestColor(TerrainCellViewRequest request)
    {
        if (request.useDirectColor)
            return request.directColor;

        if (styleConfig == null)
            return new Color32(255, 0, 255, 220);

        return styleConfig.ResolveColor(request.viewType, request.stylePart);
    }

    private static int ComposePriorityKey(TerrainViewLayer layer, byte requestPriority)
    {
        return ((int)layer << 8) | requestPriority;
    }

    private void EnsureBuffers(int cellCount)
    {
        if (resolvedColors != null && resolvedColors.Length == cellCount)
            return;

        resolvedColors = new Color32[cellCount];
        resolvedPriorityKeys = new int[cellCount];
        resolvedSequences = new long[cellCount];
        ClearBuffers();
    }

    private void ClearBuffers()
    {
        for (int i = 0; i < resolvedColors.Length; i++)
        {
            resolvedColors[i] = Clear;
            resolvedPriorityKeys[i] = -1;
            resolvedSequences[i] = -1;
        }
    }
}
