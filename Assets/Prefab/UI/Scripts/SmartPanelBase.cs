// 文件：SmartPanelBase.cs
// 功能：Panel 层的“傻瓜式”组件：ESC 关闭、点外（Panel 与 parentSafeRect 之外）关闭、Close 按钮、拖拽。
// 用法：
//   - 挂在 Panel 根节点（RectTransform）。
//   - parentSafeRect：拖一个父区域 RectTransform；点击该区域内也算安全，不会关闭。
//   - closeButton/dragHandle/cg 都是可选；不配也能跑。
//   - Show()/Hide() 控制显隐；或外部激活 GameObject 后调用 Show() 以确保入栈。
//
// 约定：
//   - 仅“栈顶”Panel 响应 ESC 和点外判断，避免多 Panel 同时被关。
//   - 若 panel/parentSafeRect 没有 Graphic，会自动补一张透明 Image 以接收点击。
//   - 拖拽时会 clamp 到父 RectTransform 边界（未设置则用 panel 的父级）。

// SmartPanel.cs — 直接挂即可用（无需继承）
// parentSafeRect：将 HUD 某父容器拖进来；点击该区域内也不会关闭。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class SmartPanel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("主体(不填=自身)")][SerializeField] RectTransform panel;
    [Header("安全父区域(可选)")][SerializeField] RectTransform parentSafeRect;
    [Header("显隐(可选)")][SerializeField] CanvasGroup cg;
    [Header("可选控件")][SerializeField] Button closeButton; [SerializeField] RectTransform dragHandle;

    [Header("行为")]
    [SerializeField] bool closeOnEsc = true;
    [SerializeField] bool closeOnLeftClick = true;
    [SerializeField] bool closeOnRightClick = true;
    [SerializeField] bool canDrag = true;
    [Header("拖拽边界(空=panel父)")][SerializeField] RectTransform dragBounds;

    public bool IsOpen { get; private set; }

    static readonly List<SmartPanel> s_stack = new List<SmartPanel>(8);
    static SmartPanel Top => s_stack.Count > 0 ? s_stack[s_stack.Count - 1] : null;

    Canvas _canvas; Camera _cam; RectTransform _panelParent;
    bool _dragging; Vector2 _lastPos;

    void Awake()
    {
        if (!panel) panel = transform as RectTransform;
        if (!dragBounds) dragBounds = panel.parent as RectTransform;
        _panelParent = panel.parent as RectTransform;

        _canvas = panel.GetComponentInParent<Canvas>();
        if (_canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) _cam = _canvas.worldCamera;

        EnsureRaycastable(panel);
        if (parentSafeRect) EnsureRaycastable(parentSafeRect);
        if (!cg) cg = GetComponent<CanvasGroup>();
        if (closeButton) closeButton.onClick.AddListener(() => Hide());
    }

    void OnEnable()
    {
        // 自动“显示+入栈”
        Show();
    }

    void OnDisable()
    {
        s_stack.Remove(this);
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (closeButton) closeButton.onClick.RemoveAllListeners();
        s_stack.Remove(this);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (cg) { cg.alpha = 1; cg.interactable = true; cg.blocksRaycasts = true; }
        s_stack.Remove(this); s_stack.Add(this); panel.SetAsLastSibling();
        IsOpen = true;
    }
    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (cg) { cg.alpha = 0; cg.interactable = false; cg.blocksRaycasts = false; }
        gameObject.SetActive(false);
    }
    public void Focus() { if (!IsOpen) return; s_stack.Remove(this); s_stack.Add(this); panel.SetAsLastSibling(); }

    void Update()
    {
        if (!IsOpen || Top != this) return;

        if (closeOnEsc && Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }

        bool left = Input.GetMouseButtonDown(0), right = Input.GetMouseButtonDown(1);
        if ((left && closeOnLeftClick) || (right && closeOnRightClick))
        {
            var sp = (Vector2)Input.mousePosition;
            if (!InSafeArea(sp)) Hide();
        }
    }

    public void OnPointerDown(PointerEventData e) { Focus(); }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!canDrag) return;
        var handle = dragHandle ? dragHandle : panel;
        if (!RectTransformUtility.RectangleContainsScreenPoint(handle, e.position, _cam)) return;
        _dragging = true; _lastPos = e.position; Focus();
    }
    public void OnDrag(PointerEventData e)
    {
        if (!_dragging) return;
        var delta = (Vector2)e.position - _lastPos; _lastPos = e.position;
        float scale = (_canvas && _canvas.scaleFactor > 0) ? _canvas.scaleFactor : 1f;
        panel.anchoredPosition += delta / scale;
        Clamp(panel, dragBounds ? dragBounds : _panelParent);
    }
    public void OnEndDrag(PointerEventData e) { _dragging = false; }

    bool InSafeArea(Vector2 sp)
    {
        if (panel && RectTransformUtility.RectangleContainsScreenPoint(panel, sp, _cam)) return true;
        if (parentSafeRect && RectTransformUtility.RectangleContainsScreenPoint(parentSafeRect, sp, _cam)) return true;
        return false;
    }

    static void Clamp(RectTransform rt, RectTransform bounds)
    {
        if (!rt || !bounds) return;
        var p = rt.anchoredPosition; var half = rt.rect.size * 0.5f; var bh = bounds.rect.size * 0.5f;
        p.x = Mathf.Clamp(p.x, -bh.x + half.x, bh.x - half.x);
        p.y = Mathf.Clamp(p.y, -bh.y + half.y, bh.y - half.y);
        rt.anchoredPosition = p;
    }
    static void EnsureRaycastable(RectTransform rt)
    {
        if (!rt) return;
        var g = rt.GetComponent<Graphic>() ?? rt.GetComponentInChildren<Graphic>(true);
        if (!g) { var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); img.raycastTarget = true; }
    }
}
