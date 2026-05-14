using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainViewState
{
    private sealed class LayerState
    {
        public bool visible = true;
        public readonly Dictionary<int, TerrainCellViewRequest> requests = new Dictionary<int, TerrainCellViewRequest>();
    }

    private readonly Dictionary<TerrainViewLayer, LayerState> layers = new Dictionary<TerrainViewLayer, LayerState>();
    private readonly HashSet<int> dirtyCells = new HashSet<int>();

    private long sequence;
    private bool dirtyAll = true;

    public event Action Changed;

    public long Version { get; private set; }
    public bool DirtyAll => dirtyAll;
    public int DirtyCellCount => dirtyCells.Count;
    public IReadOnlyCollection<int> DirtyCells => dirtyCells;

    public void SetCell(
        TerrainViewLayer layer,
        int cellIndex,
        TerrainViewType viewType,
        TerrainViewStylePart stylePart = TerrainViewStylePart.Fill,
        byte requestPriority = 0)
    {
        if (cellIndex < 0)
            return;

        var state = GetOrCreateLayer(layer);
        state.requests[cellIndex] = TerrainCellViewRequest.FromType(
            layer, viewType, stylePart, requestPriority, NextSequence());

        MarkCellDirty(cellIndex);
    }

    public void SetCells(
        TerrainViewLayer layer,
        IEnumerable<int> cellIndices,
        TerrainViewType viewType,
        TerrainViewStylePart stylePart = TerrainViewStylePart.Fill,
        byte requestPriority = 0)
    {
        if (cellIndices == null)
            return;

        var state = GetOrCreateLayer(layer);
        long seq = NextSequence();
        bool changed = false;

        foreach (int cellIndex in cellIndices)
        {
            if (cellIndex < 0)
                continue;

            state.requests[cellIndex] = TerrainCellViewRequest.FromType(
                layer, viewType, stylePart, requestPriority, seq);

            dirtyCells.Add(cellIndex);
            changed = true;
        }

        if (changed)
            MarkChangedOnly();
    }

    public void SetOverrideCell(int cellIndex, Color32 color, byte requestPriority = byte.MaxValue)
    {
        if (cellIndex < 0)
            return;

        var state = GetOrCreateLayer(TerrainViewLayer.Override);
        state.requests[cellIndex] = TerrainCellViewRequest.FromDirectColor(
            TerrainViewLayer.Override, color, requestPriority, NextSequence());

        MarkCellDirty(cellIndex);
    }

    public void SetOverrideCells(IEnumerable<int> cellIndices, Color32 color, byte requestPriority = byte.MaxValue)
    {
        if (cellIndices == null)
            return;

        var state = GetOrCreateLayer(TerrainViewLayer.Override);
        long seq = NextSequence();
        bool changed = false;

        foreach (int cellIndex in cellIndices)
        {
            if (cellIndex < 0)
                continue;

            state.requests[cellIndex] = TerrainCellViewRequest.FromDirectColor(
                TerrainViewLayer.Override, color, requestPriority, seq);

            dirtyCells.Add(cellIndex);
            changed = true;
        }

        if (changed)
            MarkChangedOnly();
    }

    public void ClearCell(TerrainViewLayer layer, int cellIndex)
    {
        if (cellIndex < 0)
            return;

        if (layers.TryGetValue(layer, out LayerState state) && state.requests.Remove(cellIndex))
            MarkCellDirty(cellIndex);
    }

    public void ClearCells(TerrainViewLayer layer, IEnumerable<int> cellIndices)
    {
        if (cellIndices == null)
            return;

        if (!layers.TryGetValue(layer, out LayerState state))
            return;

        bool changed = false;
        foreach (int cellIndex in cellIndices)
        {
            if (cellIndex < 0)
                continue;

            if (state.requests.Remove(cellIndex))
            {
                dirtyCells.Add(cellIndex);
                changed = true;
            }
        }

        if (changed)
            MarkChangedOnly();
    }

    public void ClearLayer(TerrainViewLayer layer)
    {
        if (!layers.TryGetValue(layer, out LayerState state))
            return;

        if (state.requests.Count == 0)
            return;

        foreach (int cellIndex in state.requests.Keys)
            dirtyCells.Add(cellIndex);

        state.requests.Clear();
        MarkChangedOnly();
    }

    public void ClearAll()
    {
        bool changed = false;

        foreach (var pair in layers)
        {
            if (pair.Value.requests.Count == 0)
                continue;

            pair.Value.requests.Clear();
            changed = true;
        }

        if (changed)
            MarkAllDirty();
    }

    public void SetLayerVisible(TerrainViewLayer layer, bool visible)
    {
        var state = GetOrCreateLayer(layer);
        if (state.visible == visible)
            return;

        state.visible = visible;
        MarkAllDirty();
    }

    public bool IsLayerVisible(TerrainViewLayer layer)
    {
        return !layers.TryGetValue(layer, out LayerState state) || state.visible;
    }

    public IEnumerable<KeyValuePair<TerrainViewLayer, IReadOnlyDictionary<int, TerrainCellViewRequest>>> EnumerateVisibleLayers()
    {
        foreach (var pair in layers)
        {
            if (!pair.Value.visible)
                continue;

            yield return new KeyValuePair<TerrainViewLayer, IReadOnlyDictionary<int, TerrainCellViewRequest>>(
                pair.Key, pair.Value.requests);
        }
    }

    public int GetLayerRequestCount(TerrainViewLayer layer)
    {
        return layers.TryGetValue(layer, out LayerState state) ? state.requests.Count : 0;
    }

    public int GetTotalRequestCount()
    {
        int count = 0;
        foreach (var pair in layers)
            count += pair.Value.requests.Count;
        return count;
    }

    public void ConsumeDirty()
    {
        dirtyAll = false;
        dirtyCells.Clear();
    }

    public void ForceDirtyAll()
    {
        MarkAllDirty();
    }

    private LayerState GetOrCreateLayer(TerrainViewLayer layer)
    {
        if (!layers.TryGetValue(layer, out LayerState state))
        {
            state = new LayerState();
            layers.Add(layer, state);
        }

        return state;
    }

    private long NextSequence()
    {
        sequence++;
        return sequence;
    }

    private void MarkCellDirty(int cellIndex)
    {
        dirtyCells.Add(cellIndex);
        MarkChangedOnly();
    }

    private void MarkAllDirty()
    {
        dirtyAll = true;
        dirtyCells.Clear();
        MarkChangedOnly();
    }

    private void MarkChangedOnly()
    {
        Version++;
        Changed?.Invoke();
    }
}
