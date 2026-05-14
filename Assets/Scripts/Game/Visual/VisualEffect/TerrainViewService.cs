using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The only public entry point for terrain cell visualization.
/// 
/// External systems should call semantic APIs here, such as:
/// - SetHoveredCell
/// - SetBuildingPreviewCells
/// - SetSelectedCells
/// - ShowTerritoryCells
/// - SetBuildabilityOverlay
/// 
/// External systems should not call TerrainViewController, TerrainRegionMaskBackend,
/// material, shader, or RegionColorMask directly.
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainViewService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerrainViewController controller;
    [SerializeField] private GridAsset gridAsset;
    [SerializeField] private TerrainViewStyleConfig styleConfig;

    [Header("Refresh")]
    [SerializeField] private bool refreshOnAwake = false;
    [SerializeField] private bool autoRefresh = true;
    [SerializeField] private bool usePartialResolve = true;

    [Header("Odin Debug - Cell")]
    [SerializeField] private int debugCellIndex = 0;
    [SerializeField] private TerrainViewLayer debugLayer = TerrainViewLayer.Preview;
    [SerializeField] private TerrainViewType debugViewType = TerrainViewType.PreviewValid;
    [SerializeField] private TerrainViewStylePart debugStylePart = TerrainViewStylePart.Fill;
    [SerializeField] private byte debugRequestPriority = 0;
    [SerializeField] private Color32 debugOverrideColor = new Color32(255, 0, 255, 255);

    [Header("Odin Debug - Area")]
    [SerializeField] private byte debugAreaId = 1;
    [SerializeField] private TerrainViewLayer debugAreaLayer = TerrainViewLayer.Territory;
    [SerializeField] private TerrainViewType debugAreaViewType = TerrainViewType.PlayerTerritory;
    [SerializeField] private byte debugAreaRequestPriority = 0;

    private TerrainViewState state;
    private TerrainViewResolver resolver;
    private long lastResolvedVersion = -1;

    private int batchDepth;
    private bool refreshRequestedDuringBatch;

    private readonly List<int> tempFillCells = new List<int>(256);
    private readonly List<int> tempBorderCells = new List<int>(256);

    private readonly List<int> currentHoverCells = new List<int>(4);
    private readonly List<int> currentPreviewCells = new List<int>(128);
    private readonly List<int> currentSelectionCells = new List<int>(128);
    private readonly List<int> currentBuildabilityCells = new List<int>(128);

    public TerrainViewState State => state;
    public GridAsset Grid => gridAsset;
    public bool IsReady => controller != null && gridAsset != null && styleConfig != null;
    public bool IsBatching => batchDepth > 0;

    [ShowInInspector, ReadOnly] public long StateVersion => state?.Version ?? 0;
    [ShowInInspector, ReadOnly] public int TotalRequestCount => state?.GetTotalRequestCount() ?? 0;
    [ShowInInspector, ReadOnly] public bool DirtyAll => state?.DirtyAll ?? false;
    [ShowInInspector, ReadOnly] public int DirtyCellCount => state?.DirtyCellCount ?? 0;
    [ShowInInspector, ReadOnly] public int BatchDepth => batchDepth;
    [ShowInInspector, ReadOnly] public bool RefreshRequestedDuringBatch => refreshRequestedDuringBatch;
    [ShowInInspector, ReadOnly] public int LastAreaFillCount => tempFillCells.Count;
    [ShowInInspector, ReadOnly] public int LastAreaBorderCount => tempBorderCells.Count;
    [ShowInInspector, ReadOnly] public int CurrentHoverCount => currentHoverCells.Count;
    [ShowInInspector, ReadOnly] public int CurrentPreviewCount => currentPreviewCells.Count;
    [ShowInInspector, ReadOnly] public int CurrentSelectionCount => currentSelectionCells.Count;
    [ShowInInspector, ReadOnly] public int CurrentBuildabilityCount => currentBuildabilityCells.Count;

    private void Awake()
    {
        Initialize();

        if (refreshOnAwake)
            Refresh(force: true);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        controller = GetComponent<TerrainViewController>();
        if (controller == null)
            controller = FindObjectOfType<TerrainViewController>();
    }

    private void OnValidate()
    {
        if (controller == null)
            controller = GetComponent<TerrainViewController>();

        if (gridAsset == null && controller != null)
            gridAsset = controller.Grid;
    }
