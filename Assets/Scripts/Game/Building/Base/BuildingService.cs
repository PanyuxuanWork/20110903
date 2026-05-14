using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

public struct HitInfo
{
    public Vector3 Pos;
    public BuildAsset asset;
    public Area area;
}

public class BuildCheckContext
{
    public Area cArea;
    public Vector3 cPos;
    public BuildAsset asset;
    public string msg;
}

/// <summary>
/// 建造系统服务（自定义 Layer：buildMask=允许建造的 passableType 集合）
/// 规则：仅当格子的 grid.passableType[idx] 被包含在 buildMask 中，并且不为 7（占用），才允许建造（显示绿色）。
/// </summary>
[RequireComponent(typeof(BuildingCheckBeforePlace))]
public class BuildingService : MonoSingleton<BuildingService>
{
    [Header("输入与交互")]
    public KeyCode placeKey = KeyCode.Mouse0;      // 左键放置
    public KeyCode cancelKey = KeyCode.Escape;     // 取消/退出
    public KeyCode rotateLeftKey = KeyCode.Q;      // 左转 90°
    public KeyCode rotateRightKey = KeyCode.E;     // 右转 90°

    [Tooltip("拾取未命中时，会回退到与 y=yLevel 的水平面相交，不依赖任何 Collider")]
    public float yLevel;// 放置高度（XZ 平面）

    [Header("Ghost 外观反馈")]
    public bool tintGhostByValidity = true;        // 根据合法性着色 ghost
    public Color validColor = new Color(0f, 1f, 0f, 0.35f);
    public Color invalidColor = new Color(1f, 0f, 0f, 0.35f);
    public Color gridValidColor = new Color(0f, 1f, 0f, 0.15f);
    public Color gridInvalidColor = new Color(1f, 0f, 0f, 0.15f);

    [Header("其它")]
    public bool pauseGlobalStepWhilePlacing = true;

    // ---------------- 状态对外只读 ----------------
    public bool IsPlacing { get; private set; }
    public BuildAsset CurrentBuild => _selectedBuild;
    public GridAsset CurrentGrid => _grid;

    // ---------------- 全局事件（可多处订阅） ----------------
    public event Action<bool> OnBuildingModeChanged;           // true=进入，false=退出
    public event Action<BuildAsset, HitInfo> OnBeforePlaceGlobal;  // 放置前
    public event Action<BuildAsset, HitInfo> OnAfterPlaceGlobal;   // 放置后
    private readonly List<Func<BuildCheckContext,bool>> _globalPlaceCheckFunc=new();

    // 运行态
    private GridAsset _grid;
    private BuildAsset _selectedBuild;
    private GameObject _ghost;
    private int _ghostRotationSteps;          // 0..3 -> 0/90/180/270
    private int _hoverCellIndex = -1;
    private byte[] _buildMaskSet;             // 允许建造的 passableType 值集合
    private bool _isBatch;

    // 一次性回调（来自本次 StartBuilding 的参数）
    private Action<BuildAsset, int> _oneShotBeforePlace;
    private Action<BuildAsset, int> _oneShotAfterPlace;

    #region 外部API

    public void SelectBuild(BuildAsset asset)
    {
        if (asset == null) { TLog.Error("SelectBuild 传入的 BuildAsset 为空"); return; }
        _selectedBuild = asset;
    }

    public bool StartBuilding(BuildAsset asset, Action<BuildAsset, int> beforeAction)
    {
        return StartBuilding(asset, null, beforeAction);
    }

