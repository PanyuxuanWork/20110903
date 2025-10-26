using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// [TODO] 从多个 MeshCollider 生成规则网格的一维可建造数组
public class BuildableGridBakerWindow : EditorWindow
{
    private float _cellWidth = 1.0f;
    private bool _snapOriginToCell = true; // 起点对齐到cell宽
    private readonly List<MeshCollider> _sources = new List<MeshCollider>();

    [MenuItem("Tools/passableType Grid Baker (Dense 1D)")]
    public static void Open()
    {
        BuildableGridBakerWindow w = GetWindow<BuildableGridBakerWindow>("passableType Grid (1D)");
        w.minSize = new Vector2(360, 220);
        w.Focus();
    }

    private void OnGUI()
    {
        GUILayout.Label("从 MeshCollider 烘焙规则网格 (一维数组)", EditorStyles.boldLabel);

        _cellWidth = EditorGUILayout.Slider("Cell Width", _cellWidth, 0.1f, 10f);
        _snapOriginToCell = EditorGUILayout.Toggle("Snap Origin To Cell", _snapOriginToCell);

        EditorGUILayout.Space();
        if (GUILayout.Button("Add Selected MeshColliders"))
        {
            AddSelectedMeshColliders();
        }
        if (GUILayout.Button("Clear Sources")) _sources.Clear();

        EditorGUILayout.LabelField("Sources Count: " + _sources.Count);
        for (int i = 0; i < _sources.Count; i++)
        {
            EditorGUILayout.ObjectField(_sources[i], typeof(MeshCollider), true);
        }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(_sources.Count == 0);
        if (GUILayout.Button("Bake To GridAsset..."))
        {
            BakeToAsset();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "结果为规则网格：passableType 一维数组，index = x + width * z。\n" +
            "X/Z 方向格数由 MeshCollider 的世界XZ包围盒与 CellWidth 推导。",
            MessageType.Info);
    }

    private void AddSelectedMeshColliders()
    {
        Object[] selected = Selection.objects;
        for (int i = 0; i < selected.Length; i++)
        {
            GameObject go = selected[i] as GameObject;
            if (go == null) continue;
            MeshCollider[] mcs = go.GetComponentsInChildren<MeshCollider>(true);
            for (int j = 0; j < mcs.Length; j++)
            {
                MeshCollider mc = mcs[j];
                if (mc != null && mc.sharedMesh != null && !_sources.Contains(mc))
                {
                    _sources.Add(mc);
                }
            }
        }
    }

    private void BakeToAsset()
    {
        if (_sources.Count == 0)
        {
            EditorUtility.DisplayDialog("No Sources", "请先添加至少一个 MeshCollider。", "OK");
            return;
        }
        if (_cellWidth <= 0.0f)
        {
            EditorUtility.DisplayDialog("Invalid Cell Width", "CellWidth 必须 > 0。", "OK");
            return;
        }

        // 1) 计算世界 XZ AABB
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;

        for (int i = 0; i < _sources.Count; i++)
        {
            MeshCollider mc = _sources[i];
            if (mc == null || mc.sharedMesh == null) continue;
            Bounds wb = mc.bounds; // 世界空间
            if (wb.min.x < minX) minX = wb.min.x;
            if (wb.min.z < minZ) minZ = wb.min.z;
            if (wb.max.x > maxX) maxX = wb.max.x;
            if (wb.max.z > maxZ) maxZ = wb.max.z;
        }
        if (!IsFinite(minX) || !IsFinite(minZ) || !IsFinite(maxX) || !IsFinite(maxZ))
        {
            EditorUtility.DisplayDialog("Bounds Error", "无法取得有效的世界包围盒。", "OK");
            return;
        }

        // 2) 原点（左下角）
        Vector2 originXZ = new Vector2(minX, minZ);
        if (_snapOriginToCell)
        {
            originXZ.x = Mathf.Floor(originXZ.x / _cellWidth) * _cellWidth;
            originXZ.y = Mathf.Floor(originXZ.y / _cellWidth) * _cellWidth;
        }

        // 3) 推导规则网格宽高
        int cellsX = Mathf.Max(1, Mathf.CeilToInt((maxX - originXZ.x) / _cellWidth));
        int cellsZ = Mathf.Max(1, Mathf.CeilToInt((maxZ - originXZ.y) / _cellWidth));

        // 4) 收集所有源三角形的 XZ 投影
        List<TriXZ> tris = CollectWorldTrisXZ(_sources);

        // 5) 构建一维规则网格：index = x + width * z
        int total = cellsX * cellsZ;
        byte[] buildable = new byte[total]; // 默认0=不可建造

        for (int z = 0; z < cellsZ; z++)
        {
            float cz = originXZ.y + (z + 0.5f) * _cellWidth; // 格中心Z
            for (int x = 0; x < cellsX; x++)
            {
                float cx = originXZ.x + (x + 0.5f) * _cellWidth; // 格中心X

                if (PointInMeshXZ(cx, cz, tris))
                {
                    int index = x + cellsX * z; // 你要的公式
                    buildable[index] = 1;       // 区域内全部标1
                }
            }
        }

        // 6) 生成并保存 GridAsset
        string path = EditorUtility.SaveFilePanelInProject("Save GridAsset", "GridAsset_Dense", "asset", "选择保存路径");
        if (string.IsNullOrEmpty(path)) return;

        GridAsset asset = ScriptableObject.CreateInstance<GridAsset>();
        asset.CellWidth = _cellWidth;
        asset.OriginXZ = originXZ;
        asset.Width = cellsX;
        asset.Height = cellsZ;
        asset.passableType = buildable;

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(asset);

        EditorUtility.DisplayDialog("Done",
            "烘焙完成：\n" +
            $"Width={cellsX}, Height={cellsZ}\n" +
            $"Cells={total}\n" +
            $"索引公式：index = x + width * z",
            "OK");
    }

