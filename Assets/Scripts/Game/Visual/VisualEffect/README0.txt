TerrainView Lifecycle Step

This step updates TerrainViewService.

Added gameplay-facing convenience APIs:

Hover:
- SetHoverCell(int cellIndex, TerrainViewType viewType = TerrainViewType.Hovered)
- ClearHover()

Preview:
- SetPreviewCells(IEnumerable<int> cellIndices, TerrainViewType viewType, TerrainViewStylePart stylePart = Fill, byte requestPriority = 0)
- ClearPreview()

Selection:
- SetSelectionCells(IEnumerable<int> cellIndices, TerrainViewType viewType = Selected, TerrainViewStylePart stylePart = Border, byte requestPriority = 0)
- ClearSelection()

Buildability:
- SetBuildabilityCells(IEnumerable<int> cellIndices, TerrainViewType viewType, TerrainViewStylePart stylePart = Fill, byte requestPriority = 0)
- ClearBuildability()

These APIs clear their previous tracked cells before writing new ones. This avoids stale preview/hover/selection data while keeping the service as the only external entry point.

It still does not implement dirty cell upload. Current backend/controller path may still upload full resolved colors.
