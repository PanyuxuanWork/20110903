using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

// [扩展] Editor 下绘制带高度的网格（由 MeshCollider 表面插值获得）
public class BuildableAreaPreview : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Sources (MeshColliders)")]
    public List<MeshCollider> Sources = new List<MeshCollider>();

    [Header("Grid Settings")]
    public float CellWidth = 1.0f;
    public bool SnapOriginToCell = true;     // 原点对齐到 cell 宽
    public bool OnlyDrawBuildable = true;    // 只绘制落在区域内的格子
    [Tooltip("每次最多显示 Length×Length 个格子。窗口连续，中心优先鼠标悬停；否则取 SceneView 相机中心；再不行回退到网格中心。")]
    public int Length = 64;

    [Header("Height Preview")]
    [Tooltip("是否计算并以高度绘制（Y 来自 MeshCollider 三角面插值）。")]
    public bool EnableHeight = true;

    public enum HeightPickMode { Max, Min, Average, First }
    [Tooltip("多个三角面覆盖同一投影时的高度选择策略。")]
    public HeightPickMode PickMode = HeightPickMode.Max;

    [Range(0f, 1f), Tooltip("忽略法线朝上程度不足的三角面，避免墙体等竖直面干扰。建议 0.2~0.6。")]
    public float MinUpDot = 0.3f;

    [Tooltip("绘制时整体抬升，避免与几何体共面产生闪烁。")]
    public float HeightVisualOffset = 0.01f;

    [Tooltip("绘制时角点高度是否单独采样（更平滑），否则使用格子中心高度。")]
    public bool SampleCornersForDraw = true;

    [Header("Colors")]
    public Color CellFill = new Color(0f, 1f, 0f, 0.08f);
    public Color CellOutline = new Color(0f, 0.9f, 0f, 0.6f);
    public Color HoverFill = new Color(1f, 1f, 0f, 0.25f);
    public Color HoverOutline = new Color(1f, 1f, 0f, 0.95f);

    // ―― 预览用的网格数据（动态缓存）――
    [HideInInspector] public Vector2 OriginXZ;
    [ReadOnly] public int Width, Height;
    [HideInInspector] public byte[] Buildable; // index = x + Width * z
    [ReadOnly, Tooltip("与 Buildable 同长度；EnableHeight 时填充。")]
    public float[] HeightY;

    // 变更检测
    private int _lastHash;
    private int _frameThrottle;

    // ―― 对外：窗口或脚本可调用 ―― //
    public void AddSource(MeshCollider mc)
    {
        if (mc != null && !Sources.Contains(mc)) Sources.Add(mc);
        RebuildNow();
    }

    public void RemoveNulls()
    {
        for (int i = Sources.Count - 1; i >= 0; i--)
            if (Sources[i] == null) Sources.RemoveAt(i);
    }

    public void RebuildNow()
    {
        RemoveNulls();
        RebuildInternal();
        SceneView.RepaintAll();
    }

    private void OnValidate()
    {
        if (CellWidth < 1e-4f) CellWidth = 1e-4f;
        if (Length < 1) Length = 1;
        RebuildNow();
    }

    private void Update()
    {
        // 每几帧检查下是否有变更（移动/缩放网格会导致 bounds 变）
        _frameThrottle++;
        if (_frameThrottle % 10 != 0) return;

        int h = ComputeStateHash();
        if (h != _lastHash)
        {
            _lastHash = h;
            RebuildInternal();
        }
    }

    private int ComputeStateHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 23 + Sources.Count;
            for (int i = 0; i < Sources.Count; i++)
            {
                MeshCollider mc = Sources[i];
                if (mc == null) continue;
                Bounds b = mc.bounds;
                h = h * 23 + mc.GetInstanceID();
                h = h * 23 + b.min.GetHashCode();
                h = h * 23 + b.max.GetHashCode();
                h = h * 23 + mc.transform.localToWorldMatrix.GetHashCode();
            }
            h = h * 23 + CellWidth.GetHashCode();
            h = h * 23 + SnapOriginToCell.GetHashCode();
            h = h * 23 + Length.GetHashCode();
            // 高度相关设置变化也触发重建（高度数组需要重算）
            h = h * 23 + EnableHeight.GetHashCode();
            h = h * 23 + MinUpDot.GetHashCode();
            h = h * 23 + PickMode.GetHashCode();
            return h;
        }
    }

    private void RebuildInternal()
    {
        if (Sources.Count == 0)
        {
            Width = Height = 0;
            Buildable = null;
            HeightY = null;
            return;
        }

        // 1) 世界XZ AABB
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < Sources.Count; i++)
        {
            MeshCollider mc = Sources[i];
            if (mc == null || mc.sharedMesh == null) continue;
            Bounds wb = mc.bounds;
            if (wb.min.x < minX) minX = wb.min.x;
            if (wb.min.z < minZ) minZ = wb.min.z;
            if (wb.max.x > maxX) maxX = wb.max.x;
            if (wb.max.z > maxZ) maxZ = wb.max.z;
        }
        if (float.IsInfinity(minX) || float.IsInfinity(minZ))
        {
            Width = Height = 0; Buildable = null; HeightY = null; return;
        }

        // 2) 原点/宽高
        OriginXZ = new Vector2(minX, minZ);
        if (SnapOriginToCell)
        {
            OriginXZ.x = Mathf.Floor(OriginXZ.x / CellWidth) * CellWidth;
            OriginXZ.y = Mathf.Floor(OriginXZ.y / CellWidth) * CellWidth;
        }
        Width = Mathf.Max(1, Mathf.CeilToInt((maxX - OriginXZ.x) / CellWidth));
        Height = Mathf.Max(1, Mathf.CeilToInt((maxZ - OriginXZ.y) / CellWidth));

        // 3) 收集世界三角形（含 Y 和 UpDot）
        List<TriXZ> tris = CollectWorldTrisXZ(Sources);

        // 4) 栅格化：中心点在三角形内则可建造=1，同时插值出高度
        int total = Width * Height;
        if (Buildable == null || Buildable.Length != total) Buildable = new byte[total];
        System.Array.Clear(Buildable, 0, total);

        if (EnableHeight)
        {
            if (HeightY == null || HeightY.Length != total) HeightY = new float[total];
            System.Array.Clear(HeightY, 0, total);
        }
        else
        {
            HeightY = null;
        }

        for (int z = 0; z < Height; z++)
        {
            float cz = OriginXZ.y + (z + 0.5f) * CellWidth;
            for (int x = 0; x < Width; x++)
            {
                float cx = OriginXZ.x + (x + 0.5f) * CellWidth;

                float y;
                bool inside;
                if (EnableHeight)
                {
                    inside = TrySampleHeight(cx, cz, tris, MinUpDot, PickMode, out y);
                    if (inside)
                    {
                        int idx = x + Width * z;
                        Buildable[idx] = 1;
                        HeightY[idx] = y;
                    }
                }
                else
                {
                    inside = PointInMeshXZ(cx, cz, tris);
                    if (inside)
                    {
                        int idx = x + Width * z;
                        Buildable[idx] = 1;
                    }
                }
            }
        }
    }

    private struct TriXZ
    {
        public Vector2 A, B, C;   // XZ
        public float Ay, By, Cy;  // 对应 Y
        public float MinX, MaxX, MinZ, MaxZ;
        public float UpDot;       // 法线与世界 Up 的点积（朝上为正）
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

            Matrix4x4 l2w = mc.transform.localToWorldMatrix;
            Vector3[] vtx = mesh.vertices;
            int[] idx = mesh.triangles;

            int triCount = idx.Length / 3;
            for (int ti = 0; ti < triCount; ti++)
            {
                int ia = idx[3 * ti + 0], ib = idx[3 * ti + 1], ic = idx[3 * ti + 2];
                Vector3 wa = l2w.MultiplyPoint3x4(vtx[ia]);
                Vector3 wb = l2w.MultiplyPoint3x4(vtx[ib]);
                Vector3 wc = l2w.MultiplyPoint3x4(vtx[ic]);

                Vector3 n = Vector3.Cross(wb - wa, wc - wa);
                float upDot = n.sqrMagnitude > 1e-16f ? Vector3.Dot(n.normalized, Vector3.up) : 0f;

                TriXZ t;
                t.A = new Vector2(wa.x, wa.z); t.B = new Vector2(wb.x, wb.z); t.C = new Vector2(wc.x, wc.z);
                t.Ay = wa.y; t.By = wb.y; t.Cy = wc.y;
                t.MinX = Mathf.Min(t.A.x, Mathf.Min(t.B.x, t.C.x));
                t.MaxX = Mathf.Max(t.A.x, Mathf.Max(t.B.x, t.C.x));
                t.MinZ = Mathf.Min(t.A.y, Mathf.Min(t.B.y, t.C.y));
                t.MaxZ = Mathf.Max(t.A.y, Mathf.Max(t.B.y, t.C.y));
                t.UpDot = upDot;
                list.Add(t);
            }
        }
        return list;
    }

    private static bool PointInMeshXZ(float px, float pz, List<TriXZ> tris)
    {
        for (int i = 0; i < tris.Count; i++)
        {
            TriXZ t = tris[i];
            if (px < t.MinX || px > t.MaxX || pz < t.MinZ || pz > t.MaxZ) continue;
            if (PointInTri2D(px, pz, t.A, t.B, t.C)) return true;
        }
        return false;
    }

    private static float Cross(Vector2 u, Vector2 v) { return u.x * v.y - u.y * v.x; }

    private static bool PointInTri2D_Barycentric(
        float px, float pz, in Vector2 A, in Vector2 B, in Vector2 C,
        out float w0, out float w1, out float w2)
    {
        float area = Cross(B - A, C - A);
        if (Mathf.Abs(area) < 1e-9f) { w0 = w1 = w2 = 0f; return false; }
        Vector2 P = new Vector2(px, pz);
        w0 = Cross(B - P, C - P) / area;
        w1 = Cross(C - P, A - P) / area;
        w2 = Cross(A - P, B - P) / area;
        const float eps = -1e-5f;
        return w0 >= eps && w1 >= eps && w2 >= eps;
    }

    // 采样 (px,pz) 处的高度：对覆盖该点且 UpDot>=阈值的所有三角面做插值后，按策略选取
    private static bool TrySampleHeight(float px, float pz, List<TriXZ> tris, float minUpDot, HeightPickMode mode, out float y)
    {
        bool found = false;
        y = 0f;

        float bestMax = float.NegativeInfinity;
        float bestMin = float.PositiveInfinity;
        double sum = 0;
        int cnt = 0;

        for (int i = 0; i < tris.Count; i++)
        {
            TriXZ t = tris[i];
            if (t.UpDot < minUpDot) continue;                                // 法线过滤
            if (px < t.MinX || px > t.MaxX || pz < t.MinZ || pz > t.MaxZ) continue;

            float w0, w1, w2;
            if (!PointInTri2D_Barycentric(px, pz, t.A, t.B, t.C, out w0, out w1, out w2)) continue;

            float yh = w0 * t.Ay + w1 * t.By + w2 * t.Cy;
            if (!found)
            {
                y = yh; found = true;
                bestMax = yh; bestMin = yh; sum = yh; cnt = 1;
                if (mode == HeightPickMode.First) return true;
            }
            else
            {
                if (mode == HeightPickMode.Max) { if (yh > bestMax) { bestMax = yh; y = bestMax; } }
                else if (mode == HeightPickMode.Min) { if (yh < bestMin) { bestMin = yh; y = bestMin; } }
                else if (mode == HeightPickMode.Average) { sum += yh; cnt++; y = (float)(sum / cnt); }
            }
        }
        return found;
    }

    private static bool PointInTri2D(float px, float pz, Vector2 A, Vector2 B, Vector2 C)
    {
        float w0, w1, w2;
        return PointInTri2D_Barycentric(px, pz, A, B, C, out w0, out w1, out w2);
    }

    // ―― Scene 可视化（连续窗口 + GUI 叠加文字）―― //
    private void OnDrawGizmos()
    {
        if (Buildable == null || Buildable.Length == 0 || Width <= 0 || Height <= 0) return;

        int hoverX = -1, hoverZ = -1;
        Vector3 hit;
        if (TryRayToSurface(out hit))
        {
            int x = (int)Mathf.Floor((hit.x - OriginXZ.x) / CellWidth);
            int z = (int)Mathf.Floor((hit.z - OriginXZ.y) / CellWidth);
            if ((uint)x < (uint)Width && (uint)z < (uint)Height) { hoverX = x; hoverZ = z; }
        }

        int len = Mathf.Clamp(Length, 1, Mathf.Min(Width, Height));
        int cx = hoverX, cz = hoverZ;

        if (cx < 0 || cz < 0)
        {
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                Vector3 camCenter = ProjectCameraCenterToPlane(sv.camera, 0f);
                cx = (int)Mathf.Floor((camCenter.x - OriginXZ.x) / CellWidth);
                cz = (int)Mathf.Floor((camCenter.z - OriginXZ.y) / CellWidth);
            }
        }
        if (cx < 0 || cz < 0)
        {
            cx = Width / 2;
            cz = Height / 2;
        }

        int half = len / 2;
        int startX = Mathf.Clamp(cx - half, 0, Mathf.Max(0, Width - len));
        int startZ = Mathf.Clamp(cz - half, 0, Mathf.Max(0, Height - len));
        int endX = startX + len - 1;
        int endZ = startZ + len - 1;

        // —— 先准备 GUI 文本信息，但“暂不绘制”，只记录 —— 
        bool hasHoverLabel = false;
        Rect hoverLabelRect = default;
        string hoverLabelText = string.Empty;

        // 若需要角点高度采样，准备局部三角缓存（避免重算）
        List<TriXZ> tris = null;
        if (EnableHeight && SampleCornersForDraw && (tris == null))
            tris = CollectWorldTrisXZ(Sources); // 轻量：仅在可视化阶段使用

        for (int z = startZ; z <= endZ; z++)
        {
            float z0w = OriginXZ.y + z * CellWidth;
            float z1w = z0w + CellWidth;

            for (int x = startX; x <= endX; x++)
            {
                int idx = x + Width * z;
                bool build = Buildable[idx] != 0;
                if (OnlyDrawBuildable && !build) continue;

                float x0w = OriginXZ.x + x * CellWidth;
                float x1w = x0w + CellWidth;

                // 计算绘制用高度
                float centerH = 0f;
                if (EnableHeight && HeightY != null && idx < HeightY.Length) centerH = HeightY[idx];

                float h00 = centerH, h10 = centerH, h11 = centerH, h01 = centerH;
                if (EnableHeight && SampleCornersForDraw && tris != null)
                {
                    // 角点（略向内缩一点，降低边界抖动）
                    float eps = Mathf.Min(0.001f, CellWidth * 0.02f);
                    TrySampleHeight(x0w + eps, z0w + eps, tris, MinUpDot, PickMode, out h00);
                    TrySampleHeight(x1w - eps, z0w + eps, tris, MinUpDot, PickMode, out h10);
                    TrySampleHeight(x1w - eps, z1w - eps, tris, MinUpDot, PickMode, out h11);
                    TrySampleHeight(x0w + eps, z1w - eps, tris, MinUpDot, PickMode, out h01);

                    // fallback：若某角采样失败，则回落到中心高度
                    if (float.IsNaN(h00) || h00 == 0f) h00 = centerH;
                    if (float.IsNaN(h10) || h10 == 0f) h10 = centerH;
                    if (float.IsNaN(h11) || h11 == 0f) h11 = centerH;
                    if (float.IsNaN(h01) || h01 == 0f) h01 = centerH;
                }
                else
                {
                    h00 = h10 = h11 = h01 = centerH;
                }

                Vector3 p0 = new Vector3(x0w, h00 + HeightVisualOffset, z0w);
                Vector3 p1 = new Vector3(x1w, h10 + HeightVisualOffset, z0w);
                Vector3 p2 = new Vector3(x1w, h11 + HeightVisualOffset, z1w);
                Vector3 p3 = new Vector3(x0w, h01 + HeightVisualOffset, z1w);

                if (x == hoverX && z == hoverZ)
                {
                    Handles.DrawSolidRectangleWithOutline(new Vector3[] { p0, p1, p2, p3 }, HoverFill, HoverOutline);

                    Vector3 worldCenter = (p0 + p2) * 0.5f;
                    Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldCenter);
                    hoverLabelRect = new Rect(guiPoint.x - 60f, guiPoint.y - 10f, 120f, 20f);
                    string hText = EnableHeight ? $" H={centerH:F3}" : "";
                    hoverLabelText = $"X={x}, Y={z}{hText}";
                    hasHoverLabel = true;
                }
                else
                {
                    Handles.DrawSolidRectangleWithOutline(new Vector3[] { p0, p1, p2, p3 }, CellFill, CellOutline);
                }
            }
        }

        // —— 最后一步：统一绘制 GUI（始终盖在最上层）——
