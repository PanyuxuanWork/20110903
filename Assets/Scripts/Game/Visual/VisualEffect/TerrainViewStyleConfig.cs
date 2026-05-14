using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "TerrainViewStyleConfig", menuName = "Terrain View/Style Config")]
public sealed class TerrainViewStyleConfig : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        [HorizontalGroup("Row", Width = 150)]
        [LabelWidth(40)]
        public TerrainViewType type;

        [HorizontalGroup("Row")]
        [HideLabel]
        public TerrainViewStyle style;
    }

    [SerializeField]
    [TableList(AlwaysExpanded = true)]
    private List<Entry> entries = new List<Entry>();

    private Dictionary<TerrainViewType, TerrainViewStyle> lookup;

    public bool TryGetStyle(TerrainViewType type, out TerrainViewStyle style)
    {
        EnsureLookup();
        return lookup.TryGetValue(type, out style);
    }

    public Color32 ResolveColor(TerrainViewType type, TerrainViewStylePart part)
    {
        if (type == TerrainViewType.None)
            return new Color32(0, 0, 0, 0);

        if (TryGetStyle(type, out TerrainViewStyle style))
            return style.ToColor32(part);

        return new Color32(255, 0, 255, 220);
    }

    private void EnsureLookup()
    {
        if (lookup != null)
            return;

        lookup = new Dictionary<TerrainViewType, TerrainViewStyle>();
        for (int i = 0; i < entries.Count; i++)
            lookup[entries[i].type] = entries[i].style;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        lookup = null;
    }

    [Button("Reset Default Styles", ButtonSizes.Medium)]
    [GUIColor(0.4f, 0.8f, 1f)]
    private void ResetDefaultStyles()
    {
        entries.Clear();

        Add(TerrainViewType.PlayerTerritory, new Color(0.1f, 0.45f, 1f), new Color(0.25f, 0.75f, 1f));
        Add(TerrainViewType.EnemyTerritory, new Color(1f, 0.15f, 0.12f), new Color(1f, 0.45f, 0.35f));
        Add(TerrainViewType.NeutralTerritory, new Color(0.9f, 0.75f, 0.25f), new Color(1f, 0.95f, 0.45f));

        Add(TerrainViewType.Buildable, new Color(0.15f, 1f, 0.25f), new Color(0.55f, 1f, 0.55f));
        Add(TerrainViewType.Unbuildable, new Color(1f, 0.08f, 0.08f), new Color(1f, 0.45f, 0.45f));
        Add(TerrainViewType.Blocked, new Color(0.4f, 0.4f, 0.4f), new Color(0.75f, 0.75f, 0.75f));

        Add(TerrainViewType.PreviewValid, new Color(0.1f, 0.9f, 0.35f), new Color(0.65f, 1f, 0.65f));
        Add(TerrainViewType.PreviewInvalid, new Color(1f, 0.15f, 0.1f), new Color(1f, 0.65f, 0.45f));
        Add(TerrainViewType.PreviewRoad, new Color(1f, 0.72f, 0.15f), new Color(1f, 0.95f, 0.35f));

        Add(TerrainViewType.Selected, new Color(1f, 1f, 1f), new Color(1f, 1f, 1f));
        Add(TerrainViewType.Hovered, new Color(1f, 0.9f, 0.15f), new Color(1f, 1f, 0.45f));

        Add(TerrainViewType.DebugPath, new Color(0.2f, 1f, 1f), new Color(0.65f, 1f, 1f));
        Add(TerrainViewType.DebugBlocked, new Color(1f, 0f, 1f), new Color(1f, 0.5f, 1f));
        Add(TerrainViewType.DebugArea, new Color(1f, 0.5f, 0f), new Color(1f, 0.8f, 0.25f));

        lookup = null;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private void Add(TerrainViewType type, Color fill, Color border)
    {
        entries.Add(new Entry
        {
            type = type,
            style = TerrainViewStyle.Default(fill, border)
        });
    }
#endif
}
