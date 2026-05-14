/***************************************************************************
// File       : CameraController.cs
// Author     : Panyuxuan
// Created    : 2025/08/24
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] 建造类/RTS类相机移动控制
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[System.Serializable]
public class KeyBindings
{
    [Header("平面移动")]
    public KeyCode MoveLeft = KeyCode.A;
    public KeyCode MoveRight = KeyCode.D;
    public KeyCode MoveBack = KeyCode.S;
    public KeyCode MoveFwd = KeyCode.W;

    [Header("旋转")]
    public KeyCode YawLeft = KeyCode.Q;
    public KeyCode YawRight = KeyCode.E;

    [Header("升降（垂直移动）")]
    public KeyCode Lower = KeyCode.V;
    public KeyCode Raise = KeyCode.T;

    [Header("可选：加速/减速（按住生效）")]
    public KeyCode Boost = KeyCode.LeftShift;
    public KeyCode Slow = KeyCode.LeftControl;

    public KeyCode Lock = KeyCode.X;
}

[DefaultExecutionOrder(10)]
public class RTSCameraController : MonoSingleton<RTSCameraController>
{
    public bool Locked = true;
    [Header("基础引用")]
    public Camera TargetCamera;

    [Header("键位绑定（Inspector 调整）")]
    public KeyBindings Keys = new KeyBindings();

    [Header("移动与旋转")]
    public float MoveSpeed = 15f;
    public float BoostMultiplier = 1.8f;
    public float SlowMultiplier = 0.5f;
    public float MoveAcceleration = 60f;
    public float MoveDeceleration = 80f;
    public float YawSpeed = 90f;

    [Header("垂直移动灵敏度（V/T & 滚轮）")]
    [Tooltip("V/T 的基础升降速度（米/秒）")]
    public float VerticalSpeed = 20f;                 // ↑ 调大更灵
    [Tooltip("滚轮升降的放大倍数（每格滚轮相当于 VerticalSpeed 的多少倍）")]
    public float ScrollVerticalScale = 8f;            // ↑ 调大更灵

    [Header("高度限制")]
    public float MinY = 2f;
    public float MaxY = 120f;

    [Header("相机姿态（固定俯仰 + 固定臂长）")]
    public float ArmDistance = 20f;                   // 与 Rig 的距离（沿本地 -Z）
    [Range(0f, 85f)]
    public float CameraTilt = 55f;

    [Header("屏幕边缘平移")]
    public bool EdgePanEnabled = true;
    public int EdgePixels = 12;
    public float EdgePanSpeed = 15f;
    public bool IgnoreWhenPointerOverUI = true;

    [Header("Rig 碰撞（机身防穿墙）")]
    public bool UseCharacterController = true;
    public float ControllerRadius = 0.5f;
    public float ControllerHeight = 2.0f;
    public LayerMask ControllerCollisionMask = ~0;

    [Header("相机防遮挡（视角臂）")]
    public float CameraCollisionRadius = 0.3f;
    public float CollisionPadding = 0.2f;
    public LayerMask CameraCollisionMask = ~0;

    private List<Func<bool>> CameraControlCheck = new();

    // —— 内部 —— 
    private CharacterController _cc;
    private Vector3 _vel;
    private Transform _camTr;

    protected override void Awake()
    {
        base.Awake();

        if (TargetCamera == null)
            TargetCamera = GetComponentInChildren<Camera>();
        if (TargetCamera == null)
        {
            Debug.LogError("[RTSCameraController] 未找到 Camera。");
            enabled = false; return;
        }

        _camTr = TargetCamera.transform;

        // 1) 初始：相机位置 = 父物体位置（你在编辑器里摆的 CameraRig 就是初始位）
        _camTr.position = transform.position;

        // 固定俯仰角
        _camTr.localRotation = Quaternion.Euler(CameraTilt, 0f, 0f);

        if (UseCharacterController)
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();
            _cc.radius = ControllerRadius;
            _cc.height = ControllerHeight;
            _cc.center = new Vector3(0f, ControllerHeight * 0.5f, 0f);
            _cc.detectCollisions = true;
        }
        // 注意：不再在 Awake 里把相机推到臂长，首帧看到的就是父物体处

        RegisterForbidAction(BanUpdateWhenBuilding);
    }

    private void Update()
    {
        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            Locked = !Locked;
        }

        if (Locked) return;

        if (_camTr == null) return;

        if (!CanProcessInput()) return;

        float dt = Time.deltaTime;

        // —— 输入 —— 
        Vector2 planarInput = ReadPlanarMoveInput();
        float yawInput = ReadYawInput();

        // 键盘的垂直速度（V/T）
        float verticalFromKeys = ReadVerticalInput() * VerticalSpeed;

        // 2) 滚轮方向反转：上滚降低、下滚升高（注意取负号）
        float wheel = -Input.mouseScrollDelta.y; // 取负，反转方向
        float verticalFromWheel = wheel * VerticalSpeed * ScrollVerticalScale;

        float speedScale = 1f;
        if (Input.GetKey(Keys.Boost)) speedScale *= Mathf.Max(BoostMultiplier, 1f);
        if (Input.GetKey(Keys.Slow)) speedScale *= Mathf.Max(SlowMultiplier, 0.01f);

        // —— 旋转 —— 
        if (Mathf.Abs(yawInput) > 0.0001f)
        {
            float deltaYaw = yawInput * YawSpeed * dt;
            transform.Rotate(Vector3.up, deltaYaw, Space.World);
        }

        // —— 平面移动（加减速）——
        Vector3 right = ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 forward = ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 desiredVel = (right * planarInput.x + forward * planarInput.y) * (MoveSpeed * speedScale);
        _vel = SmoothVelocity(_vel, desiredVel, MoveAcceleration, MoveDeceleration, dt);
        Vector3 moveDelta = _vel * dt;

        // —— 垂直移动（键盘 + 滚轮 叠加）——
        float verticalSpeedTotal = (verticalFromKeys * speedScale) + verticalFromWheel;
        float targetY = Mathf.Clamp(transform.position.y + verticalSpeedTotal * dt, MinY, MaxY);
        moveDelta.y = targetY - transform.position.y;

        // —— 机身移动（碰撞）——
        if (UseCharacterController && _cc != null)
        {
            Physics.SyncTransforms();
            _cc.Move(moveDelta);
        }
        else
        {
            transform.position += moveDelta;
            Vector3 p = transform.position;
            p.y = Mathf.Clamp(p.y, MinY, MaxY);
            transform.position = p;
        }

        // —— 相机放置（固定臂长 + 防遮挡）——
        PositionCameraWithCollision();
    }

    private void PositionCameraWithCollision()
    {
        // 把相机放到“沿本地 -Z 的 ArmDistance”位置，若被遮挡用 SphereCast 推回来
        Vector3 pivot = transform.position;
        Vector3 desiredLocal = new Vector3(0f, 0f, -ArmDistance);
        Vector3 desiredWorld = transform.TransformPoint(desiredLocal);

        Vector3 dir = desiredWorld - pivot;
        float dist = Mathf.Max(0.001f, dir.magnitude);
        dir /= dist;

        float safeDist = dist;
        if (Physics.SphereCast(pivot, CameraCollisionRadius, dir, out RaycastHit hit, dist, CameraCollisionMask, QueryTriggerInteraction.Ignore))
        {
            safeDist = Mathf.Max(0.1f, hit.distance - CollisionPadding);
        }

        _camTr.position = pivot + dir * safeDist;
        _camTr.rotation = transform.rotation * Quaternion.Euler(CameraTilt, 0f, 0f);
    }

    // —— 输入：键盘 + 屏幕边缘 —— 
    private Vector2 ReadPlanarMoveInput()
    {
        float x = 0f, y = 0f;

        if (Input.GetKey(Keys.MoveLeft)) x -= 1f;
        if (Input.GetKey(Keys.MoveRight)) x += 1f;
        if (Input.GetKey(Keys.MoveBack)) y -= 1f;
        if (Input.GetKey(Keys.MoveFwd)) y += 1f;

        if (EdgePanEnabled && !IsPointerBlockedByUI())
        {
            Vector3 m = Input.mousePosition;
            int w = Screen.width, h = Screen.height;

            if (m.x <= EdgePixels) x -= Mathf.Clamp01((EdgePixels - m.x) / EdgePixels);
            else if (m.x >= w - EdgePixels) x += Mathf.Clamp01((m.x - (w - EdgePixels)) / EdgePixels);

            if (m.y <= EdgePixels) y -= Mathf.Clamp01((EdgePixels - m.y) / EdgePixels);
            else if (m.y >= h - EdgePixels) y += Mathf.Clamp01((m.y - (h - EdgePixels)) / EdgePixels);
        }

        Vector2 v = new Vector2(x, y);
        if (v.sqrMagnitude > 1f) v.Normalize();

        if (EdgePanEnabled && v != Vector2.zero)
            v *= (EdgePanSpeed / Mathf.Max(MoveSpeed, 0.01f));

        return v;
    }

    private float ReadYawInput()
    {
        float yaw = 0f;
        if (Input.GetKey(Keys.YawLeft)) yaw -= 1f;
        if (Input.GetKey(Keys.YawRight)) yaw += 1f;
        return yaw;
    }

    private float ReadVerticalInput()
    {
        float v = 0f;
        if (Input.GetKey(Keys.Lower)) v -= 1f;
        if (Input.GetKey(Keys.Raise)) v += 1f;
        return v;
    }

    // —— 工具 —— 
    private static Vector3 ProjectOnPlane(Vector3 v, Vector3 normal)
    {
        return v - Vector3.Dot(v, normal) * normal;
    }

    private static Vector3 SmoothVelocity(Vector3 current, Vector3 target, float accel, float decel, float dt)
    {
        Vector3 diff = target - current;
        float use = (target.sqrMagnitude > current.sqrMagnitude) ? accel : decel;
        Vector3 step = Vector3.ClampMagnitude(diff, use * dt);
        return current + step;
    }

    private bool IsPointerBlockedByUI()
    {
        if (!IgnoreWhenPointerOverUI) return false;
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }

    private bool CanProcessInput()
    {
        foreach (var v in CameraControlCheck)
        {
            if (!v.Invoke()) return false;
        }
        return true;
    }

    public void RegisterForbidAction(Func<bool> f)
    {
        CameraControlCheck.Add(f);
    }

    private bool BanUpdateWhenBuilding()
    {
        if (BuildingService.Instance&&BuildingService.Instance.IsPlacing)
        {
            return false; 
        }

        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        MinY = Mathf.Min(MinY, MaxY);
        if (TargetCamera != null)
        {
            if (_camTr == null) _camTr = TargetCamera.transform;
            if (_camTr != null) _camTr.localRotation = Quaternion.Euler(CameraTilt, 0f, 0f);
        }
        ArmDistance = Mathf.Max(0.1f, ArmDistance);
        ScrollVerticalScale = Mathf.Max(0.01f, ScrollVerticalScale);
    }
#endif
}
