/***************************************************************************
// File       : SmartPopupBase1.cs
// Author     : Panyuxuan
// Created    : 2025/10/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// 文件：SmartPopupBase.cs
// 用途：继承它即可获得：ESC关闭、点外关闭（点自己保留并关其它未钉住）、Pin常驻、拖拽、锚点贴附。
// 用法：
//   1) 将本脚本挂在你的弹窗根物体（RectTransform）上，或让你的弹窗控制器继承它；
//   2) 调用 OpenAt(anchorRect) 打开；点外/ESC 自动关闭（除非已 Pin）；点 X 始终关闭；
//   3) 可选：给 closeButton/pinToggle/dragHandle/cg 序列化引用；不配也能跑（会自动兜底）。
//
// 约定：
//   * panel 为空时自动使用自身 RectTransform；panel 上若无任何 Graphic，会自动补一张透明 Image 以接收点击。
//   * 支持多弹窗并存：点击某弹窗将其置顶，并关闭其它未钉住的弹窗（满足“点 a 时关 bcd”）；
//   * blocksWorldClick=true 时，自动在父节点下生成/复用全屏透明遮罩按钮；
//   * 拖拽默认在 dragHandle 上进行；若未设置 dragHandle，则整块 panel 可拖；拖后自动 clamp 到父 Rect 内。
// ***************************************************************************/