    /// <summary>
    /// 外部调用，开始建造
    /// </summary>
    /// <param StepName="build">建筑资产</param>
    /// <param StepName="grid">网格资产</param>
    /// <param StepName="buildMask">可建造掩码</param>
    /// <param StepName="OnBeforePlace">放之前调用</param>
    /// <param StepName="OnAfterPlace">放置后调用</param>
    /// <param StepName="isBatch">是否进行批量建造</param>
    /// <returns></returns>
    public bool StartBuilding(BuildAsset build, byte[] buildMask = null,
                              Action<BuildAsset, int> OnBeforePlace = null,
                              Action<BuildAsset, int> OnAfterPlace = null,
                              bool isBatch = false)
    {
        if (build == null)
        {
            TLog.Error(this, "StartBuilding 失败：BuildAsset 为空");
            return false;
        }

        // —— 强制重启 —— //
        if (IsPlacing) ExitBuildingMode();

        if (buildMask == null || buildMask.Length == 0)
        {
            buildMask = BuildingMask.Road;
        }

        _selectedBuild = build;
        _buildMaskSet = buildMask;     // 允许的 passableType 集合
        _oneShotBeforePlace = OnBeforePlace;
        _oneShotAfterPlace = OnAfterPlace;
        _isBatch = isBatch;

        _ghostRotationSteps = 0;
        _hoverCellIndex = -1;

        if (pauseGlobalStepWhilePlacing && GlobalStep.Instance != null)
        {
            GlobalStep.Instance.Pause(true);
            TLog.Log(this, "进入建造模式并暂停 GlobalStep");
        }

        // 生成 ghost（优先 ghostPrefab）
        GameObject ghostPrefab = _selectedBuild.ghostPrefab != null ? _selectedBuild.ghostPrefab : _selectedBuild.buildingPrefab;
        if (ghostPrefab == null)
        {
            TLog.Error(this, "StartBuilding 失败：BuildAsset 缺少 ghostPrefab/buildingPrefab");
            return false;
        }
        _ghost = Instantiate(ghostPrefab);
        _ghost.name = $"GHOST_{ghostPrefab.name}";
        SetGhostTint(invalidColor);

        IsPlacing = true;
        OnBuildingModeChanged?.Invoke(true);
        return true;
    }

    /// <summary>退出建造模式。</summary>
    public void ExitBuildingMode()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;

        IsPlacing = false;
        _hoverCellIndex = -1;
        _oneShotBeforePlace = null;
        _oneShotAfterPlace = null;

