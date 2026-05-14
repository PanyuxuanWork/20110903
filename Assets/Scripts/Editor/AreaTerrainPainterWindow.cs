/***************************************************************************
// File       : AreaTerrainPainterWindow.cs
// Author     : Panyuxuan
// Created    : 2026/04/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Add script summary here
// ***************************************************************************/

/***************************************************************************
// File       : AreaPainterWindow.cs
// Author     : Panyuxuan
// Created    : 2026/04/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Editor-only Area painter for GridAsset.ownerAreaId[]
// ***************************************************************************/

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AreaPainterWindow : EditorWindow
{
    private enum BrushMode
    {
        Paint,
        Erase,
        Pick
    }

    private enum BrushShape
    {
        Circle,
        Square
    }

    private AreaContext areaContext;
    private GridAsset worldGrid;
    private Area selectedArea;

    private BrushMode brushMode = BrushMode.Paint;
    private BrushShape brushShape = BrushShape.Circle;

    private int brushRadius = 1; // 单位：cell
    private bool repaintOnMouseUpOnly = true;
    private bool showCellPreview = true;
    private bool lockSelectionWhilePainting = true;

    // 新增：已绘制区域可视化
    private bool showPaintedOverlay = true;
    private bool showOnlySelectedArea = false;
    private float paintedOverlayAlpha = 0.22f;
    private bool highlightSelectedArea = true;
    private bool showGridOutline = true;

    private bool isPainting;
    private bool gridDirtyInStroke;

    private readonly HashSet<int> _strokeTouchedIndices = new();
    private Vector3 _lastHitWorld;
    private bool _hasHitWorld;

    [MenuItem("Tools/Area Painter")]
    public static void Open()
    {
        var window = GetWindow<AreaPainterWindow>("Area Painter");
        window.minSize = new Vector2(380f, 360f);
        window.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        TryAutoBind();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSelectionChange()
    {
        if (!lockSelectionWhilePainting)
        {
            Repaint();
            return;
        }

        if (isPainting) return;

        if (Selection.activeGameObject != null &&
            Selection.activeGameObject.TryGetComponent(out Area area))
        {
            selectedArea = area;
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("上下文", EditorStyles.boldLabel);

            var newAreaContext = (AreaContext)EditorGUILayout.ObjectField(
                "AreaContext",
                areaContext,
                typeof(AreaContext),
                true);

            if (newAreaContext != areaContext)
            {
                areaContext = newAreaContext;
                worldGrid = areaContext != null ? areaContext.WorldGrid : null;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("World Grid", worldGrid, typeof(GridAsset), false);
            }

            if (GUILayout.Button("自动绑定上下文"))
            {
                TryAutoBind();
            }
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("目标 Area", EditorStyles.boldLabel);

            selectedArea = (Area)EditorGUILayout.ObjectField("Selected Area", selectedArea, typeof(Area), true);

            using (new EditorGUI.DisabledScope(selectedArea == null))
            {
                EditorGUILayout.IntField("AreaId", selectedArea != null ? selectedArea.AreaId : 0);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("使用当前选中对象"))
            {
                if (Selection.activeGameObject != null &&
                    Selection.activeGameObject.TryGetComponent(out Area area))
                {
                    selectedArea = area;
                    SceneView.RepaintAll();
                }
                else
                {
                    Debug.LogWarning("[AreaPainter] 当前选中对象不是 Area。");
                }
            }

            if (GUILayout.Button("Ping AreaContext"))
            {
                if (areaContext != null)
                    EditorGUIUtility.PingObject(areaContext.gameObject);
            }
            EditorGUILayout.EndHorizontal();
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("画笔", EditorStyles.boldLabel);

            brushMode = (BrushMode)EditorGUILayout.EnumPopup("模式", brushMode);
            brushShape = (BrushShape)EditorGUILayout.EnumPopup("形状", brushShape);
            brushRadius = Mathf.Max(0, EditorGUILayout.IntSlider("半径(Cell)", brushRadius, 0, 16));

            showCellPreview = EditorGUILayout.Toggle("显示刷子预览", showCellPreview);
            repaintOnMouseUpOnly = EditorGUILayout.Toggle("抬起鼠标后重建缓存", repaintOnMouseUpOnly);
            lockSelectionWhilePainting = EditorGUILayout.Toggle("绘制时锁定选择", lockSelectionWhilePainting);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("区域可视化", EditorStyles.boldLabel);

            showPaintedOverlay = EditorGUILayout.Toggle("显示已绘制区域", showPaintedOverlay);
            showOnlySelectedArea = EditorGUILayout.Toggle("只显示当前 Area", showOnlySelectedArea);
            highlightSelectedArea = EditorGUILayout.Toggle("高亮当前 Area", highlightSelectedArea);
            showGridOutline = EditorGUILayout.Toggle("显示格子描边", showGridOutline);
            paintedOverlayAlpha = EditorGUILayout.Slider("填充透明度", paintedOverlayAlpha, 0.02f, 0.8f);
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Grid Ready", (worldGrid != null).ToString());
            EditorGUILayout.LabelField("Has Hit", _hasHitWorld.ToString());

            if (_hasHitWorld && worldGrid != null && worldGrid.WorldToCell(_lastHitWorld, out int x, out int z))
            {
                int idx = worldGrid.ToIndex(x, z);
                EditorGUILayout.LabelField("Cell", $"({x}, {z})");
                EditorGUILayout.LabelField("Index", idx.ToString());
                EditorGUILayout.LabelField("OwnerAreaId", worldGrid.GetOwnerAreaIdByIndex(idx).ToString());
            }
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("快捷操作", EditorStyles.boldLabel);

            if (GUILayout.Button("清空全部 Area 归属（ownerAreaId = 0）"))
            {
                ClearAllOwnership();
            }

            if (GUILayout.Button("手动重建 AreaContext 缓存"))
            {
                RebuildAreaContext();
            }
        }

        EditorGUILayout.HelpBox(
            "SceneView 操作：\n" +
            "• 左键拖动：绘制\n" +
            "• Shift + 左键：临时 Erase\n" +
            "• Ctrl/Cmd + 左键：临时 Pick\n" +
            "• Alt + 鼠标：让出给视角控制\n" +
            "• 这个工具直接修改 GridAsset.ownerAreaId[]，并用 Editor 覆盖层显示结果。",
            MessageType.Info);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        if (e == null)
            return;

        // Alt 让出给视角操作
        if (e.alt)
            return;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        // 在 Layout 阶段抢占默认控制，阻止 SceneView 框选
        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlId);
        }

        if (areaContext == null || worldGrid == null)
            return;

        bool hasHit = TryGetMouseWorldOnGrid(
            e.mousePosition,
            out Vector3 hitWorld,
            out int hitIndex,
            out int hitX,
            out int hitZ);

        _hasHitWorld = hasHit;
        if (hasHit)
            _lastHitWorld = hitWorld;

        // 先画已经存在的区域覆盖层
        if (showPaintedOverlay)
        {
            DrawPaintedCellsOverlay();
        }

        // 再画鼠标刷子预览
        if (hasHit && showCellPreview)
        {
            DrawBrushPreview(hitX, hitZ);
        }

        DrawOverlayPanel(e, hasHit, hitIndex, hitX, hitZ);

        if (e.button != 0)
            return;

        var effectiveMode = GetEffectiveBrushMode(e);

        if (effectiveMode == BrushMode.Paint && selectedArea == null)
            return;

        switch (e.type)
        {
            case EventType.MouseDown:
                if (!hasHit) return;

                isPainting = true;
                gridDirtyInStroke = false;
                _strokeTouchedIndices.Clear();

                GUIUtility.hotControl = controlId;
                ApplyBrushAt(hitX, hitZ, effectiveMode);
                e.Use();
                break;

            case EventType.MouseDrag:
                if (!isPainting)
                    return;

                if (hasHit)
                {
                    ApplyBrushAt(hitX, hitZ, effectiveMode);
                }

                GUIUtility.hotControl = controlId;
                e.Use();
                break;

            case EventType.MouseUp:
                if (!isPainting)
                    return;

                isPainting = false;

                if (gridDirtyInStroke)
                {
                    FinalizeStroke();
                }

                _strokeTouchedIndices.Clear();

                GUIUtility.hotControl = 0;
                e.Use();
                break;
        }
    }

    private void DrawOverlayPanel(Event e, bool hasHit, int hitIndex, int hitX, int hitZ)
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(10, 10, 290, 90), "Area Painter", "Window");
        GUILayout.Label($"Mode: {GetEffectiveBrushMode(e)}");
        GUILayout.Label(hasHit ? $"Cell: ({hitX}, {hitZ})  Index: {hitIndex}" : "Cell: <none>");
        GUILayout.Label($"Area: {(selectedArea != null ? selectedArea.AreaId.ToString() : "None")}");

        if (hasHit && worldGrid != null && hitIndex >= 0)
        {
            GUILayout.Label($"Owner: {worldGrid.GetOwnerAreaIdByIndex(hitIndex)}");
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private void TryAutoBind()
    {
        if (areaContext == null)
            areaContext = FindObjectOfType<AreaContext>();

        if (areaContext != null)
            worldGrid = areaContext.WorldGrid;

        if (selectedArea == null &&
            Selection.activeGameObject != null &&
            Selection.activeGameObject.TryGetComponent(out Area area))
        {
            selectedArea = area;
        }

        Repaint();
        SceneView.RepaintAll();
    }

    private BrushMode GetEffectiveBrushMode(Event e)
    {
        if (e == null) return brushMode;

        if (e.control || e.command)
            return BrushMode.Pick;

        if (e.shift)
            return BrushMode.Erase;

        return brushMode;
    }

    private bool TryGetMouseWorldOnGrid(Vector2 guiPos, out Vector3 hitWorld, out int index, out int cellX, out int cellZ)
    {
        hitWorld = default;
        index = -1;
        cellX = -1;
        cellZ = -1;

        if (worldGrid == null)
            return false;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPos);

        // 优先 Physics 命中（TerrainCollider / MeshCollider 都可）
        if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
        {
            hitWorld = hit.point;
        }
        else
        {
            // 退化方案：投到一个更合理的高度平面，而不是固定 y=0
            float planeY = 0f;
            if (worldGrid.heightY != null && worldGrid.heightY.Length > 0)
                planeY = worldGrid.heightY[0];

            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (!plane.Raycast(ray, out float enter))
                return false;

            hitWorld = ray.GetPoint(enter);
        }

        if (!worldGrid.WorldToCell(hitWorld, out cellX, out cellZ))
            return false;

        index = worldGrid.ToIndex(cellX, cellZ);
        return true;
    }

    private void ApplyBrushAt(int centerX, int centerZ, BrushMode mode)
    {
        if (worldGrid == null)
            return;

        byte targetAreaId = mode == BrushMode.Paint
            ? (selectedArea != null ? selectedArea.AreaId : GridAsset.NoAreaId)
            : GridAsset.NoAreaId;

        if (mode == BrushMode.Pick)
        {
            byte picked = worldGrid.GetOwnerAreaId(centerX, centerZ);
            TrySelectAreaById(picked);
            Repaint();
            SceneView.RepaintAll();
            return;
        }

        Undo.RecordObject(worldGrid, $"Area Paint ({mode})");

        bool changedAny = false;

        foreach (int index in EnumerateBrushIndices(centerX, centerZ))
        {
            if (_strokeTouchedIndices.Contains(index))
                continue;

            byte before = worldGrid.GetOwnerAreaIdByIndex(index);
            if (before == targetAreaId)
            {
                _strokeTouchedIndices.Add(index);
                continue;
            }

            worldGrid.SetOwnerAreaIdByIndex(index, targetAreaId);
            _strokeTouchedIndices.Add(index);
            changedAny = true;
        }

        if (!changedAny)
            return;

        gridDirtyInStroke = true;
        EditorUtility.SetDirty(worldGrid);

        // 关键：即使不立刻重建 AreaContext，也先把 Scene 刷新出来，
        // 覆盖层会直接读取 ownerAreaId[]，所以你能马上看到颜色变化。
        SceneView.RepaintAll();

        if (!repaintOnMouseUpOnly)
        {
            RebuildAreaContext();
        }
    }

    private IEnumerable<int> EnumerateBrushIndices(int centerX, int centerZ)
    {
        int radius = Mathf.Max(0, brushRadius);

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (brushShape == BrushShape.Circle)
                {
                    if (dx * dx + dz * dz > radius * radius)
                        continue;
                }

                int x = centerX + dx;
                int z = centerZ + dz;

                if ((uint)x >= (uint)worldGrid.Width || (uint)z >= (uint)worldGrid.Height)
                    continue;

                yield return worldGrid.ToIndex(x, z);
            }
        }
    }

    private void DrawBrushPreview(int centerX, int centerZ)
    {
        if (worldGrid == null) return;

        float cell = worldGrid.CellWidth;
        float yOffset = 0.18f;

        var oldZTest = Handles.zTest;
        Color oldColor = Handles.color;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        foreach (int index in EnumerateBrushIndices(centerX, centerZ))
        {
            Vector3 c = worldGrid.IndexToWorldCenter(index);
            c.y += yOffset;

            float half = cell * 0.5f;

            Vector3 p1 = new Vector3(c.x - half, c.y, c.z - half);
            Vector3 p2 = new Vector3(c.x - half, c.y, c.z + half);
            Vector3 p3 = new Vector3(c.x + half, c.y, c.z + half);
            Vector3 p4 = new Vector3(c.x + half, c.y, c.z - half);

            Handles.color = new Color(0f, 1f, 1f, 0.10f);
            Handles.DrawAAConvexPolygon(p1, p2, p3, p4);

            Handles.color = new Color(0f, 1f, 1f, 1f);
            Handles.DrawAAPolyLine(3f, p1, p2, p3, p4, p1);
        }

        Handles.zTest = oldZTest;
        Handles.color = oldColor;
    }

    private void DrawPaintedCellsOverlay()
    {
        if (worldGrid == null || worldGrid.ownerAreaId == null)
            return;

        var oldColor = Handles.color;
        var oldZTest = Handles.zTest;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        int cellCount = Mathf.Max(worldGrid.Width * worldGrid.Height, 0);
        float half = worldGrid.CellWidth * 0.5f;

        for (int index = 0; index < cellCount; index++)
        {
            byte areaId = worldGrid.GetOwnerAreaIdByIndex(index);
            if (areaId == GridAsset.NoAreaId)
                continue;

            if (showOnlySelectedArea && selectedArea != null && areaId != selectedArea.AreaId)
                continue;

            Vector3 c = worldGrid.IndexToWorldCenter(index);
            c.y += 0.08f;

            Color fill = GetAreaDebugColor(areaId);
            Color outline = GetAreaDebugColor(areaId);

            fill.a = paintedOverlayAlpha;
            outline.a = showGridOutline ? 0.95f : 0f;

            if (highlightSelectedArea && selectedArea != null && areaId == selectedArea.AreaId)
            {
                fill.a = Mathf.Clamp01(paintedOverlayAlpha + 0.15f);
                outline.a = 1f;
            }

            Vector3 p1 = new Vector3(c.x - half, c.y, c.z - half);
            Vector3 p2 = new Vector3(c.x - half, c.y, c.z + half);
            Vector3 p3 = new Vector3(c.x + half, c.y, c.z + half);
            Vector3 p4 = new Vector3(c.x + half, c.y, c.z - half);

            Handles.color = fill;
            Handles.DrawAAConvexPolygon(p1, p2, p3, p4);

            if (showGridOutline)
            {
                Handles.color = outline;
                Handles.DrawAAPolyLine(2f, p1, p2, p3, p4, p1);
            }
        }

        Handles.color = oldColor;
        Handles.zTest = oldZTest;
    }

    private Color GetAreaDebugColor(byte areaId)
    {
        switch (areaId % 8)
        {
            case 1: return new Color(1.00f, 0.25f, 0.25f, 1f); // 红
            case 2: return new Color(0.25f, 0.90f, 0.35f, 1f); // 绿
            case 3: return new Color(0.25f, 0.55f, 1.00f, 1f); // 蓝
            case 4: return new Color(1.00f, 0.85f, 0.25f, 1f); // 黄
            case 5: return new Color(0.90f, 0.35f, 1.00f, 1f); // 紫
            case 6: return new Color(0.20f, 0.95f, 0.95f, 1f); // 青
            case 7: return new Color(1.00f, 0.55f, 0.20f, 1f); // 橙
            default: return new Color(0.85f, 0.85f, 0.85f, 1f); // 灰
        }
    }

    private void TrySelectAreaById(byte areaId)
    {
        if (areaId == GridAsset.NoAreaId)
        {
            selectedArea = null;
            return;
        }

        if (areaContext != null && areaContext.TryGetAreaById(areaId, out Area found))
        {
            selectedArea = found;
            Selection.activeGameObject = found.gameObject;
        }
        else
        {
            Debug.LogWarning($"[AreaPainter] 吸取到 AreaId={areaId}，但 AreaContext 中没有对应 Area。");
        }
    }

    private void FinalizeStroke()
    {
        AssetDatabase.SaveAssetIfDirty(worldGrid);
        RebuildAreaContext();
        SceneView.RepaintAll();
    }

    private void RebuildAreaContext()
    {
        if (areaContext == null) return;

        Undo.RecordObject(areaContext.gameObject, "Rebuild Area Context");
        areaContext.RebuildFromWorldGrid();
        EditorUtility.SetDirty(areaContext.gameObject);
    }

    private void ClearAllOwnership()
    {
        if (worldGrid == null)
        {
            Debug.LogWarning("[AreaPainter] WorldGrid 为空。");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Clear All Area Ownership",
                "确认把整个 Grid 的 ownerAreaId[] 全部清成 0 吗？",
                "确认", "取消"))
        {
            return;
        }

        Undo.RecordObject(worldGrid, "Clear All Area Ownership");

        int cellCount = Mathf.Max(worldGrid.Width * worldGrid.Height, 0);
        for (int i = 0; i < cellCount; i++)
        {
            worldGrid.SetOwnerAreaIdByIndex(i, GridAsset.NoAreaId);
        }

        EditorUtility.SetDirty(worldGrid);
        AssetDatabase.SaveAssetIfDirty(worldGrid);
        RebuildAreaContext();
        SceneView.RepaintAll();
    }
}
#endif