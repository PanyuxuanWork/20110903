using System;
using UnityEngine;

/// <summary>
/// ID:2B
/// [A15-A12][A11-A8][A8-A0] :  BuildingType,SubBuildingType,ID
/// </summary>

[CreateAssetMenu(fileName = "CustomAsset",menuName = "BuildAsset")]
[Serializable]
public class BuildAsset : ScriptableObject
{
    public BuildMajor buildMajor;
    public BuildMinor buildMinor;
    public byte ID;
    public GameObject buildingPrefab;
    public GameObject ghostPrefab;
    public Vector2Int size;
}
