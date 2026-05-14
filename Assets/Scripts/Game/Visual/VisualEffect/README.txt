TerrainView Input Bridge Step

New files:
- TerrainCellHit.cs
- TerrainCellRaycaster.cs
- TerrainHoverDebugDriver.cs
- TerrainSelectionDebugDriver.cs

Purpose:
Raycast Terrain -> world position -> GridAsset.WorldToCell -> cellIndex -> TerrainViewService.

Recommended setup:
1. Add TerrainCellRaycaster to the same GameObject as TerrainViewService.
2. Assign Camera, Terrain, GridAsset if auto assignment does not find them.
3. Set terrainLayerMask to the Terrain's layer.
4. Optionally add TerrainHoverDebugDriver to test mouse hover.
5. Optionally add TerrainSelectionDebugDriver to test click selection.

Usage:
- TerrainHoverDebugDriver updates service.SetHoverCell() every time the hovered cell changes.
- TerrainSelectionDebugDriver:
  Left click = select one cell
  Shift + left click = append cell
  Right click = clear selection

Core rule preserved:
External input systems still call TerrainViewService only.
They do not call TerrainViewController, Backend, material, texture, or shader.
