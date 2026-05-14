using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Simple debug selection driver.
/// Left click selects one cell through TerrainViewService.
/// Shift + Left click appends to the current selection.
/// Right click clears selection.
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainSelectionDebugDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerrainCellRaycaster cellRaycaster;
    [SerializeField] private TerrainViewService terrainViewService;

    [Header("Input")]
    [SerializeField] private bool enableSelection = false;
    [SerializeField] private KeyCode appendKey = KeyCode.LeftShift;

    [Header("Selection Style")]
    [SerializeField] private TerrainViewType selectionViewType = TerrainViewType.Selected;
    [SerializeField] private TerrainViewStylePart selectionStylePart = TerrainViewStylePart.Border;

    private readonly List<int> selectedCells = new List<int>();

    [ShowInInspector, ReadOnly]
    public int SelectionCount => selectedCells.Count;

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
        if (!enableSelection)
            return;

        if (Input.GetMouseButtonDown(0))
            SelectFromMouse(append: Input.GetKey(appendKey));

        if (Input.GetMouseButtonDown(1))
            ClearSelection();
    }

    public void SelectFromMouse(bool append)
    {
        AutoAssignMissingReferences();

        if (cellRaycaster == null || terrainViewService == null)
            return;

        if (!cellRaycaster.TryGetCellFromMouse(out TerrainCellHit hit))
            return;

        if (!append)
            selectedCells.Clear();

        if (!selectedCells.Contains(hit.cellIndex))
            selectedCells.Add(hit.cellIndex);

        terrainViewService.SetSelectionCells(selectedCells, selectionViewType, selectionStylePart);
    }

    public void ClearSelection()
    {
        selectedCells.Clear();
        terrainViewService?.ClearSelection();
    }

    public void SetSelectionEnabled(bool enabled)
    {
        enableSelection = enabled;
    }

    private void AutoAssignMissingReferences()
    {
        if (cellRaycaster == null)
            cellRaycaster = GetComponent<TerrainCellRaycaster>();

        if (terrainViewService == null)
            terrainViewService = GetComponent<TerrainViewService>();
    }

    [Button("Enable Selection", ButtonSizes.Small)]
    private void OdinEnableSelection()
    {
        SetSelectionEnabled(true);
    }

    [Button("Disable Selection", ButtonSizes.Small)]
    private void OdinDisableSelection()
    {
        SetSelectionEnabled(false);
    }

    [Button("Clear Selection", ButtonSizes.Small)]
    private void OdinClearSelection()
    {
        ClearSelection();
    }
}
