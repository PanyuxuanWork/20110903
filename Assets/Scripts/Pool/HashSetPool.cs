/***************************************************************************
// File       : HashSetPool.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine.Pool;

/// <summary>
/// HashSet对象池，用于复用HashSet实例，减少GC分配
/// </summary>
/// <typeparam name="T">HashSet中元素的类型</typeparam>
public static class HashSetPool<T>
{
    // 使用Unity内置的ObjectPool来管理HashSet实例
    private static readonly ObjectPool<HashSet<T>> _pool;

    // 静态构造函数，在第一次使用HashSetPool<T>时初始化
    static HashSetPool()
    {
        _pool = new ObjectPool<HashSet<T>>(
            createFunc: () => new HashSet<T>(),           // 创建新HashSet
            actionOnGet: (set) => set.Clear(),            // 从池中取出时清空
            actionOnRelease: (set) => set.Clear(),        // 归还到池时清空
            actionOnDestroy: null,                        // 销毁时的操作（可选）
            collectionCheck: true,                         // 检查重复归还（调试用）
            defaultCapacity: 10,                           // 默认容量
            maxSize: 100                                    // 池的最大容量
        );
    }

    /// <summary>
    /// 从池中获取一个HashSet实例
    /// </summary>
    /// <returns>可用的HashSet实例</returns>
    public static HashSet<T> Get()
    {
        return _pool.Get();
    }

    /// <summary>
    /// 将HashSet实例归还到池中
    /// </summary>
    /// <param name="set">要归还的HashSet</param>
    public static void Release(HashSet<T> set)
    {
        _pool.Release(set);
    }

    /// <summary>
    /// 使用using语句的便捷方法
    /// 示例：using (HashSetPool<int>.Get(out var set)) { ... }
    /// </summary>
    public static PooledObject<HashSet<T>> Get(out HashSet<T> set)
    {
        var pooledObject = _pool.Get(out set);
        return pooledObject;
    }
}

/// <summary>
/// 非泛型版本，提供更简洁的调用语法
/// </summary>
public static class HashSetPool
{
    /// <summary>
    /// 获取指定类型的HashSet池实例
    /// </summary>
    public static class For<T>
    {
        /// <summary>
        /// 从池中获取一个HashSet实例
        /// </summary>
        public static HashSet<T> Get() => HashSetPool<T>.Get();

        /// <summary>
        /// 将HashSet实例归还到池中
        /// </summary>
        public static void Release(HashSet<T> set) => HashSetPool<T>.Release(set);

        /// <summary>
        /// 使用using语句的便捷方法
        /// </summary>
        public static PooledObject<HashSet<T>> Get(out HashSet<T> set)
            => HashSetPool<T>.Get(out set);
    }
}