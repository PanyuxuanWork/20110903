# TerrainView Public API

External systems should use semantic APIs:

## Hover

```csharp
terrainViewService.SetHoveredCell(cellIndex);
terrainViewService.ClearHoveredCell();
```

## Building preview

```csharp
terrainViewService.SetBuildingPreviewCells(cells, isValid);
terrainViewService.SetBuildingPreviewCells(validCells, invalidCells);
terrainViewService.ClearBuildingPreview();
```

## Road preview

```csharp
terrainViewService.SetRoadPreviewCells(cells, isValid);
terrainViewService.ClearRoadPreview();
```

## Selection

```csharp
terrainViewService.SetSelectedCells(cells);
terrainViewService.ClearSelectedCells();
```

## Territory

```csharp
terrainViewService.ShowTerritoryCells(cells, TerrainViewType.PlayerTerritory);
terrainViewService.ClearTerritory();
```

## Buildability

```csharp
terrainViewService.SetBuildabilityOverlay(cells, true);
terrainViewService.SetBuildabilityOverlay(cells, false);
terrainViewService.ShowBlockedCells(cells);
terrainViewService.ClearBuildabilityOverlay();
```

## Debug

```csharp
terrainViewService.ShowDebugCells(cells);
terrainViewService.ShowDebugPath(pathCells);
terrainViewService.SetDebugCellColor(cellIndex, color);
terrainViewService.ClearDebugViews();
terrainViewService.ClearDebugColors();
```

## Lifecycle

```csharp
terrainViewService.ClearTemporaryViews(); // Hover + Preview + Selection
terrainViewService.ClearGameplayViews();  // Territory + Buildability + Preview + Selection + Hover
terrainViewService.ClearAllViews();       // Everything
```

## Batching

```csharp
using (terrainViewService.Batch())
{
    terrainViewService.ClearBuildingPreview();
    terrainViewService.SetBuildingPreviewCells(previewCells, isValid);
    terrainViewService.SetHoveredCell(cellIndex);
}
```

During a batch, the service delays refresh. The outermost EndBatch refreshes once.
