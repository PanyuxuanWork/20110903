/***************************************************************************
// File       : RegionMaskPainter.cs
// Author     : Panyuxuan
// Created    : 2026/03/08
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sirenix.OdinInspector;
using UnityEngine;

public enum RegionChannel
{
    R = 0,   // 工业区
    G = 1,   // 居住区
    B = 2,   // 商业区
    A = 3    // 预留区
}

public class RegionMaskPainter : MonoBehaviour
{
    [Header("Refs")]
    public Camera TargetCamera;
    public Terrain TargetTerrain;

    [Tooltip("可留空，留空时自动取 TargetTerrain.materialTemplate")]
    public Material SourceTerrainMaterial;

    [Header("Mask")]
    [Min(16)] public int MaskResolution = 512;
    public FilterMode FilterMode = FilterMode.Point;

    [Header("Region Visual")]
    [SerializeField] private float _regionBorderFeather = 0.02f;
    public float RegionBorderFeather
    {
        get => _regionBorderFeather;
        set
        {
            value = Mathf.Clamp(value, 0.001f, 0.3f);
            if (Mathf.Approximately(_regionBorderFeather, value)) return;
            _regionBorderFeather = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private float _regionBorderMin = 0.08f;
    public float RegionBorderMin
    {
        get => _regionBorderMin;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_regionBorderMin, value)) return;
            _regionBorderMin = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private float _regionBorderMax = 0.18f;
    public float RegionBorderMax
    {
        get => _regionBorderMax;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_regionBorderMax, value)) return;
            _regionBorderMax = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private float _regionBorderOpacity = 0.92f;
    public float RegionBorderOpacity
    {
        get => _regionBorderOpacity;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_regionBorderOpacity, value)) return;
            _regionBorderOpacity = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private float _regionBorderDarken = 0.55f;
    public float RegionBorderDarken
    {
        get => _regionBorderDarken;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_regionBorderDarken, value)) return;
            _regionBorderDarken = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private float _regionOverlayOpacity = 0.72f;
    public float RegionOverlayOpacity
    {
        get => _regionOverlayOpacity;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_regionOverlayOpacity, value)) return;
            _regionOverlayOpacity = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private Color _regionTintR = new Color(1f, 0.18f, 0.18f, 1f);
    public Color RegionTintR
    {
        get => _regionTintR;
        set
        {
            if (_regionTintR == value) return;
            _regionTintR = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private Color _regionTintG = new Color(0.35f, 0.85f, 0.35f, 1f);
    public Color RegionTintG
    {
        get => _regionTintG;
        set
        {
            if (_regionTintG == value) return;
            _regionTintG = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private Color _regionTintB = new Color(0.25f, 0.55f, 1.0f, 1f);
    public Color RegionTintB
    {
        get => _regionTintB;
        set
        {
            if (_regionTintB == value) return;
            _regionTintB = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private Color _regionTintA = new Color(0.95f, 0.85f, 0.25f, 1f);
    public Color RegionTintA
    {
        get => _regionTintA;
        set
        {
            if (_regionTintA == value) return;
            _regionTintA = value;
            ApplyRegionVisualParams();
        }
    }

    [SerializeField] private Color _borderTint = new Color(0.95f, 0.85f, 0.25f, 1f);
    public Color BorderTint
    {
        get => _borderTint;
        set
        {
            if (_borderTint == value) return;
            _borderTint = value;
            ApplyRegionVisualParams();
        }
    }
    [HideInInspector]public Texture2D RegionMaskTexture => _regionMask;
    private static readonly int RegionDisplayChannelId = Shader.PropertyToID("_RegionDisplayChannel");

    [Header("Brush")]
    public RegionChannel CurrentChannel = RegionChannel.R;

    [Range(1f, 256f)] public float BrushRadiusWorld = 32f;
    [Range(0.01f, 5f)] public float BrushStrength = 1f;
    [Range(0.01f, 1f)] public float BrushSoftness = 0.5f;

    [Tooltip("新画一个区域时，是否压掉其它区域通道")]
    public bool OverwriteOtherChannels = true;

    [Tooltip("压掉其它通道的强度倍率，1 表示与当前通道增长同速")]
    [Range(0f, 2f)] public float OverwriteStrength = 1f;

    [Header("Hotkeys")]
    public bool EnableChannelHotkeys = true;

    [Header("Debug")]
    public bool VerboseLog = false;

    private Texture2D _regionMask;
    private Color[] _pixels;

    private TerrainData _terrainData;
    private Vector3 _terrainPos;
    private Vector3 _terrainSize;
    private TerrainCollider _terrainCollider;

    private Material _runtimeMaterial;
    private Material _originalTerrainMaterial;

    private static readonly int RegionMaskId = Shader.PropertyToID("_RegionMask");
    private static readonly int RegionWorldMinId = Shader.PropertyToID("_RegionWorldMin");
    private static readonly int RegionWorldSizeId = Shader.PropertyToID("_RegionWorldSize");
    private static readonly int RegionOverlayOpacityId = Shader.PropertyToID("_RegionOverlayOpacity");
    private static readonly int RegionBorderOpacityId = Shader.PropertyToID("_RegionBorderOpacity");
    private static readonly int RegionBorderDarkenId = Shader.PropertyToID("_RegionBorderDarken");
    private static readonly int RegionBorderMinId = Shader.PropertyToID("_RegionBorderMin");
    private static readonly int RegionBorderMaxId = Shader.PropertyToID("_RegionBorderMax");
    private static readonly int RegionBorderFeatherId = Shader.PropertyToID("_RegionBorderFeather");
    private static readonly int RegionTintRId = Shader.PropertyToID("_RegionTintR");
    private static readonly int RegionTintGId = Shader.PropertyToID("_RegionTintG");
    private static readonly int RegionTintBId = Shader.PropertyToID("_RegionTintB");
    private static readonly int RegionTintAId = Shader.PropertyToID("_RegionTintA");
    private static readonly int RegionBorderTint = Shader.PropertyToID("_RegionBorderTintGlobal");

    private void Awake()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterial();
        DestroyRuntimeResources();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        ApplyRegionVisualParams();
    }

    private void Update()
    {
        HandleHotkeys();

        if (Input.GetMouseButton(0))
            PaintFromMouse(false);
        else if (Input.GetMouseButton(1))
            PaintFromMouse(true);
    }

    private void Initialize()
    {
        if (TargetTerrain == null)
            TargetTerrain = Terrain.activeTerrain;

        if (TargetCamera == null)
            TargetCamera = Camera.main;

        if (TargetTerrain == null)
        {
            Debug.LogError("[RegionMaskPainter] TargetTerrain is null.");
            enabled = false;
            return;
        }

        if (TargetCamera == null)
        {
            Debug.LogError("[RegionMaskPainter] TargetCamera is null.");
            enabled = false;
            return;
        }

        _terrainData = TargetTerrain.terrainData;
        _terrainPos = TargetTerrain.transform.position;
        _terrainSize = _terrainData.size;
        _terrainCollider = TargetTerrain.GetComponent<TerrainCollider>();

        if (_terrainCollider == null)
        {
            Debug.LogError("[RegionMaskPainter] TerrainCollider missing.");
            enabled = false;
            return;
        }

        Material baseMat = SourceTerrainMaterial != null
            ? SourceTerrainMaterial
            : TargetTerrain.materialTemplate;

        if (baseMat == null)
        {
            Debug.LogError("[RegionMaskPainter] No terrain material found.");
            enabled = false;
            return;
        }

        _originalTerrainMaterial = TargetTerrain.materialTemplate;
        _runtimeMaterial = new Material(baseMat);
        _runtimeMaterial.name = baseMat.name + " (Runtime Region Painter)";
        TargetTerrain.materialTemplate = _runtimeMaterial;

        CreateMask();
        BindShaderParams();
        ApplyRegionVisualParams();

        if (VerboseLog)
        {
            Debug.Log($"[RegionMaskPainter] Init OK. Terrain={TargetTerrain.name}, Material={_runtimeMaterial.name}");
        }
    }

    private void RestoreOriginalMaterial()
    {
        if (TargetTerrain != null)
            TargetTerrain.materialTemplate = _originalTerrainMaterial;
    }

    private void DestroyRuntimeResources()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
        }

        if (_regionMask != null)
        {
            Destroy(_regionMask);
            _regionMask = null;
        }

        _pixels = null;
    }

    private void HandleHotkeys()
    {
        if (!EnableChannelHotkeys)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            CurrentChannel = RegionChannel.R;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            CurrentChannel = RegionChannel.G;

        if (Input.GetKeyDown(KeyCode.Alpha3))
            CurrentChannel = RegionChannel.B;

        if (Input.GetKeyDown(KeyCode.Alpha4))
            CurrentChannel = RegionChannel.A;
    }

    private void CreateMask()
    {
        _regionMask = new Texture2D(MaskResolution, MaskResolution, TextureFormat.RGBA32, false, true);
        _regionMask.wrapMode = TextureWrapMode.Clamp;
        _regionMask.filterMode = FilterMode;

        _pixels = new Color[MaskResolution * MaskResolution];
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = Color.black;

        _regionMask.SetPixels(_pixels);
        _regionMask.Apply(false, false);
    }

    private void BindShaderParams()
    {
        if (_runtimeMaterial == null)
            return;

        _runtimeMaterial.SetTexture(RegionMaskId, _regionMask);
        _runtimeMaterial.SetVector(RegionWorldMinId, new Vector4(_terrainPos.x, _terrainPos.z, 0f, 0f));
        _runtimeMaterial.SetVector(RegionWorldSizeId, new Vector4(_terrainSize.x, _terrainSize.z, 0f, 0f));
    }

    [Button]
    public void ApplyRegionVisualParams()
    {
        if (_runtimeMaterial == null)
            return;

        _runtimeMaterial.SetFloat(RegionOverlayOpacityId, _regionOverlayOpacity);
        _runtimeMaterial.SetFloat(RegionBorderOpacityId, _regionBorderOpacity);
        _runtimeMaterial.SetFloat(RegionBorderDarkenId, _regionBorderDarken);
        _runtimeMaterial.SetFloat(RegionBorderMinId, _regionBorderMin);
        _runtimeMaterial.SetFloat(RegionBorderMaxId, _regionBorderMax);
        _runtimeMaterial.SetFloat(RegionBorderFeatherId, _regionBorderFeather);

        _runtimeMaterial.SetColor(RegionTintRId, _regionTintR);
        _runtimeMaterial.SetColor(RegionTintGId, _regionTintG);
        _runtimeMaterial.SetColor(RegionTintBId, _regionTintB);
        _runtimeMaterial.SetColor(RegionTintAId, _regionTintA);

        _runtimeMaterial.SetFloat(RegionDisplayChannelId, (float)CurrentChannel);
        _runtimeMaterial.SetColor(RegionBorderTint, BorderTint);
    }

    private void PaintFromMouse(bool erase)
    {
        Ray ray = TargetCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 10000f))
            return;

        if (hit.collider != _terrainCollider)
            return;

        Vector2 uv = WorldToMaskUV(hit.point);
        PaintCircle(uv, BrushRadiusWorld, BrushStrength, BrushSoftness, CurrentChannel, erase);
    }

    private Vector2 WorldToMaskUV(Vector3 worldPos)
    {
        float u = Mathf.InverseLerp(_terrainPos.x, _terrainPos.x + _terrainSize.x, worldPos.x);
        float v = Mathf.InverseLerp(_terrainPos.z, _terrainPos.z + _terrainSize.z, worldPos.z);
        return new Vector2(u, v);
    }

    private void PaintCircle(Vector2 uv, float radiusWorld, float strength, float softness, RegionChannel channel, bool erase)
    {
        int centerX = Mathf.RoundToInt(uv.x * (MaskResolution - 1));
        int centerY = Mathf.RoundToInt(uv.y * (MaskResolution - 1));

        float radiusPxX = radiusWorld / Mathf.Max(_terrainSize.x, 0.0001f) * MaskResolution;
        float radiusPxY = radiusWorld / Mathf.Max(_terrainSize.z, 0.0001f) * MaskResolution;

        int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusPxX));
        int maxX = Mathf.Min(MaskResolution - 1, Mathf.CeilToInt(centerX + radiusPxX));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusPxY));
        int maxY = Mathf.Min(MaskResolution - 1, Mathf.CeilToInt(centerY + radiusPxY));

        float inner01 = Mathf.Clamp01(1f - softness);
        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float nx = (x - centerX) / Mathf.Max(radiusPxX, 0.0001f);
                float ny = (y - centerY) / Mathf.Max(radiusPxY, 0.0001f);
                float dist01 = Mathf.Sqrt(nx * nx + ny * ny);

                if (dist01 > 1f)
                    continue;

                float falloff = dist01 <= inner01
                    ? 1f
                    : 1f - Mathf.InverseLerp(inner01, 1f, dist01);

                float delta = strength * falloff * Time.deltaTime * 20f;
                int idx = y * MaskResolution + x;
                Color c = _pixels[idx];
                Color before = c;

                if (erase)
                {
                    SetChannel(ref c, channel, Mathf.Max(0f, GetChannel(c, channel) - delta));
                }
                else
                {
                    if (OverwriteOtherChannels)
                    {
                        float eraseOthers = delta * OverwriteStrength;

                        switch (channel)
                        {
                            case RegionChannel.R:
                                c.g = Mathf.Max(0f, c.g - eraseOthers);
                                c.b = Mathf.Max(0f, c.b - eraseOthers);
                                c.a = Mathf.Max(0f, c.a - eraseOthers);
                                c.r = Mathf.Min(1f, c.r + delta);
                                break;

                            case RegionChannel.G:
                                c.r = Mathf.Max(0f, c.r - eraseOthers);
                                c.b = Mathf.Max(0f, c.b - eraseOthers);
                                c.a = Mathf.Max(0f, c.a - eraseOthers);
                                c.g = Mathf.Min(1f, c.g + delta);
                                break;

                            case RegionChannel.B:
                                c.r = Mathf.Max(0f, c.r - eraseOthers);
                                c.g = Mathf.Max(0f, c.g - eraseOthers);
                                c.a = Mathf.Max(0f, c.a - eraseOthers);
                                c.b = Mathf.Min(1f, c.b + delta);
                                break;

                            case RegionChannel.A:
                                c.r = Mathf.Max(0f, c.r - eraseOthers);
                                c.g = Mathf.Max(0f, c.g - eraseOthers);
                                c.b = Mathf.Max(0f, c.b - eraseOthers);
                                c.a = Mathf.Min(1f, c.a + delta);
                                break;
                        }
                    }
                    else
                    {
                        SetChannel(ref c, channel, Mathf.Min(1f, GetChannel(c, channel) + delta));
                    }
                }

                if (c != before)
                {
                    _pixels[idx] = c;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _regionMask.SetPixels(_pixels);
            _regionMask.Apply(false, false);

            if (VerboseLog)
                Debug.Log($"[RegionMaskPainter] Painted Channel={channel}");
        }
    }

    private static float GetChannel(Color c, RegionChannel channel)
    {
        switch (channel)
        {
            case RegionChannel.R: return c.r;
            case RegionChannel.G: return c.g;
            case RegionChannel.B: return c.b;
            case RegionChannel.A: return c.a;
            default: return 0f;
        }
    }

    private static void SetChannel(ref Color c, RegionChannel channel, float value)
    {
        switch (channel)
        {
            case RegionChannel.R: c.r = value; break;
            case RegionChannel.G: c.g = value; break;
            case RegionChannel.B: c.b = value; break;
            case RegionChannel.A: c.a = value; break;
        }
    }

    [Button("Clear Mask")]
    public void ClearMask()
    {
        if (_pixels == null || _regionMask == null)
            return;

        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = Color.black;

        _regionMask.SetPixels(_pixels);
        _regionMask.Apply(false, false);

        if (VerboseLog)
            Debug.Log("[RegionMaskPainter] Mask cleared.");
    }
}