#if UNITY_EDITOR
        if (hasHoverLabel)
        {
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.red;
            labelStyle.hover.textColor = Color.red;
            labelStyle.active.textColor = Color.red;
            labelStyle.focused.textColor = Color.red;

            GUIStyle shadowStyle = new GUIStyle(labelStyle);
            shadowStyle.normal.textColor = Color.black;
            shadowStyle.hover.textColor = Color.black;
            shadowStyle.active.textColor = Color.black;
            shadowStyle.focused.textColor = Color.black;

            Handles.BeginGUI();
            GUI.Label(new Rect(hoverLabelRect.x + 1, hoverLabelRect.y + 1, hoverLabelRect.width, hoverLabelRect.height), hoverLabelText, shadowStyle);
            GUI.Label(hoverLabelRect, hoverLabelText, labelStyle);
            Handles.EndGUI();
        }
#endif
    }


private bool TryRayToSurface(out Vector3 hitWorld)
{
    hitWorld = Vector3.zero;
    var sv = SceneView.lastActiveSceneView;
    if (sv == null || Event.current == null) return false;

    Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

    // 1) 逐个 MeshCollider 精确 Raycast（与层无关，直接打 collider）
    float best = float.PositiveInfinity;
    bool hit = false;
    for (int i = 0; i < Sources.Count; i++)
    {
        var mc = Sources[i];
        if (mc == null || !mc.enabled || !mc.gameObject.activeInHierarchy) continue;

        RaycastHit rh;
        if (mc.Raycast(ray, out rh, 1e6f) && rh.distance < best)
        {
            best = rh.distance;
            hitWorld = rh.point;
            hit = true;
        }
    }
    if (hit) return true;

    // 2) 回退：若开启了高度，尝试在“估计的地面高度”所在平面求交（减少视差）
    if (EnableHeight)
    {
        // 先与 y=0 求一次交，拿到一个大致的 xz，再用你的采样函数估一个高度
        Plane p0 = new Plane(Vector3.up, Vector3.zero);
        float d0;
        if (p0.Raycast(ray, out d0))
        {
            Vector3 guess = ray.origin + ray.direction * d0;
            var tris = CollectWorldTrisXZ(Sources);
            float y;
            if (TrySampleHeight(guess.x, guess.z, tris, MinUpDot, PickMode, out y))
            {
                Plane pH = new Plane(Vector3.up, new Vector3(0f, y, 0f));
                float dH;
                if (pH.Raycast(ray, out dH))
                {
                    hitWorld = ray.origin + ray.direction * dH;
                    return true;
                }
            }
        }
    }

    // 3) 最终回退：y=0 平面
    {
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        float d;
        if (plane.Raycast(ray, out d))
        {
            hitWorld = ray.origin + ray.direction * d;
            return true;
        }
    }

    return false;
}

    private static Vector3 ProjectCameraCenterToPlane(Camera cam, float planeY)
    {
        Ray r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        float d;
        Vector3 p = r.origin;
        if (plane.Raycast(r, out d)) p = r.origin + r.direction * d; else p.y = planeY;
        return p;
    }

    private static Vector3[] GetFrustumXZCorners(Camera cam, float planeY)
    {
        Vector3[] outCorners = new Vector3[4];
        Vector3[] viewport = { new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1), new Vector3(0, 1) };
        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        for (int i = 0; i < 4; i++)
        {
            Ray r = cam.ViewportPointToRay(viewport[i]);
            float d; Vector3 p = r.origin;
            if (plane.Raycast(r, out d)) p = r.origin + r.direction * d; else p.y = planeY;
            outCorners[i] = p;
        }
        return outCorners;
    }

    // ====== Export ======