#endif

    public void Initialize()
    {
        if (controller == null)
            controller = GetComponent<TerrainViewController>();

        if (controller == null)
        {
            Debug.LogError("[TerrainViewService] TerrainViewController is missing.", this);
            return;
        }

        if (gridAsset == null)
            gridAsset = controller.Grid;

        if (gridAsset == null)
        {
            Debug.LogError("[TerrainViewService] GridAsset is missing.", this);
            return;
        }

        if (styleConfig == null)
        {
            Debug.LogError("[TerrainViewService] TerrainViewStyleConfig is missing.", this);
            return;
        }

        state ??= new TerrainViewState();
        resolver ??= new TerrainViewResolver(gridAsset, styleConfig);
    }

    // ---------------------------------------------------------------------
    // Public batching API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Defers refresh until EndBatch().
    /// Use this when one gameplay operation changes multiple terrain view layers.
    /// </summary>
    public void BeginBatch()
    {
        EnsureInitialized();
        batchDepth++;
    }

    /// <summary>
    /// Ends a batch. The outermost EndBatch triggers one refresh if needed.
    /// </summary>
    public void EndBatch(bool refresh = true)
    {
        EnsureInitialized();

        if (batchDepth <= 0)
        {
            Debug.LogWarning("[TerrainViewService] EndBatch called without BeginBatch.", this);
            batchDepth = 0;
            return;
        }

        batchDepth--;

        if (batchDepth == 0 && refresh && refreshRequestedDuringBatch)
        {
            refreshRequestedDuringBatch = false;
            Refresh(force: false);
        }
    }

    /// <summary>
    /// Safe helper for code that wants to guarantee batch cleanup.
    /// Usage: using (terrainViewService.Batch()) { ... }
    /// </summary>
    public TerrainViewBatchScope Batch()
    {
        return new TerrainViewBatchScope(this);
    }

    public readonly struct TerrainViewBatchScope : System.IDisposable
    {
        private readonly TerrainViewService service;

        public TerrainViewBatchScope(TerrainViewService service)
        {
            this.service = service;
            this.service.BeginBatch();
        }

        public void Dispose()
        {
            service.EndBatch(refresh: true);
        }
    }

    // ---------------------------------------------------------------------
    // Public semantic gameplay APIs
    // ---------------------------------------------------------------------

    public void SetHoveredCell(int cellIndex)
    {
        SetHoverCell(cellIndex, TerrainViewType.Hovered);
    }

    public void ClearHoveredCell()
    {
        ClearHover();
    }

    /// <summary>
    /// Building preview API. External building system only needs cells + validity.
    /// </summary>
    public void SetBuildingPreviewCells(IEnumerable<int> cellIndices, bool isValid)
    {
        SetPreviewCells(
            cellIndices,
            isValid ? TerrainViewType.PreviewValid : TerrainViewType.PreviewInvalid,
            TerrainViewStylePart.Fill);
    }

    public void SetBuildingPreviewCells(IEnumerable<int> validCells, IEnumerable<int> invalidCells)
    {
        EnsureInitialized();

        using (Batch())
        {
            ClearPreview();

            if (validCells != null)
            {
                state.SetCells(
                    TerrainViewLayer.Preview,
                    validCells,
                    TerrainViewType.PreviewValid,
                    TerrainViewStylePart.Fill,
                    requestPriority: 0);

                ReplaceTrackedCells(currentPreviewCells, validCells);
            }

            if (invalidCells != null)
            {
                state.SetCells(
                    TerrainViewLayer.Preview,
                    invalidCells,
                    TerrainViewType.PreviewInvalid,
                    TerrainViewStylePart.Fill,
                    requestPriority: 10);

                AddTrackedCells(currentPreviewCells, invalidCells);
            }

            RequestRefresh();
        }
    }

    public void ClearBuildingPreview()
    {
        ClearPreview();
    }

    public void SetRoadPreviewCells(IEnumerable<int> cellIndices, bool isValid = true)
    {
        SetPreviewCells(
            cellIndices,
            isValid ? TerrainViewType.PreviewRoad : TerrainViewType.PreviewInvalid,
            TerrainViewStylePart.Fill);
    }

    public void ClearRoadPreview()
    {
        ClearPreview();
    }

    public void SetSelectedCells(IEnumerable<int> cellIndices)
    {
        SetSelectionCells(cellIndices, TerrainViewType.Selected, TerrainViewStylePart.Border);
    }

    public void ClearSelectedCells()
    {
        ClearSelection();
    }

    public void ShowTerritoryCells(IEnumerable<int> cellIndices, TerrainViewType territoryType, bool asArea = true, bool clearLayerFirst = false)
    {
        TerrainViewType type = NormalizeTerritoryType(territoryType);

        if (asArea)
            ShowCellArea(cellIndices, TerrainViewLayer.Territory, type, requestPriority: 0, clearLayerFirst: clearLayerFirst);
        else
        {
            if (clearLayerFirst)
                ClearLayer(TerrainViewLayer.Territory);

            ShowCells(TerrainViewLayer.Territory, cellIndices, type, TerrainViewStylePart.Fill);
        }
    }

    public void ClearTerritory()
    {
        ClearLayer(TerrainViewLayer.Territory);
    }

    public void SetBuildabilityOverlay(IEnumerable<int> cellIndices, bool isBuildable)
    {
        SetBuildabilityCells(
            cellIndices,
            isBuildable ? TerrainViewType.Buildable : TerrainViewType.Unbuildable,
            TerrainViewStylePart.Fill);
    }

    public void ClearBuildabilityOverlay()
    {
        ClearBuildability();
    }

    public void ShowBlockedCells(IEnumerable<int> cellIndices)
    {
        SetBuildabilityCells(cellIndices, TerrainViewType.Blocked, TerrainViewStylePart.Fill);
    }

    public void ShowDebugCells(IEnumerable<int> cellIndices, TerrainViewType debugType = TerrainViewType.DebugArea, byte requestPriority = 0)
    {
        ShowCells(TerrainViewLayer.Debug, cellIndices, debugType, TerrainViewStylePart.Fill, requestPriority);
    }

    public void ShowDebugPath(IEnumerable<int> cellIndices)
    {
        ShowCells(TerrainViewLayer.Debug, cellIndices, TerrainViewType.DebugPath, TerrainViewStylePart.Fill);
    }

    public void ClearDebugViews()
    {
        ClearLayer(TerrainViewLayer.Debug);
    }

    public void SetDebugCellColor(int cellIndex, Color32 color)
    {
        SetOverrideCellColor(cellIndex, color);
    }

    public void SetDebugCellsColor(IEnumerable<int> cellIndices, Color32 color)
    {
        SetOverrideCellsColor(cellIndices, color);
    }

    public void ClearDebugColors()
    {
        ClearLayer(TerrainViewLayer.Override);
    }

    /// <summary>
    /// Clears high-frequency temporary views, but keeps long-term territory/buildability/debug data.
    /// </summary>
    public void ClearTemporaryViews()
    {
        using (Batch())
        {
            ClearHover();
            ClearPreview();
            ClearSelection();
        }
    }

    /// <summary>
    /// Clears gameplay overlays. Keeps Debug and Override by default.
    /// </summary>
    public void ClearGameplayViews()
    {
        using (Batch())
        {
            ClearLayer(TerrainViewLayer.Territory);
            ClearLayer(TerrainViewLayer.Buildability);
            ClearLayer(TerrainViewLayer.Preview);
            ClearLayer(TerrainViewLayer.Selection);
            ClearLayer(TerrainViewLayer.Hover);

            currentHoverCells.Clear();
            currentPreviewCells.Clear();
            currentSelectionCells.Clear();
            currentBuildabilityCells.Clear();
        }
    }

    public void ClearAllViews()
    {
        ClearAll();
    }

    // ---------------------------------------------------------------------
    // Lower-level APIs retained for internal/debug use
    // ---------------------------------------------------------------------

    public void ShowCell(TerrainViewLayer layer, int cellIndex, TerrainViewType viewType, TerrainViewStylePart stylePart = TerrainViewStylePart.Fill, byte requestPriority = 0)
    {
        EnsureInitialized();
        state.SetCell(layer, cellIndex, viewType, stylePart, requestPriority);
        RequestRefresh();
    }

    public void ShowCells(TerrainViewLayer layer, IEnumerable<int> cellIndices, TerrainViewType viewType, TerrainViewStylePart stylePart = TerrainViewStylePart.Fill, byte requestPriority = 0)
    {
        EnsureInitialized();
        state.SetCells(layer, cellIndices, viewType, stylePart, requestPriority);
        RequestRefresh();
    }

    public void ShowOwnerArea(byte areaId, TerrainViewLayer layer, TerrainViewType viewType, byte requestPriority = 0, bool clearLayerFirst = false)
    {
        EnsureInitialized();

        TerrainAreaCellBuilder.BuildFromOwnerArea(gridAsset, areaId, tempFillCells, tempBorderCells);

        if (clearLayerFirst)
            state.ClearLayer(layer);

        if (tempFillCells.Count > 0)
            state.SetCells(layer, tempFillCells, viewType, TerrainViewStylePart.Fill, requestPriority);

        if (tempBorderCells.Count > 0)
            state.SetCells(layer, tempBorderCells, viewType, TerrainViewStylePart.Border, requestPriority);

        RequestRefresh();
    }

    public void ShowCellArea(IEnumerable<int> areaCells, TerrainViewLayer layer, TerrainViewType viewType, byte requestPriority = 0, bool clearLayerFirst = false)
    {
        EnsureInitialized();

        TerrainAreaCellBuilder.BuildFromCellSet(gridAsset, areaCells, tempFillCells, tempBorderCells);

        if (clearLayerFirst)
            state.ClearLayer(layer);

        if (tempFillCells.Count > 0)
            state.SetCells(layer, tempFillCells, viewType, TerrainViewStylePart.Fill, requestPriority);

        if (tempBorderCells.Count > 0)
            state.SetCells(layer, tempBorderCells, viewType, TerrainViewStylePart.Border, requestPriority);

        RequestRefresh();
    }

    public void SetOverrideCellColor(int cellIndex, Color32 color, byte requestPriority = byte.MaxValue)
    {
        EnsureInitialized();
        state.SetOverrideCell(cellIndex, color, requestPriority);
        RequestRefresh();
    }

    public void SetOverrideCellsColor(IEnumerable<int> cellIndices, Color32 color, byte requestPriority = byte.MaxValue)
    {
        EnsureInitialized();
        state.SetOverrideCells(cellIndices, color, requestPriority);
        RequestRefresh();
    }

    public void ClearCell(TerrainViewLayer layer, int cellIndex)
    {
        EnsureInitialized();
        state.ClearCell(layer, cellIndex);
        RemoveFromRuntimeCaches(layer, cellIndex);
        RequestRefresh();
    }

    public void ClearCells(TerrainViewLayer layer, IEnumerable<int> cellIndices)
    {
        EnsureInitialized();
        state.ClearCells(layer, cellIndices);
        RemoveFromRuntimeCaches(layer, cellIndices);
        RequestRefresh();
    }

    public void ClearLayer(TerrainViewLayer layer)
    {
        EnsureInitialized();
        state.ClearLayer(layer);
        ClearRuntimeCache(layer);
        RequestRefresh();
    }

    public void ClearAll()
    {
        EnsureInitialized();
        state.ClearAll();
        currentHoverCells.Clear();
        currentPreviewCells.Clear();
        currentSelectionCells.Clear();
        currentBuildabilityCells.Clear();
        RequestRefresh();
    }

    public void ShowLayer(TerrainViewLayer layer)
    {
        EnsureInitialized();
        state.SetLayerVisible(layer, true);
        RequestRefresh();
    }

    public void HideLayer(TerrainViewLayer layer)
    {
        EnsureInitialized();
        state.SetLayerVisible(layer, false);
        RequestRefresh();
    }

    public void SetOverlayVisible(bool visible)
    {
        EnsureInitialized();
        controller.SetOverlayVisible(visible);
    }

    // Existing lifecycle APIs retained for compatibility.
    public void SetHoverCell(int cellIndex, TerrainViewType viewType = TerrainViewType.Hovered)
    {
        EnsureInitialized();

        if (currentHoverCells.Count > 0)
        {
            state.ClearCells(TerrainViewLayer.Hover, currentHoverCells);
            currentHoverCells.Clear();
        }

        if (cellIndex >= 0)
        {
            currentHoverCells.Add(cellIndex);
            state.SetCell(TerrainViewLayer.Hover, cellIndex, viewType, TerrainViewStylePart.Border, 0);
        }

        RequestRefresh();
    }

    public void ClearHover()
    {
        ClearTrackedLayer(TerrainViewLayer.Hover, currentHoverCells);
    }

    public void SetPreviewCells(IEnumerable<int> cellIndices, TerrainViewType viewType, TerrainViewStylePart stylePart = TerrainViewStylePart.Fill, byte requestPriority = 0)
    {
        ReplaceTrackedLayer(TerrainViewLayer.Preview, currentPreviewCells, cellIndices, viewType, stylePart, requestPriority);
    }

    public void ClearPreview()
    {
        ClearTrackedLayer(TerrainViewLayer.Preview, currentPreviewCells);
    }

    public void SetSelectionCells(IEnumerable<int> cellIndices, TerrainViewType viewType = TerrainViewType.Selected, TerrainViewStylePart stylePart = TerrainViewStylePart.Border, byte requestPriority = 0)
    {
        ReplaceTrackedLayer(TerrainViewLayer.Selection, currentSelectionCells, cellIndices, viewType, stylePart, requestPriority);
    }

    public void ClearSelection()
    {
        ClearTrackedLayer(TerrainViewLayer.Selection, currentSelectionCells);
    }

    public void SetBuildabilityCells(IEnumerable<int> cellIndices, TerrainViewType viewType, TerrainViewStylePart stylePart = TerrainViewStylePart.Fill, byte requestPriority = 0)
    {
        ReplaceTrackedLayer(TerrainViewLayer.Buildability, currentBuildabilityCells, cellIndices, viewType, stylePart, requestPriority);
    }

    public void ClearBuildability()
    {
        ClearTrackedLayer(TerrainViewLayer.Buildability, currentBuildabilityCells);
    }

    // ---------------------------------------------------------------------
    // Refresh
    // ---------------------------------------------------------------------

    public void Refresh(bool force = false)
    {
        EnsureInitialized();

        if (!force && lastResolvedVersion == state.Version)
            return;

        Color32[] colors;
        bool dirtyAll = force || state.DirtyAll || !usePartialResolve;

        if (dirtyAll)
            colors = resolver.ResolveAll(state);
        else
            colors = resolver.ResolveCells(state, state.DirtyCells);

        controller.UploadResolvedColorsSmart(colors, state.DirtyCells, dirtyAll, apply: true);

        lastResolvedVersion = state.Version;
        state.ConsumeDirty();
    }

    public void RefreshNow()
    {
        Refresh(force: false);
    }

    public void ForceFullRefresh()
    {
        EnsureInitialized();
        state.ForceDirtyAll();
        Refresh(force: true);
    }

    private void RequestRefresh()
    {
        if (!autoRefresh)
            return;

        if (batchDepth > 0)
        {
            refreshRequestedDuringBatch = true;
            return;
        }

        Refresh(force: false);
    }

    private void EnsureInitialized()
    {
        Initialize();

        if (!IsReady)
            throw new System.InvalidOperationException("TerrainViewService is not ready.");

        if (!controller.IsInitialized)
            controller.Initialize();
    }

    private void ReplaceTrackedLayer(TerrainViewLayer layer, List<int> trackedCells, IEnumerable<int> newCells, TerrainViewType viewType, TerrainViewStylePart stylePart, byte requestPriority)
    {
        EnsureInitialized();

        if (trackedCells.Count > 0)
        {
            state.ClearCells(layer, trackedCells);
            trackedCells.Clear();
        }

        if (newCells != null)
        {
            foreach (int cellIndex in newCells)
            {
                if (cellIndex < 0)
                    continue;

                trackedCells.Add(cellIndex);
            }
        }

        if (trackedCells.Count > 0)
            state.SetCells(layer, trackedCells, viewType, stylePart, requestPriority);

        RequestRefresh();
    }

    private void ClearTrackedLayer(TerrainViewLayer layer, List<int> trackedCells)
    {
        EnsureInitialized();

        if (trackedCells.Count == 0)
            return;

        state.ClearCells(layer, trackedCells);
        trackedCells.Clear();
        RequestRefresh();
    }

    private void ReplaceTrackedCells(List<int> target, IEnumerable<int> source)
    {
        target.Clear();
        AddTrackedCells(target, source);
    }

    private void AddTrackedCells(List<int> target, IEnumerable<int> source)
    {
        if (source == null)
            return;

        foreach (int cellIndex in source)
        {
            if (cellIndex < 0)
                continue;

            if (!target.Contains(cellIndex))
                target.Add(cellIndex);
        }
    }

    private void RemoveFromRuntimeCaches(TerrainViewLayer layer, int cellIndex)
    {
        if (cellIndex < 0)
            return;

        switch (layer)
        {
            case TerrainViewLayer.Hover:
                currentHoverCells.Remove(cellIndex);
                break;
            case TerrainViewLayer.Preview:
                currentPreviewCells.Remove(cellIndex);
                break;
            case TerrainViewLayer.Selection:
                currentSelectionCells.Remove(cellIndex);
                break;
            case TerrainViewLayer.Buildability:
                currentBuildabilityCells.Remove(cellIndex);
                break;
        }
    }

    private void RemoveFromRuntimeCaches(TerrainViewLayer layer, IEnumerable<int> cellIndices)
    {
        if (cellIndices == null)
            return;

        foreach (int cellIndex in cellIndices)
            RemoveFromRuntimeCaches(layer, cellIndex);
    }

    private void ClearRuntimeCache(TerrainViewLayer layer)
    {
        switch (layer)
        {
            case TerrainViewLayer.Hover:
                currentHoverCells.Clear();
                break;
            case TerrainViewLayer.Preview:
                currentPreviewCells.Clear();
                break;
            case TerrainViewLayer.Selection:
                currentSelectionCells.Clear();
                break;
            case TerrainViewLayer.Buildability:
                currentBuildabilityCells.Clear();
                break;
        }
    }

    private TerrainViewType NormalizeTerritoryType(TerrainViewType type)
    {
        switch (type)
        {
            case TerrainViewType.PlayerTerritory:
            case TerrainViewType.EnemyTerritory:
            case TerrainViewType.NeutralTerritory:
                return type;
            default:
                return TerrainViewType.NeutralTerritory;
        }
    }

    // ---------------------------------------------------------------------
    // Odin debug
    // ---------------------------------------------------------------------

    [Button("Begin Batch", ButtonSizes.Small)]
    private void OdinBeginBatch()
    {
        BeginBatch();
    }

    [Button("End Batch", ButtonSizes.Small)]
    private void OdinEndBatch()
    {
        EndBatch(refresh: true);
    }

    [Button("Show Test Cell", ButtonSizes.Medium)]
    [GUIColor(0.4f, 0.8f, 1f)]
    private void OdinShowTestCell()
    {
        ShowCell(debugLayer, debugCellIndex, debugViewType, debugStylePart, debugRequestPriority);
    }

    [Button("Show Override Cell", ButtonSizes.Medium)]
    [GUIColor(1f, 0.5f, 1f)]
    private void OdinShowOverrideCell()
    {
        SetOverrideCellColor(debugCellIndex, debugOverrideColor);
    }

    [Button("Set Hover Cell", ButtonSizes.Small)]
    [GUIColor(1f, 0.9f, 0.3f)]
    private void OdinSetHoverCell()
    {
        SetHoveredCell(debugCellIndex);
    }

    [Button("Clear Hover", ButtonSizes.Small)]
    private void OdinClearHover()
    {
        ClearHoveredCell();
    }

    [Button("Show Owner Area", ButtonSizes.Medium)]
    [GUIColor(1f, 0.65f, 0.25f)]
    private void OdinShowOwnerArea()
    {
        ShowOwnerArea(debugAreaId, debugAreaLayer, debugAreaViewType, debugAreaRequestPriority, clearLayerFirst: true);
    }

    [Button("Clear Temporary Views", ButtonSizes.Small)]
    private void OdinClearTemporaryViews()
    {
        ClearTemporaryViews();
    }

    [Button("Clear Gameplay Views", ButtonSizes.Small)]
    private void OdinClearGameplayViews()
    {
        ClearGameplayViews();
    }

    [Button("Clear Debug Views", ButtonSizes.Small)]
    private void OdinClearDebugViews()
    {
        ClearDebugViews();
    }

    [Button("Clear Override Layer", ButtonSizes.Small)]
    private void OdinClearOverrideLayer()
    {
        ClearDebugColors();
    }

    [Button("Clear All", ButtonSizes.Small)]
    [GUIColor(1f, 0.35f, 0.35f)]
    private void OdinClearAll()
    {
        ClearAllViews();
    }

    [Button("Refresh Now", ButtonSizes.Small)]
    private void OdinRefreshNow()
    {
        RefreshNow();
    }

    [Button("Force Full Refresh", ButtonSizes.Small)]
    private void OdinForceFullRefresh()
    {
        ForceFullRefresh();
    }

    [Button("Hide Overlay", ButtonSizes.Small)]
    private void OdinHideOverlay()
    {
        SetOverlayVisible(false);
    }

    [Button("Show Overlay", ButtonSizes.Small)]
    private void OdinShowOverlay()
    {
        SetOverlayVisible(true);
    }
}