// SmartPopup.cs  — 直接挂即可用（无需继承）
// 特点：OnEnable 自动“打开+入栈”；没锚点也能跑。
// 可选：closeButton / pinToggle / dragHandle / cg。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class SmartPopup : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("--------SmartPopConfig---------")]
    [Header("主体(不填=自身)")]
     RectTransform panel;
    [Header("额外安全区(箭头/阴影等)")]
    RectTransform[] extraSafeRects;
    [Header("显隐控制(可空)")]
     CanvasGroup cg;
    [Header("可选:关闭按钮 / Pin")]
    [SerializeField] Button closeButton;
    [SerializeField] Toggle pinToggle;
    [Header("可选:拖拽手柄(空=整块可拖)")] RectTransform dragHandle;

    [Header("行为")]
    [SerializeField] bool blocksWorldClick = true;
    [SerializeField] bool closeOnLeftClick = true;
    [SerializeField] bool closeOnRightClick = true;
    [SerializeField] bool closeOnEsc = true;
    [SerializeField] bool canDrag = true;
    [SerializeField] float attachYOffset = 8f;

    public bool IsOpen { get; private set; }
    public bool IsPinned { get; private set; }
    public bool CanDrag { get => canDrag; set => canDrag = value; }

    static readonly List<SmartPopup> s_stack = new List<SmartPopup>(8);
    static Button s_maskBtn;
    static readonly List<RectTransform> s_safeBuf = new List<RectTransform>(8);
    static Vector3[] s_corners = new Vector3[4];

    Canvas _canvas; Camera _cam; RectTransform _parent;
    bool _dragging; Vector2 _lastPos;

    protected virtual void Awake()
    {
        if (!panel) panel = transform as RectTransform;
        _parent = panel.parent as RectTransform;

        _canvas = panel.GetComponentInParent<Canvas>();
        if (_canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) _cam = _canvas.worldCamera;

        // 确保可射线
        EnsureRaycastable(panel);
        if (!cg) cg = GetComponent<CanvasGroup>();

        // 自动识别 Close/Pin
        if (!closeButton)
            foreach (var b in GetComponentsInChildren<Button>(true))
                if (b.name.ToLower().Contains("close") || b.name == "X") { closeButton = b; break; }
        if (!pinToggle)
            foreach (var t in GetComponentsInChildren<Toggle>(true))
                if (t.name.ToLower().Contains("pin") || t.name.ToLower().Contains("lock") || t.name.ToLower().Contains("anchor")) { pinToggle = t; break; }

        if (closeButton) closeButton.onClick.AddListener(() => Close(force: true));
        if (pinToggle) pinToggle.onValueChanged.AddListener(SetPinned);
    }

    void OnEnable()
    {
        // —— 自动“打开 + 入栈”，无需手动调用 ——
        ShowNow();
        IsOpen = true;
        s_stack.Remove(this);
        s_stack.Add(this);
        panel.SetAsLastSibling();

        // 互斥：点自己/激活自己 → 关其它未钉住
        CloseOthersUnpinnedExcept(this);
        RefreshMaskForTop();
    }

    void OnDisable()
    {
        // 退出时从栈里拿掉
        s_stack.Remove(this);
        RefreshMaskForTop();
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (closeButton) closeButton.onClick.RemoveAllListeners();
        if (pinToggle) pinToggle.onValueChanged.RemoveAllListeners();
        s_stack.Remove(this);
        RefreshMaskForTop();
    }

    public void Close(bool force = false)
    {
        if (!IsOpen) return;
        if (IsPinned && !force) return;
        gameObject.SetActive(false); // 触发 OnDisable 清理
    }

    public void SetPinned(bool on)
    {
        IsPinned = on;
        if (pinToggle && pinToggle.isOn != on) pinToggle.isOn = on;
        RefreshMaskForTop();
    }

    void ShowNow()
    {
        gameObject.SetActive(true);
        if (cg) { cg.alpha = 1; cg.interactable = true; cg.blocksRaycasts = true; }
    }

    // —— 输入 —— //
    void Update()
    {
        if (!IsOpen || Top() != this) return;

        if (closeOnEsc && !IsPinned && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

        if (!blocksWorldClick && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            var sp = (Vector2)Input.mousePosition;
            if (!PointerInSelf(sp))
            {
                if ((Input.GetMouseButtonDown(0) && closeOnLeftClick) || (Input.GetMouseButtonDown(1) && closeOnRightClick))
                    CloseAllUnpinned();
            }
        }

        if (blocksWorldClick && Input.GetMouseButtonDown(1) && closeOnRightClick)
            CloseAllUnpinned();
    }

    public void OnPointerDown(PointerEventData e)
    {
        // 置顶并互斥
        s_stack.Remove(this);
        s_stack.Add(this);
        panel.SetAsLastSibling();
        CloseOthersUnpinnedExcept(this);
        RefreshMaskForTop();
    }

    // —— 拖拽 —— //
    public void OnBeginDrag(PointerEventData e)
    {
        if (!canDrag) return;
        var handle = dragHandle ? dragHandle : panel;
        if (!RectTransformUtility.RectangleContainsScreenPoint(handle, e.position, _cam)) return;

        _dragging = true; _lastPos = e.position;
        OnPointerDown(e); // 置顶
    }
    public void OnDrag(PointerEventData e)
    {
        if (!_dragging) return;
        var delta = (Vector2)e.position - _lastPos; _lastPos = e.position;
        float scale = (_canvas && _canvas.scaleFactor > 0) ? _canvas.scaleFactor : 1f;
        panel.anchoredPosition += delta / scale;
        ClampToParent(panel, _parent);
    }
    public void OnEndDrag(PointerEventData e) { _dragging = false; }

    // —— 工具 —— //
    static SmartPopup Top() => s_stack.Count > 0 ? s_stack[s_stack.Count - 1] : null;

    static void CloseAllUnpinned()
    {
        for (int i = s_stack.Count - 1; i >= 0; i--) { var p = s_stack[i]; if (p && p.IsOpen && !p.IsPinned) p.Close(); }
    }
    static void CloseOthersUnpinnedExcept(SmartPopup x)
    {
        for (int i = s_stack.Count - 1; i >= 0; i--) { var p = s_stack[i]; if (p && p != x && p.IsOpen && !p.IsPinned) p.Close(); }
    }

    bool PointerInSelf(Vector2 sp)
    {
        s_safeBuf.Clear();
        if (panel) s_safeBuf.Add(panel);
        if (extraSafeRects != null) for (int i = 0; i < extraSafeRects.Length; i++) if (extraSafeRects[i]) s_safeBuf.Add(extraSafeRects[i]);

        for (int i = 0; i < s_safeBuf.Count; i++)
        {
            var rt = s_safeBuf[i]; if (!rt) continue;
            var c = rt.GetComponentInParent<Canvas>();
            var cam = (c && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, sp, cam)) return true;
        }
        return false;
    }

    static void RefreshMaskForTop()
    {
        // 是否存在“需拦截”的未钉住弹窗
        bool need = false; SmartPopup top = null;
        for (int i = s_stack.Count - 1; i >= 0; i--)
        {
            var p = s_stack[i];
            if (p && p.IsOpen) { if (!p.IsPinned && p.blocksWorldClick) need = true; if (top == null) top = p; }
        }
        if (!need || top == null) { if (s_maskBtn) s_maskBtn.gameObject.SetActive(false); return; }

        EnsureMaskUnder(top._parent, top.panel.GetSiblingIndex());
        s_maskBtn.gameObject.SetActive(true);
        s_maskBtn.onClick.RemoveAllListeners();
        s_maskBtn.onClick.AddListener(() => CloseAllUnpinned());
    }

    static void EnsureMaskUnder(RectTransform parent, int panelSiblingIndex)
    {
        if (!parent) return;
        if (!s_maskBtn || !s_maskBtn.gameObject)
        {
            var go = new GameObject("__PopupMask__", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button),typeof(PopmuskLogic));
            s_maskBtn = go.GetComponent<Button>();
            var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
        }
        var rt = (RectTransform)s_maskBtn.transform;
        if (rt.parent != parent) rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        // 确保遮罩在面板“下方一层”
        int idx = Mathf.Clamp(panelSiblingIndex - 1, 0, parent.childCount - 1);
        rt.SetSiblingIndex(idx);
    }

    static void ClampToParent(RectTransform rt, RectTransform parent)
    {
        if (!rt || !parent) return;
        var p = rt.anchoredPosition; var half = rt.rect.size * 0.5f; var ph = parent.rect.size * 0.5f;
        p.x = Mathf.Clamp(p.x, -ph.x + half.x, ph.x - half.x);
        p.y = Mathf.Clamp(p.y, -ph.y + half.y, ph.y - half.y);
        rt.anchoredPosition = p;
    }

    static void EnsureRaycastable(RectTransform rt)
    {
        if (!rt) return;
        var g = rt.GetComponent<Graphic>() ?? rt.GetComponentInChildren<Graphic>(true);
        if (!g) { var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); img.raycastTarget = true; }
    }
}
