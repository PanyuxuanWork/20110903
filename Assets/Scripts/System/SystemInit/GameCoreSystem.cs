using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static System.Reflection.Assembly;

[DefaultExecutionOrder(-1000)]
public class GameCoreSystem : MonoSingleton<GameCoreSystem>
{
    [SerializeField]
    private GameObject[] _initGO;

    protected override void Awake()
    {
        base.Awake();
        // 若需要常驻
        DontDestroyOnLoad(gameObject);

        // 一帧内按序实例化
        if (_initGO == null) return;
        for (int i = 0; i < _initGO.Length; i++)
        {
            var src = _initGO[i];
            if (src == null) continue;

            var go = Instantiate(src, transform);

            // 显式设置层级顺序，确保与数组顺序一致
            go.transform.SetSiblingIndex(i);
        }
    }

    void Start()
    {
        var typesWithAttribute = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsSubclassOf(typeof(MonoBehaviour)) && Attribute.IsDefined(t, typeof(AutoAttached)))
            .ToList();

        foreach (var type in typesWithAttribute)
        {
            // 获取 AutoAttachToGameObjectAttribute 特性
            var attribute = (AutoAttached)Attribute.GetCustomAttribute(type, typeof(AutoAttached));

            if (attribute != null)
            {
                gameObject.AddComponent(type);
            }
        }
    }
}

 