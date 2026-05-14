/***************************************************************************
// File       : ClickPickerInfo3D.cs
// Author     : Panyuxuan
// Created    : 2025/10/20
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public struct RayHit
{
    public GameObject gameObject;
    public Transform transform;
    public Collider collider;
    public Vector3 point;
    public Vector3 normal;
    public float distance;
    public int triangleIndex;   // 若有 MeshCollider 且可用
    public Vector2 textureCoord; // 若可用
    public Vector2 textureCoord2; // 若可用
}

public class ClickPickerInfo3D : MonoSingleton<ClickPickerInfo3D>
{
    [Header("Raycast Settings")]
    [SerializeField] private Camera cam;                      // 为空则用 Camera.main
    [SerializeField] private LayerMask hitLayers = ~0;        // 命中哪些层
    [SerializeField] private float maxDistance = 9999f;       // 最大射线距离
    [SerializeField] private bool ignoreUI = true;            // 点击在 UI 上时是否忽略
    [SerializeField] private QueryTriggerInteraction trigger = QueryTriggerInteraction.Ignore;
    public RayHit ClickedInfo;

    protected override void Awake()
    {
        base.Awake();
        if (!cam) cam = Camera.main;
        TLog.Log(this, "点击系统初始化已完成...");
    }

    private void Update()
    {
        if (GetClickedInfo(out ClickedInfo))
        {
            var v = ClickedInfo.gameObject.TryGetComponent(out ClickedUnit clickedUnit);
            if (v)
            {
                if (!clickedUnit.gameObject.TryGetComponent(out Resident building))
                {
                    if (clickedUnit.isFirstClick)
                        clickedUnit.isFirstClick = false;
                    else clickedUnit.OnClicked.Invoke();
                }
                else
                {
                    clickedUnit.OnClicked.Invoke();
                }
            }
        }

    }

    /// <summary>
    /// 在“鼠标左键按下”的这一帧发出一条射线，命中则返回 true，并输出 RayHit 信息。
    /// 未点击/未命中/点在UI上(且ignoreUI=true)返回 false，info 为默认值。
    /// </summary>
    public bool GetClickedInfo(out RayHit info)
    {
        info = default;

        // 仅在按下这一帧响应；若想持续检测，改成 GetMouseButton(0)
        if (!Input.GetMouseButtonDown(0))
            return false;

        // 可选：点击在 UI 上则不处理（避免穿透）
        if (ignoreUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        if (!cam) cam = Camera.main;
        if (!cam) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, hitLayers, trigger))
        {
            info.gameObject = hit.collider.gameObject;
            info.transform = hit.collider.transform;
            info.collider = hit.collider;
            info.point = hit.point;
            info.normal = hit.normal;
            info.distance = hit.distance;
            info.triangleIndex = hit.triangleIndex;  // 若非 MeshCollider 可能为 -1
            info.textureCoord = hit.textureCoord;   // 若不可用则为 (0,0)
            info.textureCoord2 = hit.textureCoord2;  // 若不可用则为 (0,0)
            return true;
        }

        return false;
    }

}