        if (pauseGlobalStepWhilePlacing && GlobalStep.Instance != null)
        {
            GlobalStep.Instance.Pause(false);
            TLog.Log(this, "退出建造模式并恢复 GlobalStep");
        }
        else
        {
            TLog.Log(this, "退出建造模式");
        }
        OnBuildingModeChanged?.Invoke(false);
    }



    #endregion

    protected override void Awake()
    {
        base.Awake();
        IsPlacing = false;
    }

    private IEnumerator Start()
    {
        yield return null;
    }

    private void Update()
    {
        if (!IsPlacing) return;
        UpdateGhostPoseAndHoverIndex();
        HandleRotateAndConfirm();
    }

    #region 内部实现
    private void UpdateGhostPoseAndHoverIndex()
    {
        Vector3 world;
        if (!TryGetMouseWorld(out world))
        {
            _hoverCellIndex = -1;
            SetGhostTint(invalidColor);
            Debug.Log("未获取到有效的鼠标世界坐标，Ghost 设置为无效颜色");
            return;
        }

        var hitGrid = AreaContext.Instance.FindGridAssetByVector3(world);
        if (hitGrid == null)
        {
            _grid = null;
        }
        if (hitGrid != null && _grid != hitGrid)
        {
            _grid = hitGrid;
            yLevel = world.y; // 更新放置高度
            Debug.Log($"找到新的网格：{_grid.name}");
        }

        int x, z;
        if (_grid.WorldToCell(world, out x, out z))
        {
            int idx = _grid.ToIndex(x, z);
            _hoverCellIndex = idx;

            Vector3 center = _grid.IndexToWorldCenter(idx);
            center.y = world.y;
            ApplyGhostTRS(center, Quaternion.Euler(0f, 90f * _ghostRotationSteps, 0f));

            // 预判合法性 + 可视化
            List<int> footprint = CollectFootprintIndicesCentered(idx, _selectedBuild.size, _ghostRotationSteps);
            string reason;
            bool valid = ValidateFootprint(footprint, out reason);
            SetGhostTint(valid ? validColor : invalidColor);
            DrawFootprint(footprint);

        }
        else
        {
            Debug.Log("未命中有效格子");
            _hoverCellIndex = -1;
            SetGhostTint(invalidColor);
        }
    }


    private bool TryGetMouseWorld(out Vector3 world)
    {
        Camera cam = Camera.main;
        world = default;
        if (cam == null)
        {
            Debug.LogError("没有找到主相机！");
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // 1) 试图命中任意 Collider（不使用 Unity Layer）
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 10000f, 1 << 12))
        {

            world = hit.point;
            return true;
        }

        // 2) 回退：与 y=yLevel 的水平面相交
        /* Plane plane = new Plane(Vector3.up, new Vector3(0f, yLevel, 0f));
         float dist;
         if (plane.Raycast(ray, out dist))
         {
             world = ray.GetPoint(dist);
             world.y = yLevel;
             Debug.Log($"命中水平面，位置：{world}");
             return true;
         }*/

        Debug.LogError("没有命中任何有效位置！");
        return false;
    }


    private void HandleRotateAndConfirm()
    {
        if (Input.GetKeyDown(rotateLeftKey))
        {
            _ghostRotationSteps = (_ghostRotationSteps + 3) & 3; // -1 mod 4
            TLog.Log(this, $"旋转：{_ghostRotationSteps * 90}°");
        }
        else if (Input.GetKeyDown(rotateRightKey))
        {
            _ghostRotationSteps = (_ghostRotationSteps + 1) & 3; // +1 mod 4
            TLog.Log(this, $"旋转：{_ghostRotationSteps * 90}°");
        }

        if (Input.GetKeyDown(placeKey))
        {
            bool ok = PlaceBuilding(_selectedBuild);
            if (!ok) TLog.Warning(this, "放置失败，原因见上方日志");
        }
        if (Input.GetKeyDown(cancelKey))
        {
            ExitBuildingMode();
        }
    }

    private bool PlaceBuilding(BuildAsset asset)
    {

        // 确保当前处于建造模式
        if (!IsPlacing)
        {
            TLog.Warning(this, "PlaceBuilding 调用时不在建造模式");
            Debug.LogWarning("尝试放置建筑时，不在建造模式！");
            return false;
        }

        // 检查建筑和网格是否有效
        if (asset == null || _grid == null)
        {
            TLog.Error(this, "PlaceBuilding 失败：BuildAsset 或 GridAsset 为空");
            Debug.LogError("放置失败：BuildAsset 或 GridAsset 为空！");
            return false;
        }

        // 确保鼠标指向了有效的格子
        if (_hoverCellIndex < 0)
        {
            TLog.Warning(this, "PlaceBuilding 失败：鼠标未指向有效格");
            Debug.LogWarning("放置失败：鼠标未指向有效格！");
            return false;
        }

        // 获取建筑占用的格子
        List<int> footprint = CollectFootprintIndicesCentered(_hoverCellIndex, asset.size, _ghostRotationSteps);
        string reason;
        if (!ValidateFootprint(footprint, out reason))
        {
            SetGhostTint(invalidColor);
            TLog.Warning(this, $"放置非法：{reason}");
            Debug.LogWarning($"放置非法：{reason}");
            return false;
        }

        


        // 计算放置位置和旋转角度
        Vector3 worldPos = _grid.IndexToWorldCenter(_hoverCellIndex);
        worldPos.y = yLevel;
        Quaternion rot = Quaternion.Euler(0f, 90f * _ghostRotationSteps, 0f);

        var area = AreaContext.Instance.FindAreaByVector3(worldPos);

        //外部可建造判断检测
        BuildCheckContext context = new BuildCheckContext()
        {
            cArea = area,
            cPos = worldPos,
            asset = asset
        };
        if (!CheckBeforePlace(context))
        {
            return false;
        }
        
        if (area == null)
        {
            TLog.Error(this, $"{asset.bname}放止位置非法，位置:{worldPos}");
        }

        // 确保建筑物的 prefab 存在
        if (asset.buildingPrefab == null)
        {
            TLog.Error(this, "PlaceBuilding 失败：BuildAsset.buildingPrefab 为空");
            Debug.LogError("放置失败：BuildAsset.buildingPrefab 为空");
            return false;
        }

        HitInfo info = new HitInfo()
        {
            Pos = worldPos,
            area = area,
            asset = asset
        };

        // --- 放置前的操作（一次性 + 全局） ---
        try
        {
            _oneShotBeforePlace?.Invoke(asset, _hoverCellIndex);
            OnBeforePlaceGlobal?.Invoke(asset, info);
        }
        catch (Exception e)
        {
            TLog.Error(this, $"OnBeforePlace 执行异常：{e}");
            return false;
        }

        // 实例化建筑物
        GameObject go = Instantiate(asset.buildingPrefab, worldPos, rot);
        go.name = $"Building_{asset.buildingPrefab.name}";

        // 注册建筑物
        RegisterBuilding(asset, _grid, go);

        // 更新网格状态，标记占用的格子
        for (int i = 0; i < footprint.Count; i++)
        {
            int idx = footprint[i];
            _grid.passableType[idx] = 7;  // 7表示占用
        }

        TLog.Log(this, $"放置成功：占用 {footprint.Count} 格，中心Index={_hoverCellIndex}，旋转={_ghostRotationSteps * 90}°");

        // --- 放置后的操作（一次性 + 全局） ---
        try
        {
            _oneShotAfterPlace?.Invoke(asset, _hoverCellIndex);
            OnAfterPlaceGlobal?.Invoke(asset, info);
        }
        catch (Exception e)
        {
            TLog.Error(this, $"OnAfterPlace 执行异常：{e}");
        }

        // 批量建造或单次建造
        if (_isBatch)
        {
            SetGhostTint(validColor); // 继续放置
        }
        else
        {
            ExitBuildingMode();       // 单次：放置后退出建造模式
        }

        return true;
    }



    private void ApplyGhostTRS(Vector3 pos, Quaternion rot)
    {
        if (_ghost == null) return;
        _ghost.transform.SetPositionAndRotation(pos, rot);
    }

    private void SetGhostTint(Color c)
    {
        if (!tintGhostByValidity || _ghost == null) return;
        Renderer[] renderers = _ghost.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", c);
                else if (r.sharedMaterial.HasProperty("_Color")) mpb.SetColor("_Color", c);
            }
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>以中心锚定计算 footprint（旋转 90/270° 仅交换宽高）。</summary>
    private List<int> CollectFootprintIndicesCentered(int centerIndex, Vector2Int size, int rotSteps)
    {
        int w = _grid.Width;
        int h = _grid.Height;
        if (w <= 0 || h <= 0) return new List<int>(0);

        int sx = size.x;
        int sz = size.y;
        if ((rotSteps & 1) == 1)
        {
            (sx, sz) = (sz, sx);
        }

        int cx = centerIndex % w;
        int cz = centerIndex / w;

        int startX = cx - (sx >> 1);
        int startZ = cz - (sz >> 1);

        List<int> list = new List<int>(sx * sz);
        for (int dz = 0; dz < sz; dz++)
        {
            for (int dx = 0; dx < sx; dx++)
            {
                int gx = startX + dx;
                int gz = startZ + dz;
                if (gx < 0 || gx >= w || gz < 0 || gz >= h) list.Add(-1);
                else list.Add(_grid.ToIndex(gx, gz));
            }
        }
        return list;
    }

    /// <summary>
    /// 校验 footprint。
    /// 规则：1) 越界禁止；2) passableType == 7（占用）禁止；
    ///      3) 如果提供了 buildMask（允许集合），则 passableType 必须包含在 buildMask 中；
    ///      4) 如果未提供 buildMask，则一律视为不允许（按你的最新要求严格执行）
    /// </summary>
    private bool ValidateFootprint(List<int> footprint, out string reason)
    {
        reason = null;
        if (footprint == null || footprint.Count == 0)
        {
            reason = "未能计算 footprint";
            return false;
        }
        if (_buildMaskSet == null || _buildMaskSet.Length == 0)
        {
            reason = "buildMask 未提供或为空（未指定允许的 passableType 集合）";
            return false;
        }

        for (int i = 0; i < footprint.Count; i++)
        {
            int idx = footprint[i];
            if (idx < 0 || idx >= _grid.passableType.Length)
            {
                reason = $"越界 idx={idx}";
                return false;
            }

            byte p = _grid.passableType[idx];

            // 7 = 已占用，直接禁止
            if (p == 7)
            {
                reason = $"已被占用 idx={idx} (passableType=7)";
                return false;
            }

            // 必须在允许集合中
            if (!ContainsPassable(_buildMaskSet, p))
            {
                reason = $"不在允许集合内 idx={idx} (passableType={p})";
                return false;
            }
        }
        return true;
    }

    /// <summary>绘制 footprint 的格线：绿=允许（p 在 buildMask 且 !=7），红=禁止。</summary>
    private void DrawFootprint(List<int> footprint)
    {
        if (footprint == null) return;

        for (int i = 0; i < footprint.Count; i++)
        {
            int idx = footprint[i];
            if (idx < 0 || idx >= _grid.passableType.Length) continue;

            Vector3 c = _grid.IndexToWorldCenter(idx);
            c.y = yLevel;

            byte p = _grid.passableType[idx];
            bool cellValid = (_buildMaskSet != null && _buildMaskSet.Length > 0 &&
                              p != 7 && ContainsPassable(_buildMaskSet, p));

            Color color = cellValid ? gridValidColor : gridInvalidColor;
            Vector3 a = c + Vector3.left * 0.5f + Vector3.back * 0.5f;
            Vector3 b = c + Vector3.left * 0.5f + Vector3.forward * 0.5f;
            Vector3 d = c + Vector3.right * 0.5f + Vector3.forward * 0.5f;
            Vector3 e = c + Vector3.right * 0.5f + Vector3.back * 0.5f;
            Debug.DrawLine(a, b, color);
            Debug.DrawLine(b, d, color);
            Debug.DrawLine(d, e, color);
            Debug.DrawLine(e, a, color);
        }
    }

    // 工具：判断 passableType 是否在允许集合中（byte 数组当成集合）
    private static bool ContainsPassable(byte[] set, byte value)
    {
        // 集合很短时线性扫描最快（陆地/海洋/两栖几种）
        for (int i = 0; i < set.Length; i++)
            if (set[i] == value) return true;
        return false;
    }

    private bool RegisterBuilding(BuildAsset asset, GridAsset grid, GameObject go)
    {
        foreach (var v in AreaContext.Instance.Areas)
        {
            if (v.grid == grid)
            {
                return v.RegisterNewBuilding(asset, go);
            }
        }
        return false;
    }


    #endregion


    #region Check

    private bool CheckBeforePlace(BuildCheckContext c)
    {
        foreach (var v in _globalPlaceCheckFunc)
        {
            try
            {
                if (!v.Invoke(c))
                {
                    UILog.Instance.ShowError(this,c.msg);
                    return false;
                }
            }
            catch (Exception e)
            {
                TLog.Error(this,$"放置前检测异常，异常信息{e.Message}");
                throw;
            }
        }
        return true;
    }

    public void RegisterBeforeAction(Func<BuildCheckContext,bool> f)
    {
        _globalPlaceCheckFunc.Add(f);
    }

    #endregion
}
