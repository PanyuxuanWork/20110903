using UnityEngine;

/// <summary>
/// 自动创建并保证唯一的 MonoBehaviour 单例，具备退出防御机制
/// </summary>
public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _isShuttingDown = false;

    /// <summary>
    /// 当前是否正在关闭应用/切换场景中
    /// </summary>
    public static bool IsShuttingDown => _isShuttingDown;

    public static T Instance
    {
        get
        {
            if (_isShuttingDown)
            {
                Debug.LogWarning($"[MonoSingleton<{typeof(T).Name}>] 正在退出时尝试访问单例，将返回 null。");
                return null;
            }

            if (_instance == null)
            {
                // 尝试查找
                _instance = FindObjectOfType<T>();

                // 自动创建
                if (_instance == null)
                {
                    GameObject go = new GameObject(typeof(T).Name);
                    _instance = go.AddComponent<T>();

                    // 可选：防止销毁
                    // DontDestroyOnLoad(go);
                }
            }

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;

            // 可选：跨场景保留
            // DontDestroyOnLoad(this.gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning($"[MonoSingleton] {typeof(T).Name} 已存在，销毁重复实例。");
            Destroy(this.gameObject);
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _isShuttingDown = true;
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}