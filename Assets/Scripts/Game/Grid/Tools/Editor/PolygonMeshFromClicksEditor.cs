#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 在 Scene 里 Ctrl+左键拾取点 → 三角化 → 生成 Mesh。
/// 新增：Flatten To Avg Height 开关：生成前将所有顶点 Y 拉平为选点平均 Y。
/// </summary>
public class PolygonMeshFromClicksEditor : EditorWindow
{
    // ===== 可调参数 =====
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField] private bool _conformToSurface = true;           // 拾取点贴合碰撞表面
    [SerializeField] private bool _closeLoopPreview = true;           // 预览时闭合多边形
    [SerializeField] private float _uvScale = 1f;                     // UV 缩放（越大纹理越小）
    [SerializeField] private float _handleSize = 0.05f;               // 点的可视大小
    [SerializeField] private Material _previewLineMaterial = null;    // 可选：自定义预览线材质

    // 新增：生成前拉平
    [SerializeField] private bool _flattenToAverageHeight = true;

    [SerializeField] private string _meshObjectName = "PolygonMesh";

    // ===== 运行数据 =====
    private readonly List<Vector3> _points = new();   // 拾取的 3D 点
    private readonly List<Vector3> _normals = new();  // 拾取点所在面的法线（可选）
    private Vector3 _basisOrigin;                     // 平面投影的原点
    private Vector3 _basisT;                          // 平面切向
    private Vector3 _basisB;                          // 平面副切向
    private Vector3 _basisN;                          // 平面法线

    private bool _sceneHooked;

    // ===== 菜单 =====
    [MenuItem("Tools/Polygon Mesh From Clicks")]
    public static void Open()
    {
        var win = GetWindow<PolygonMeshFromClicksEditor>("Polygon From Clicks");
        win.Show();
    }

    private void OnEnable()
    {
        HookSceneGUI(true);
    }
    private void OnDisable()
    {
        HookSceneGUI(false);
    }

    private void HookSceneGUI(bool on)
    {
        if (on && !_sceneHooked)
        {
            SceneView.duringSceneGui += OnSceneGUI;
            _sceneHooked = true;
        }
        else if (!on && _sceneHooked)
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _sceneHooked = false;
        }
    }

    // ===== GUI =====
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Picking & Generation", EditorStyles.boldLabel);
        _hitMask = LayerMaskField("Hit Mask", _hitMask);
        _conformToSurface = EditorGUILayout.Toggle("Conform To Surface", _conformToSurface);
        _closeLoopPreview = EditorGUILayout.Toggle("Close Loop Preview", _closeLoopPreview);
        _uvScale = Mathf.Max(1e-4f, EditorGUILayout.FloatField("UV Scale", _uvScale));
        _handleSize = Mathf.Max(0.001f, EditorGUILayout.FloatField("Handle Size", _handleSize));

        // 新增：拉平到平均高度
        _flattenToAverageHeight = EditorGUILayout.Toggle("Flatten To Avg Height", _flattenToAverageHeight);

        _meshObjectName = EditorGUILayout.TextField("Mesh Object Name", _meshObjectName);
        _previewLineMaterial = (Material)EditorGUILayout.ObjectField("Preview Line Material", _previewLineMaterial, typeof(Material), false);

        GUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = _points.Count > 0;
            if (GUILayout.Button("Undo Last")) { UndoLast(); }
            if (GUILayout.Button("Clear")) { ClearAll(); }
            if (GUILayout.Button("Generate Mesh")) { GenerateMeshObject(); }
            GUI.enabled = true;
        }

        GUILayout.Space(8);
        EditorGUILayout.HelpBox("Scene 视图：按住 Ctrl + 左键 点击地面/碰撞体拾取点；按住中键可平移视图。按 Enter 也可生成。", MessageType.Info);
        EditorGUILayout.LabelField($"Points: {_points.Count}");
        Repaint(); // 实时刷新点数显示
    }

    // 自定义 LayerMask 字段（Editor 无内置）
    private static LayerMask LayerMaskField(string label, LayerMask selected)
    {
        var layers = InternalEditorUtility.layers;
        var layerNumbers = new int[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            layerNumbers[i] = LayerMask.NameToLayer(layers[i]);

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Length; i++)
            if (((selected.value >> layerNumbers[i]) & 1) == 1)
                maskWithoutEmpty |= 1 << i;

        int newMaskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers);
        int newMask = 0;
        for (int i = 0; i < layerNumbers.Length; i++)
            if ((newMaskWithoutEmpty & (1 << i)) != 0)
                newMask |= 1 << layerNumbers[i];

        selected.value = newMask;
        return selected;
    }

    // ===== Scene 交互 =====
    private void OnSceneGUI(SceneView sv)
    {
        Event e = Event.current;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        // 画已选点 & 线
        DrawPreview();

        // Ctrl+左键 拾取
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) &&
            e.button == 0 && e.control)
        {
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector3 hitPos;
            Vector3 hitNormal;
            if (RaycastPick(ray, out hitPos, out hitNormal))
            {
                AddPoint(hitPos, hitNormal);
                e.Use();
            }
        }

        // Enter 生成
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            GenerateMeshObject();
            e.Use();
        }
    }

    private bool RaycastPick(Ray ray, out Vector3 pos, out Vector3 normal)
    {
        if (_conformToSurface && Physics.Raycast(ray, out var hit, 10000f, _hitMask.value, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point;
            normal = hit.normal;
            return true;
        }
        // 不贴合表面：投到 y=0 的 XZ 平面
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float t))
        {
            pos = ray.GetPoint(t);
            normal = Vector3.up;
            return true;
        }
        pos = Vector3.zero; normal = Vector3.up;
        return false;
    }

    private void AddPoint(Vector3 p, Vector3 n)
    {
        Undo.RecordObject(this, "Add Polygon Point");
        _points.Add(p);
        _normals.Add(n);
        ComputeBasis(); // 更新平面
    }

    private void UndoLast()
    {
        if (_points.Count == 0) return;
        Undo.RecordObject(this, "Remove Polygon Point");
        _points.RemoveAt(_points.Count - 1);
        _normals.RemoveAt(_normals.Count - 1);
        ComputeBasis();
    }

    private void ClearAll()
    {
        Undo.RecordObject(this, "Clear Polygon Points");
        _points.Clear();
        _normals.Clear();
    }

    private void DrawPreview()
    {
        if (_points.Count == 0) return;

        // 点
        for (int i = 0; i < _points.Count; i++)
        {
            Handles.color = Color.cyan;
            Handles.SphereHandleCap(0, _points[i], Quaternion.identity, HandleUtility.GetHandleSize(_points[i]) * _handleSize, EventType.Repaint);
        }

        // 线
        if (_previewLineMaterial) _previewLineMaterial.SetPass(0);
        Handles.color = Color.green;
        for (int i = 0; i < _points.Count - 1; i++)
        {
            Handles.DrawLine(_points[i], _points[i + 1], 2f);
        }
        if (_closeLoopPreview && _points.Count >= 3)
        {
            Handles.DrawLine(_points[^1], _points[0], 2f);
        }
    }

    // ===== 核心：生成 Mesh =====
    private void GenerateMeshObject()
    {
        if (_points.Count < 3)
        {
            EditorUtility.DisplayDialog("Polygon", "至少需要 3 个点。", "OK");
            return;
        }

        // 1) 计算平面基（用于投影到2D三角化 & 法线/UV）
        ComputeBasis();

        // 2) 生成顶点：是否拉平到平均高度
        Vector3[] verts = new Vector3[_points.Count];
        if (_flattenToAverageHeight)
        {
            float sumY = 0f;
            for (int i = 0; i < _points.Count; i++) sumY += _points[i].y;
            float avgY = sumY / _points.Count;
            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i];
                verts[i] = new Vector3(p.x, avgY, p.z); // 拉平 Y
            }
        }
        else
        {
            for (int i = 0; i < _points.Count; i++)
                verts[i] = _points[i];
        }

        // 3) 投影到 2D（耳切三角化）
        var poly2D = new List<Vector2>(_points.Count);
        for (int i = 0; i < _points.Count; i++)
        {
            poly2D.Add(WorldToPlane2D(_points[i]));
        }

        var indices = TriangulateEarClipping(poly2D);
        if (indices == null || indices.Length < 3)
        {
            EditorUtility.DisplayDialog("Triangulate Failed", "三角化失败，请检查点序是否自交或重合。", "OK");
            return;
        }

        // 4) 法线
        var norms = new Vector3[_points.Count];
        for (int i = 0; i < norms.Length; i++)
        {
            // 拉平时统一用平面法线，避免凹凸法线导致阴影瑕疵
            norms[i] = _basisN;
        }

        // 5) UV（平面投影）
        var uvs = new Vector2[_points.Count];
        for (int i = 0; i < uvs.Length; i++)
        {
            var uv = WorldToPlane2D(verts[i]); // 注意：用最终顶点（已拉平）投影
            uvs[i] = uv / Mathf.Max(1e-4f, _uvScale);
        }

        // 6) 创建 Mesh 对象
        var go = new GameObject(string.IsNullOrEmpty(_meshObjectName) ? "PolygonMesh" : _meshObjectName);
        Undo.RegisterCreatedObjectUndo(go, "Create Polygon Mesh");

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mc = go.AddComponent<MeshCollider>();

        var mesh = new Mesh();
        mesh.name = go.name + "_Mesh";
        mesh.indexFormat = (verts.Length > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = indices;
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        // 如需材质，用户自行赋值；这里不强制设置
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // ===== 几何工具：平面基 + 投影 =====
    private void ComputeBasis()
    {
        if (_points.Count < 3)
        {
            _basisOrigin = Vector3.zero;
            _basisN = Vector3.up;
            _basisT = Vector3.right;
            _basisB = Vector3.forward;
            return;
        }

        // 原点 = 质心
        _basisOrigin = Vector3.zero;
        foreach (var p in _points) _basisOrigin += p;
        _basisOrigin /= _points.Count;

        // 法线 = Newell 法（对任意简单多边形鲁棒）
        Vector3 n = Vector3.zero;
        for (int i = 0; i < _points.Count; i++)
        {
            var cur = _points[i];
            var nxt = _points[(i + 1) % _points.Count];
            n.x += (cur.y - nxt.y) * (cur.z + nxt.z);
            n.y += (cur.z - nxt.z) * (cur.x + nxt.x);
            n.z += (cur.x - nxt.x) * (cur.y + nxt.y);
        }
        if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
        _basisN = n.normalized;

        // 切向：尽量投影世界右到平面；共线则换一个
        _basisT = Vector3.ProjectOnPlane(Vector3.right, _basisN).normalized;
        if (_basisT.sqrMagnitude < 1e-6f)
            _basisT = Vector3.ProjectOnPlane(Vector3.forward, _basisN).normalized;
        _basisB = Vector3.Cross(_basisN, _basisT).normalized;
    }

    private Vector2 WorldToPlane2D(Vector3 p)
    {
        var d = p - _basisOrigin;
        float u = Vector3.Dot(d, _basisT);
        float v = Vector3.Dot(d, _basisB);
        return new Vector2(u, v);
    }

    // ===== 简单耳切三角化（无孔） =====
    private static int[] TriangulateEarClipping(List<Vector2> poly)
    {
        // 拷贝索引
        int n = poly.Count;
        if (n < 3) return null;

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);

        // 面朝向
        bool ccw = PolygonArea(poly) > 0f;
        var tris = new List<int>((n - 2) * 3);

        int guard = 0;
        while (idx.Count > 3 && guard++ < 100000)
        {
            bool earFound = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int i0 = idx[(i + idx.Count - 1) % idx.Count];
                int i1 = idx[i];
                int i2 = idx[(i + 1) % idx.Count];

                Vector2 a = poly[i0];
                Vector2 b = poly[i1];
                Vector2 c = poly[i2];

                if (!IsConvex(a, b, c, ccw)) continue;
                bool anyInside = false;
                for (int j = 0; j < idx.Count; j++)
                {
                    int k = idx[j];
                    if (k == i0 || k == i1 || k == i2) continue;
                    if (PointInTriangle(poly[k], a, b, c))
                    {
                        anyInside = true; break;
                    }
                }
                if (anyInside) continue;

                // 取一个耳朵
                if (ccw) { tris.Add(i0); tris.Add(i1); tris.Add(i2); }
                else { tris.Add(i2); tris.Add(i1); tris.Add(i0); }
                idx.RemoveAt(i);
                earFound = true;
                break;
            }
            if (!earFound) break; // 失败（自交/重复点）
        }

        if (idx.Count == 3)
        {
            if (PolygonArea(poly) > 0f) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            else { tris.Add(idx[2]); tris.Add(idx[1]); tris.Add(idx[0]); }
        }

        return tris.Count >= 3 ? tris.ToArray() : null;
    }

    private static float PolygonArea(List<Vector2> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            a += (double)p[i].x * p[j].y - (double)p[j].x * p[i].y;
        }
        return (float)(a * 0.5);
    }

    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c, bool ccw)
    {
        float cross = Cross(b - a, c - b);
        return ccw ? cross > 0f : cross < 0f;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross(b - a, p - a);
        float c2 = Cross(c - b, p - b);
        float c3 = Cross(a - c, p - c);
        bool hasNeg = (c1 < 0) || (c2 < 0) || (c3 < 0);
        bool hasPos = (c1 > 0) || (c2 > 0) || (c3 > 0);
        return !(hasNeg && hasPos);
    }
}
#endif
