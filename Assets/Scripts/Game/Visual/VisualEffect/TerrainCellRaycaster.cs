using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Converts screen position / ray input into GridAsset cells.
/// This is the bridge used by hover, selection, building preview, and debug tools.
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainCellRaycaster : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera raycastCamera;
    [SerializeField] private Terrain terrain;
    [SerializeField] private GridAsset gridAsset;

    [Header("Raycast")]
    [SerializeField] private LayerMask terrainLayerMask = ~0;
    [SerializeField, Min(1f)] private float maxDistance = 10000f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay = false;
    [SerializeField] private float debugRayDuration = 0.05f;

    [ShowInInspector, ReadOnly]
    public int LastCellIndex { get; private set; } = -1;

    [ShowInInspector, ReadOnly]
    public Vector2Int LastCellXZ { get; private set; } = new Vector2Int(-1, -1);

    [ShowInInspector, ReadOnly]
    public Vector3 LastWorldPosition { get; private set; }

    public Camera RaycastCamera
    {
        get => raycastCamera;
        set => raycastCamera = value;
    }

    public GridAsset Grid
    {
        get => gridAsset;
        set => gridAsset = value;
    }

    private void Awake()
    {
        AutoAssignMissingReferences();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        AutoAssignMissingReferences();
    }

    private void OnValidate()
    {
        AutoAssignMissingReferences();
    }
#endif

    public bool TryGetCellFromMouse(out TerrainCellHit cellHit)
    {
        return TryGetCellFromScreenPosition(Input.mousePosition, out cellHit);
    }

    public bool TryGetCellFromScreenPosition(Vector2 screenPosition, out TerrainCellHit cellHit)
    {
        AutoAssignMissingReferences();

        if (raycastCamera == null)
        {
            Debug.LogError("[TerrainCellRaycaster] Camera is missing.", this);
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        Ray ray = raycastCamera.ScreenPointToRay(screenPosition);
        return TryGetCellFromRay(ray, out cellHit);
    }

    public bool TryGetCellFromRay(Ray ray, out TerrainCellHit cellHit)
    {
        AutoAssignMissingReferences();

        if (gridAsset == null)
        {
            Debug.LogError("[TerrainCellRaycaster] GridAsset is missing.", this);
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        if (drawDebugRay)
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.yellow, debugRayDuration);

        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, terrainLayerMask, triggerInteraction))
        {
            SetLastInvalid();
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        // Optional terrain guard: if assigned, ignore hits from other colliders.
        if (terrain != null)
        {
            TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null && hit.collider != terrainCollider)
            {
                SetLastInvalid();
                cellHit = TerrainCellHit.Invalid;
                return false;
            }
        }

        if (!gridAsset.WorldToCell(hit.point, out int x, out int z))
        {
            SetLastInvalid();
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        int index = gridAsset.ToIndex(x, z);

        LastCellIndex = index;
        LastCellXZ = new Vector2Int(x, z);
        LastWorldPosition = hit.point;

        cellHit = new TerrainCellHit(true, index, x, z, hit.point, hit);
        return true;
    }

    public bool TryWorldToCell(Vector3 worldPosition, out TerrainCellHit cellHit)
    {
        AutoAssignMissingReferences();

        if (gridAsset == null)
        {
            Debug.LogError("[TerrainCellRaycaster] GridAsset is missing.", this);
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        if (!gridAsset.WorldToCell(worldPosition, out int x, out int z))
        {
            SetLastInvalid();
            cellHit = TerrainCellHit.Invalid;
            return false;
        }

        int index = gridAsset.ToIndex(x, z);
        LastCellIndex = index;
        LastCellXZ = new Vector2Int(x, z);
        LastWorldPosition = worldPosition;

        cellHit = new TerrainCellHit(true, index, x, z, worldPosition, default);
        return true;
    }

    private void AutoAssignMissingReferences()
    {
        if (raycastCamera == null)
            raycastCamera = Camera.main;

        if (terrain == null)
            terrain = GetComponent<Terrain>();

        if (terrain == null)
            terrain = FindObjectOfType<Terrain>();

        if (gridAsset == null)
        {
            TerrainViewService service = GetComponent<TerrainViewService>();
            if (service != null)
                gridAsset = service.Grid;
        }
    }

    private void SetLastInvalid()
    {
        LastCellIndex = -1;
        LastCellXZ = new Vector2Int(-1, -1);
        LastWorldPosition = default;
    }

    [Button("Raycast Mouse Now", ButtonSizes.Small)]
    private void OdinRaycastMouseNow()
    {
        if (TryGetCellFromMouse(out TerrainCellHit hit))
            Debug.Log($"[TerrainCellRaycaster] Mouse hit cell index={hit.cellIndex}, x={hit.x}, z={hit.z}, world={hit.worldPosition}", this);
        else
            Debug.Log("[TerrainCellRaycaster] Mouse hit no valid cell.", this);
    }
}
