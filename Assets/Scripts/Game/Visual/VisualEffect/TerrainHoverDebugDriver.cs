using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Simple input bridge example:
/// Mouse position -> TerrainCellRaycaster -> TerrainViewService.SetHoverCell.
/// This can be removed later when the real input/selection system owns hover.
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainHoverDebugDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerrainCellRaycaster cellRaycaster;
    [SerializeField] private TerrainViewService terrainViewService;

    [Header("Hover")]
    [SerializeField] private bool enableHover = true;
    [SerializeField] private TerrainViewType hoverViewType = TerrainViewType.Hovered;
    [SerializeField] private bool clearHoverWhenNoHit = true;

    [ShowInInspector, ReadOnly]
    public int CurrentHoverCell { get; private set; } = -1;

    private void Reset()
    {
        AutoAssignMissingReferences();
    }

    private void Awake()
    {
        AutoAssignMissingReferences();
    }

    private void Update()
    {
        if (!enableHover)
            return;

        UpdateHoverFromMouse();
    }

    public void UpdateHoverFromMouse()
    {
        AutoAssignMissingReferences();

        if (cellRaycaster == null || terrainViewService == null)
            return;

        if (cellRaycaster.TryGetCellFromMouse(out TerrainCellHit hit))
        {
            if (hit.cellIndex != CurrentHoverCell)
            {
                CurrentHoverCell = hit.cellIndex;
                terrainViewService.SetHoverCell(hit.cellIndex, hoverViewType);
            }
        }
        else if (clearHoverWhenNoHit && CurrentHoverCell >= 0)
        {
            CurrentHoverCell = -1;
            terrainViewService.ClearHover();
        }
    }

    public void SetHoverEnabled(bool enabled)
    {
        enableHover = enabled;

        if (!enabled)
        {
            CurrentHoverCell = -1;
            terrainViewService?.ClearHover();
        }
    }

    private void AutoAssignMissingReferences()
    {
        if (cellRaycaster == null)
            cellRaycaster = GetComponent<TerrainCellRaycaster>();

        if (terrainViewService == null)
            terrainViewService = GetComponent<TerrainViewService>();
    }

    [Button("Enable Hover", ButtonSizes.Small)]
    private void OdinEnableHover()
    {
        SetHoverEnabled(true);
    }

    [Button("Disable Hover And Clear", ButtonSizes.Small)]
    private void OdinDisableHover()
    {
        SetHoverEnabled(false);
    }
}
