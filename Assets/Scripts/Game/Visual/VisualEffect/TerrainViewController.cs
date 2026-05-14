using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Terrain display controller.
/// Owns runtime material and Region mask backend.
/// Business systems should call TerrainViewService instead.
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainViewController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Terrain terrain;
    [SerializeField] private GridAsset gridAsset;
    [SerializeField] private Material materialOverride;

    [Header("Overlay")]
    [SerializeField, Range(0f, 1f)] private float overlayOpacity = 1f;
    [SerializeField] private bool overlayVisible = true;

    [Header("Upload Optimization")]
    [SerializeField] private bool allowPartialUpload = true;
    [SerializeField, Min(1)] private int fullUploadDirtyCellThreshold = 4096;

    [Header("Debug")]
    [SerializeField] private int debugCellIndex;
    [SerializeField] private Color32 debugColor = new Color32(255, 0, 255, 255);

    private Material originalMaterial;
    private Material runtimeMaterial;
    private TerrainRegionMaskBackend backend;
    private bool initialized;

    public bool IsInitialized => initialized;
    public GridAsset Grid => gridAsset;
    public TerrainRegionMaskBackend Backend => backend;

    [ShowInInspector, ReadOnly] public Texture2D RuntimeRegionTexture => backend?.Texture;
    [ShowInInspector, ReadOnly] public Material RuntimeMaterial => runtimeMaterial;

    private void Awake()
    {
        Initialize();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        terrain = GetComponent<Terrain>();
    }

    private void OnValidate()
    {
        overlayOpacity = Mathf.Clamp01(overlayOpacity);

        if (backend != null)
        {
            backend.SetOverlayOpacity(overlayOpacity);
            backend.SetVisible(overlayVisible);
        }
    }
#endif

    private void OnDestroy()
    {
        Dispose();
    }

    public void Initialize()
    {
        if (initialized)
            return;

        if (terrain == null)
            terrain = GetComponent<Terrain>();

        if (terrain == null)
        {
            Debug.LogError("[TerrainViewController] Terrain is missing.", this);
            return;
        }

        if (gridAsset == null)
        {
            Debug.LogError("[TerrainViewController] GridAsset is missing.", this);
            return;
        }

        originalMaterial = terrain.materialTemplate;

        Material sourceMaterial = materialOverride != null ? materialOverride : originalMaterial;
        if (sourceMaterial == null)
        {
            Debug.LogError("[TerrainViewController] Terrain material is missing. Assign MaterialOverride or terrain.materialTemplate.", this);
            return;
        }

        runtimeMaterial = Instantiate(sourceMaterial);
        runtimeMaterial.name = $"{sourceMaterial.name}_RuntimeRegion";
        terrain.materialTemplate = runtimeMaterial;

        backend = new TerrainRegionMaskBackend(gridAsset, runtimeMaterial);
        backend.Initialize();
        backend.SetOverlayOpacity(overlayOpacity);
        backend.SetVisible(overlayVisible);

        initialized = true;
    }

    public void Dispose()
    {
        if (terrain != null)
            terrain.materialTemplate = originalMaterial;

        backend?.Dispose();
        backend = null;

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        initialized = false;
    }

    public void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;
        backend?.SetVisible(visible);
    }

    public void SetOverlayOpacity(float opacity)
    {
        overlayOpacity = Mathf.Clamp01(opacity);
        backend?.SetOverlayOpacity(overlayOpacity);
    }

    public void UploadResolvedColors(Color32[] resolvedColors, bool apply = true)
    {
        EnsureInitialized();
        backend.UploadFull(resolvedColors, apply);
    }

    public void UploadResolvedCells(Color32[] resolvedColors, IReadOnlyCollection<int> dirtyCells, bool apply = true)
    {
        EnsureInitialized();

        if (!allowPartialUpload || dirtyCells == null || dirtyCells.Count == 0)
            return;

        if (dirtyCells.Count >= fullUploadDirtyCellThreshold)
        {
            backend.UploadFull(resolvedColors, apply);
            return;
        }

        backend.UploadCells(resolvedColors, dirtyCells, apply);
    }

    public void UploadResolvedColorsSmart(Color32[] resolvedColors, IReadOnlyCollection<int> dirtyCells, bool dirtyAll, bool apply = true)
    {
        EnsureInitialized();

        if (dirtyAll || !allowPartialUpload || dirtyCells == null || dirtyCells.Count == 0 || dirtyCells.Count >= fullUploadDirtyCellThreshold)
        {
            backend.UploadFull(resolvedColors, apply);
            return;
        }

        backend.UploadCells(resolvedColors, dirtyCells, apply);
    }

    public void SetCellColorForDebug(int cellIndex, Color32 color, bool apply = true)
    {
        EnsureInitialized();
        backend.SetCell(cellIndex, color, apply);
    }

    public void ClearOverlay()
    {
        EnsureInitialized();
        backend.Clear(apply: true);
    }

    private void EnsureInitialized()
    {
        if (!initialized)
            Initialize();

        if (!initialized || backend == null)
            throw new System.InvalidOperationException("TerrainViewController is not initialized.");
    }

    [Button("Paint Test Cell", ButtonSizes.Medium)]
    [GUIColor(1f, 0.5f, 1f)]
    private void OdinPaintTestCell()
    {
        SetCellColorForDebug(debugCellIndex, debugColor, true);
    }

    [Button("Clear Overlay", ButtonSizes.Small)]
    private void OdinClearOverlay()
    {
        ClearOverlay();
    }

    [Button("Show Overlay", ButtonSizes.Small)]
    private void OdinShowOverlay()
    {
        SetOverlayVisible(true);
    }

    [Button("Hide Overlay", ButtonSizes.Small)]
    private void OdinHideOverlay()
    {
        SetOverlayVisible(false);
    }
}
