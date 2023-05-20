//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;

namespace GameFramework.ObjectPool
{
    internal sealed partial class ObjectPoolManager : GameFrameworkModule, IObjectPoolManager
    {
        /// <summary>
        /// 内部对象。
        /// PS：单个对象，使用“Object<T>”限制所有对象都是“ObjectBase”的子类对象
        ///    参照单例模式“Singleton<T>”来理解即可
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        private sealed class Object<T> : IReference where T : ObjectBase
        {
            //封装了一种“class”，名称为“Object”，其中用到的参数是“T”，该参数继承自“ObjectBase”
            private T m_Object;
            //该参数代表本internalObject在对象池中被使用的次数。当创建一个新的“internalObject”时，虽然其不会被放入对象池中，
            //但会使用“Register”方法将该对象注册到“对象池的m_Objects”集合中，此时“m_SpawnCount”默认为“spawned = true”,即数值为1
            //但是当去需要回收时，则会将该对象放入对象池中，此时会更新其计数，即“m_SpawnCount”为0，即此时“spawned = false”
            private int m_SpawnCount;

            /// <summary>
            /// 初始化内部对象的新实例。
            /// </summary>
            public Object()
            {
                m_Object = null;
                m_SpawnCount = 0;
            }

            /// <summary>
            /// 获取对象名称。
            /// </summary>
            public string Name
            {
                get
                {
                    return m_Object.Name;
                }
            }

            /// <summary>
            /// 获取对象是否被加锁。
            /// </summary>
            public bool Locked
            {
                get
                {
                    return m_Object.Locked;
                }
                internal set
                {
                    m_Object.Locked = value;
                }
            }

            /// <summary>
            /// 获取对象的优先级。
            /// </summary>
            public int Priority
            {
                get
                {
                    return m_Object.Priority;
                }
                internal set
                {
                    m_Object.Priority = value;
                }
            }

            /// <summary>
            /// 获取自定义释放检查标记。
            /// </summary>
            public bool CustomCanReleaseFlag
            {
                get
                {
                    return m_Object.CustomCanReleaseFlag;
                }
            }

            /// <summary>
            /// 获取对象上次使用时间。
            /// </summary>
            public DateTime LastUseTime
            {
                get
                {
                    return m_Object.LastUseTime;
                }
            }

            /// <summary>
            /// 获取对象是否正在使用。
            /// </summary>
            public bool IsInUse
            {
                get
                {
                    return m_SpawnCount > 0;
                }
            }

            /// <summary>
            /// 获取对象的获取计数。
            /// </summary>
            public int SpawnCount
            {
                get
                {
                    return m_SpawnCount;
                }
            }

            /// <summary>
            /// 创建内部对象。
            /// </summary>
            /// <param name="obj">对象。</param>
            /// <param name="spawned">对象是否已被获取。</param>
            /// <returns>创建的内部对象。</returns>
            public static Object<T> Create(T obj, bool spawned)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                //这里传递过来的“obj”已经是“ObjectBase”了，如“EntitiyInstanceObject”对象
                Object<T> internalObject = ReferencePool.Acquire<Object<T>>();
                internalObject.m_Object = obj;
                internalObject.m_SpawnCount = spawned ? 1 : 0;
                if (spawned)
                {
                    obj.OnSpawn();
                }

                return internalObject;
            }

            /// <summary>
            /// 清理内部对象。
            /// </summary>
            public void Clear()
            {
                m_Object = null;
                m_SpawnCount = 0;
            }

            /// <summary>
            /// 查看对象。
            /// </summary>
            /// <returns>对象。</returns>
            public T Peek()
            {
                return m_Object;
            }

            /// <summary>
            /// 获取对象。
            /// PS：该对象本身被获取时执行。
            /// 注意：从池子中取出对象，并不是直接就拿出一个。而是在从池子中拿出来后还要执行该“对象”自身的“获取方法，即Spawn”
            ///      并不是单方面的“获取”
            /// </summary>
            /// <returns>对象。</returns>
            public T Spawn()
            {
                m_SpawnCount++;
                m_Object.LastUseTime = DateTime.UtcNow;
                m_Object.OnSpawn();
                return m_Object;
            }

            /// <summary>
            /// 回收对象。
            /// </summary>
            public void Unspawn()
            {
                m_Object.OnUnspawn();
                m_Object.LastUseTime = DateTime.UtcNow;
                m_SpawnCount--;
                if (m_SpawnCount < 0)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Object '{0}' spawn count is less than 0.", Name));
                }
            }

            /// <summary>
            /// 释放对象。
            /// </summary>
            /// <param name="isShutdown">是否是关闭对象池时触发。</param>
            public void Release(bool isShutdown)
            {
                m_Object.Release(isShutdown);
                ReferencePool.Release(m_Object);
            }
        }
    }
}
