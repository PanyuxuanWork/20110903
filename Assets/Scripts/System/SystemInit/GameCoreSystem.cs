using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class GameCoreSystem : MonoBehaviour
{
    [SerializeField]
    private GameObject[] _initGO;

    private void Awake()
    {
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
}