/***************************************************************************
// File       : RegionMaskPngSaver.cs
// Author     : Panyuxuan
// Created    : 2026/03/08
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.IO;
using UnityEngine;
using System;


public class RegionMaskPngSaver : MonoBehaviour
{
    [Header("Source")]
    public RegionMaskPainter Painter;

    [Header("Save")]
    [Tooltip("为空时默认保存到 Application.persistentDataPath/RegionMasks")]
    public string SaveDirectory = "";

    [Tooltip("文件名前缀")]
    public string FilePrefix = "RegionMask";

    [Tooltip("是否自动加时间戳")]
    public bool UseTimestamp = true;

    [Header("Hotkey")]
    public bool EnableHotkey = true;
    public KeyCode SaveKey = KeyCode.P;

    private void Update()
    {
        if (!EnableHotkey)
            return;

        if (Input.GetKeyDown(SaveKey))
        {
            SavePng();
        }
    }

    [ContextMenu("Save PNG")]
    public void SavePng()
    {
        if (Painter == null)
        {
            Debug.LogWarning("[RegionMaskPngSaver] Painter is null.");
            return;
        }

        Texture2D source = Painter.RegionMaskTexture;
        if (source == null)
        {
            Debug.LogWarning("[RegionMaskPngSaver] RegionMaskTexture is null.");
            return;
        }

        string dir = GetSaveDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string fileName = BuildFileName();
        string fullPath = Path.Combine(dir, fileName);

        try
        {
            Texture2D readableCopy = CreateReadableCopy(source);
            byte[] png = readableCopy.EncodeToPNG();
            Destroy(readableCopy);

            File.WriteAllBytes(fullPath, png);
            Debug.Log($"[RegionMaskPngSaver] Saved PNG:\n{fullPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RegionMaskPngSaver] Save failed: {e}");
        }
    }

    private string GetSaveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(SaveDirectory))
            return SaveDirectory;

        return Path.Combine(Application.persistentDataPath, "RegionMasks");
    }

    private string BuildFileName()
    {
        if (!UseTimestamp)
            return $"{FilePrefix}.png";

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{FilePrefix}_{timestamp}.png";
    }

    private Texture2D CreateReadableCopy(Texture2D src)
    {
        TextureFormat format = TextureFormat.RGBA32;
        Texture2D copy = new Texture2D(src.width, src.height, format, false, true);
        copy.SetPixels(src.GetPixels());
        copy.Apply(false, false);
        return copy;
    }
}