/***************************************************************************
// File       : Property.cs
// Author     : Panyuxuan
// Created    : 2025/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

namespace Sim
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// 可观察的属性包装器，支持任何类型
    /// </summary>
    public class Property<T> : IDisposable
    {
        private T _data;
        private readonly object _lock = new object();
        private readonly bool _enableThreadSafe =false;
        /// <summary>
        /// 当前值
        /// </summary>
        public T Data
        {
            get
            {
                if (_enableThreadSafe)
                {
                    lock (_lock) { return _data; }
                }
                return _data;
            }
            set
            {
                if (_enableThreadSafe)
                {
                    lock (_lock)
                    {
                        SetValueInternal(value);
                    }
                }
                else
                {
                    SetValueInternal(value);
                }
            }
        }

        /// <summary>
        /// 值变化事件 (oldValue, newValue)
        /// </summary>
        public event Action<T, T> OnValueChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="initialValue">初始值</param>
        /// <param name="enableThreadSafe">是否启用线程安全</param>
        public Property(T initialValue = default, bool enableThreadSafe = false)
        {
            _data = initialValue;
            _enableThreadSafe = enableThreadSafe;
            OnValueChanged = null;
        }

        private void SetValueInternal(T value)
        {
            if (!EqualityComparer<T>.Default.Equals(_data, value))
            {
                var oldData = _data;
                _data = value;
                OnValueChanged?.Invoke(oldData, _data);
            }
        }

        /// <summary>
        /// 手动触发一次值变化事件（用于强制刷新）
        /// </summary>
        public void ForceNotify()
        {
            OnValueChanged?.Invoke(_data, _data);
        }

        /// <summary>
        /// 清空所有监听
        /// </summary>
        public void ClearListeners()
        {
            OnValueChanged = null;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            ClearListeners();
        }

        // 隐式转换，方便直接使用
        public static implicit operator T(Property<T> property) => property.Data;
    }
}