    // —— 几何/工具 —— //

    private struct TriXZ
    {
        public Vector2 A, B, C;
        public float MinX, MaxX, MinZ, MaxZ;
    }

    private static List<TriXZ> CollectWorldTrisXZ(List<MeshCollider> sources)
    {
        List<TriXZ> list = new List<TriXZ>(4096);
        for (int i = 0; i < sources.Count; i++)
        {
            MeshCollider mc = sources[i];
            if (mc == null) continue;
            Mesh mesh = mc.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) continue;

            Transform t = mc.transform;
            Matrix4x4 l2w = t.localToWorldMatrix;

            Vector3[] vtx = mesh.vertices;
            int[] idx = mesh.triangles;

            int triCount = idx.Length / 3;
            for (int ti = 0; ti < triCount; ti++)
            {
                int ia = idx[ti * 3 + 0];
                int ib = idx[ti * 3 + 1];
                int ic = idx[ti * 3 + 2];

                Vector3 wa = l2w.MultiplyPoint3x4(vtx[ia]);
                Vector3 wb = l2w.MultiplyPoint3x4(vtx[ib]);
                Vector3 wc = l2w.MultiplyPoint3x4(vtx[ic]);

                TriXZ tri = new TriXZ();
                tri.A = new Vector2(wa.x, wa.z);
                tri.B = new Vector2(wb.x, wb.z);
                tri.C = new Vector2(wc.x, wc.z);

                tri.MinX = Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x));
                tri.MaxX = Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x));
                tri.MinZ = Mathf.Min(tri.A.y, Mathf.Min(tri.B.y, tri.C.y));
                tri.MaxZ = Mathf.Max(tri.A.y, Mathf.Max(tri.B.y, tri.C.y));

                list.Add(tri);
            }
        }
        return list;
    }

    private static bool PointInMeshXZ(float px, float pz, List<TriXZ> tris)
    {
        // 按三角形AABB粗筛 + 点在三角形内（包含边界）
        for (int i = 0; i < tris.Count; i++)
        {
            TriXZ t = tris[i];
            if (px < t.MinX || px > t.MaxX || pz < t.MinZ || pz > t.MaxZ) continue;
            if (PointInTri2D(px, pz, t.A, t.B, t.C)) return true;
        }
        return false;
    }

    private static bool PointInTri2D(float px, float pz, Vector2 A, Vector2 B, Vector2 C)
    {
        float area = Cross(B - A, C - A);
        if (Mathf.Abs(area) < 1e-9f) return false;

        Vector2 P = new Vector2(px, pz);
        float w0 = Cross(B - P, C - P) / area;
        float w1 = Cross(C - P, A - P) / area;
        float w2 = Cross(A - P, B - P) / area;

        const float eps = -1e-5f; // 包含边界
        return w0 >= eps && w1 >= eps && w2 >= eps;
    }

    private static float Cross(Vector2 u, Vector2 v)
    {
        return u.x * v.y - u.y * v.x;
    }

    private static bool IsFinite(float f)
    {
        return !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