#if UNITY_EDITOR
    [Header("Export")]
    [FolderPath(AbsolutePath = false)]
    public string DefaultSaveFolder = "Assets";  // 可选：默认保存目录（Project 相对路径）

    /// <summary>
    /// 将当前预览的规则网格导出为 GridAsset ScriptableObject 并保存到工程。
    /// </summary>
    [Button("Create GridAsset From Preview"), ContextMenu("Create GridAsset From Preview")]
    public void CreateGridAssetFromPreview()
    {
        // 1) 基本校验
        if (Buildable == null || Buildable.Length == 0 || Width <= 0 || Height <= 0)
        {
            EditorUtility.DisplayDialog("Create GridAsset", "当前没有有效的栅格数据（请先确保 Sources 正确并已生成）。", "OK");
            return;
        }

        // 2) 选择保存路径
        if (string.IsNullOrEmpty(DefaultSaveFolder)) DefaultSaveFolder = "Assets";
        if (!AssetDatabase.IsValidFolder(DefaultSaveFolder))
        {
            DefaultSaveFolder = "Assets";
        }

        string fileName = $"GridAsset_{name}_{Width}x{Height}.asset";
        string path = EditorUtility.SaveFilePanelInProject(
            "Save GridAsset",
            fileName,
            "asset",
            "选择保存 GridAsset 的路径",
            DefaultSaveFolder
        );
        if (string.IsNullOrEmpty(path)) return; // 用户取消

        // 3) 创建并填充 GridAsset
        GridAsset asset = ScriptableObject.CreateInstance<GridAsset>();
        asset.CellWidth = CellWidth;
        asset.OriginXZ = OriginXZ;
        asset.Width = Width;
        asset.Height = Height;

        // 拷贝数组
        asset.passableType = new byte[Buildable.Length];
        System.Array.Copy(Buildable, asset.passableType, Buildable.Length);

        // 若 GridAsset 中存在 heightY 字段，则同步写入（你之前已添加）
        try
        {
            var f = typeof(GridAsset).GetField("heightY");
            if (f != null && EnableHeight && HeightY != null && HeightY.Length == Buildable.Length)
            {
                float[] hy = new float[HeightY.Length];
                System.Array.Copy(HeightY, hy, HeightY.Length);
                f.SetValue(asset, hy);
            }
        }
        catch { /* 反射失败则忽略，不影响通行数据的导出 */ }

        // 4) 写入工程并选中
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;

        Debug.Log($"[BuildableAreaPreview] GridAsset 已创建：{path}");
    }
#endif

#endif
}
