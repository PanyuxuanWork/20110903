using System;
using System.Collections.Generic;

public static class ObPool<T> where T : class, new()
{
    // internal storage
    private static readonly Stack<T> _stack = new Stack<T>(64);
    private static readonly object _lock = new object();

    // configurable behavior
    private static Func<T> _factory = null;         // if null, new T() is used
    private static Action<T> _resetAction = null;   // called on Release before pushing
    private static int _maxSize = 1024;             // default max capacity

    /// <summary>
    /// Configure the pool behavior. Call once at startup if you want custom factory/reset or a specific maxSize.
    /// Safe to call multiple times; new config will apply to future Get/Release.
    /// </summary>
    public static void Configure(Func<T> factory = null, Action<T> reset = null, int maxSize = 1024)
    {
        if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        lock (_lock)
        {
            _factory = factory;
            _resetAction = reset;
            _maxSize = maxSize;
            // Optionally trim pool if current count exceeds new max
            while (_stack.Count > _maxSize)
            {
                _stack.Pop();
            }
        }
    }

    /// <summary>
    /// Get an instance from pool (creates new if empty).
    /// </summary>
    public static T Get()
    {
        lock (_lock)
        {
            if (_stack.Count > 0)
            {
                return _stack.Pop();
            }
        }
        // create outside lock to minimize lock time if factory is slow
        if (_factory != null) return _factory();
        return new T();
    }

    /// <summary>
    /// Try to acquire without creating; returns false if pool empty.
    /// </summary>
    public static bool TryAcquire(out T item)
    {
        lock (_lock)
        {
            if (_stack.Count > 0)
            {
                item = _stack.Pop();
                return true;
            }
            item = null;
            return false;
        }
    }

    /// <summary>
    /// Release an object back to pool. reset() will be applied if configured.
    /// If pool already at maxSize, the object will be dropped.
    /// </summary>
    public static void Release(T obj)
    {
        if (obj == null) return;

        // allow reset outside lock to avoid blocking Get/Release too long.
        var reset = _resetAction;
        if (reset != null)
        {
            try { reset(obj); }
            catch (Exception ex) { TLog.Error($"ObPool Exception : {obj} ,{ex.Message}");}
        }

        lock (_lock)
        {
            if (_stack.Count >= _maxSize)
            {
                // drop object to avoid unbounded growth
                return;
            }
            _stack.Push(obj);
        }
    }

    /// <summary>
    /// Pre-allocate up to 'count' instances into the pool (honors maxSize).
    /// </summary>
    public static void Prewarm(int count)
    {
        if (count <= 0) return;
        var list = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            if (_factory != null) list.Add(_factory()); else list.Add(new T());
        }

        lock (_lock)
        {
            int toAdd = Math.Min(count, _maxSize - _stack.Count);
            for (int i = 0; i < toAdd; i++)
            {
                _stack.Push(list[i]);
            }
        }
    }

    /// <summary>
    /// Clear the pool.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _stack.Clear();
        }
    }

    /// <summary>
    /// Current count of pooled instances (for diagnostics).
    /// </summary>
    public static int Count
    {
        get
        {
            lock (_lock) return _stack.Count;
        }
    }
}
