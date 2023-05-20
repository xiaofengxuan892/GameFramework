//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace GameFramework.ObjectPool
{
    internal sealed partial class ObjectPoolManager : GameFrameworkModule, IObjectPoolManager
    {
        /// <summary>
        /// 对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        private sealed class ObjectPool<T> : ObjectPoolBase, IObjectPool<T> where T : ObjectBase
        {
            private readonly GameFrameworkMultiDictionary<string, Object<T>> m_Objects;
            private readonly Dictionary<object, Object<T>> m_ObjectMap;

            //说明：1.m_CachedToReleaseObjects中存放的是“必然被释放的对象”，在任意时候调用“Release”方法都会
            //     检测当前“对象池m_ObjectMap”集合中“已经超出目标过期时间的对象”，该对象必然会被释放
            //     2.m_CacheCanReleaseObjects指当前“m_ObjectMap”中根据“对象池最大容量Capacity”和“当前已包含的对象数量Count”
            //       来计算出“必须要释放的对象的数量”，该数值需要尽可能做到，但也依然需要在“m_CachedCanReleaseObjects”的集合内挑选
            //       即如果该对象当前正在使用或被锁定，则是必然不能释放的
            //       对象的释放挑选标准：只要该对象当前没有被使用，并且没有被锁定，则必然可以被释放
            //     3.m_DefaultReleaseObjectFilterCallback筛选逻辑：即使当前需要释放的数量<0,即“对象池空间足够大，足以存放当前所有对象”
            //       但对于“对象池中已经超出expireTime的对象”也已然要释放掉
            //       在以上基础上，如果还无法满足“必须要释放的数量 —— 根据Capacity和Count决定”，则从“m_CachedCanReleaseObjects”继续挑选
            // 但无论是哪种情况，如果该对象当前还在被使用中，或者被锁定，则必然不能释放
            private readonly List<T> m_CachedCanReleaseObjects;
            private readonly List<T> m_CachedToReleaseObjects;
            private readonly ReleaseObjectFilterCallback<T> m_DefaultReleaseObjectFilterCallback;

            private readonly bool m_AllowMultiSpawn;
            private float m_AutoReleaseInterval;
            private int m_Capacity;
            //这里的“m_ExpireTime”指的是每个对象在上次使用过后间隔“m_ExpireTime”秒后会被视为“过期”
            //是“float”类型变量。当在实际判断过期时间时，通常会将“DateTime.Now”与该数值进行计算，如此比较来确定对象当前是否过期
            private float m_ExpireTime;

            private int m_Priority;
            private float m_AutoReleaseTime;

            /// <summary>
            /// 初始化对象池的新实例。
            /// </summary>
            /// <param name="name">对象池名称。</param>
            /// <param name="allowMultiSpawn">是否允许对象被多次获取。</param>
            /// <param name="autoReleaseInterval">对象池自动释放可释放对象的间隔秒数。</param>
            /// <param name="capacity">对象池的容量。</param>
            /// <param name="expireTime">对象池对象过期秒数。</param>
            /// <param name="priority">对象池的优先级。</param>
            public ObjectPool(string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity, float expireTime, int priority)
                : base(name)
            {
                m_Objects = new GameFrameworkMultiDictionary<string, Object<T>>();
                m_ObjectMap = new Dictionary<object, Object<T>>();
                m_DefaultReleaseObjectFilterCallback = DefaultReleaseObjectFilterCallback;
                m_CachedCanReleaseObjects = new List<T>();
                m_CachedToReleaseObjects = new List<T>();
                m_AllowMultiSpawn = allowMultiSpawn;
                m_AutoReleaseInterval = autoReleaseInterval;
                Capacity = capacity;
                ExpireTime = expireTime;
                m_Priority = priority;
                m_AutoReleaseTime = 0f;
            }

            /// <summary>
            /// 获取对象池对象类型。
            /// </summary>
            public override Type ObjectType
            {
                get
                {
                    return typeof(T);
                }
            }

            /// <summary>
            /// 获取对象池中对象的数量。
            /// </summary>
            public override int Count
            {
                get
                {
                    return m_ObjectMap.Count;
                }
            }

            /// <summary>
            /// 获取对象池中能被释放的对象的数量。
            /// </summary>
            public override int CanReleaseCount
            {
                get
                {
                    //此时传递过去的“m_CachedCanReleaseObjects”本来就是空的，使用“GetCanReleaseObjects”方法将目标对象
                    //筛选出来放入“m_CachedCanReleaseObjects”中
                    //按照当前的代码逻辑：只要该对象没有被使用，并且没有被锁定，即可以被视为“可以释放的对象”
                    GetCanReleaseObjects(m_CachedCanReleaseObjects);
                    return m_CachedCanReleaseObjects.Count;
                }
            }

            /// <summary>
            /// 获取是否允许对象被多次获取。
            /// </summary>
            public override bool AllowMultiSpawn
            {
                get
                {
                    return m_AllowMultiSpawn;
                }
            }

            /// <summary>
            /// 获取或设置对象池自动释放可释放对象的间隔秒数。
            /// </summary>
            public override float AutoReleaseInterval
            {
                get
                {
                    return m_AutoReleaseInterval;
                }
                set
                {
                    m_AutoReleaseInterval = value;
                }
            }

            /// <summary>
            /// 获取或设置对象池的容量。
            /// </summary>
            public override int Capacity
            {
                get
                {
                    return m_Capacity;
                }
                set
                {
                    if (value < 0)
                    {
                        throw new GameFrameworkException("Capacity is invalid.");
                    }

                    if (m_Capacity == value)
                    {
                        return;
                    }

                    m_Capacity = value;
                    //尽最大可能释放满足“Count - Capacity”数量的对象，但依然要满足“m_CachedCanReleaseObjects”的挑选规则
                    Release();
                }
            }

            /// <summary>
            /// 获取或设置对象池对象过期秒数。
            /// </summary>
            public override float ExpireTime
            {
                get
                {
                    return m_ExpireTime;
                }

                set
                {
                    if (value < 0f)
                    {
                        throw new GameFrameworkException("ExpireTime is invalid.");
                    }

                    if (ExpireTime == value)
                    {
                        return;
                    }

                    //注意：这里设置的时间“m_ExpireTime”代表的是“相较上次使用间隔的时间”，在此时间后该对象过期
                    m_ExpireTime = value;
                    Release();
                }
            }

            /// <summary>
            /// 获取或设置对象池的优先级。
            /// </summary>
            public override int Priority
            {
                get
                {
                    return m_Priority;
                }
                set
                {
                    m_Priority = value;
                }
            }

            /// <summary>
            /// 获取所有对象信息。
            /// </summary>
            /// <returns>所有对象信息。</returns>
            public override ObjectInfo[] GetAllObjectInfos()
            {
                List<ObjectInfo> results = new List<ObjectInfo>();
                foreach (KeyValuePair<string, GameFrameworkLinkedListRange<Object<T>>> objectRanges in m_Objects)
                {
                    foreach (Object<T> internalObject in objectRanges.Value)
                    {
                        results.Add(new ObjectInfo(internalObject.Name, internalObject.Locked, internalObject.CustomCanReleaseFlag, internalObject.Priority, internalObject.LastUseTime, internalObject.SpawnCount));
                    }
                }

                return results.ToArray();
            }

            internal override void Update(float elapseSeconds, float realElapseSeconds)
            {
                m_AutoReleaseTime += realElapseSeconds;
                if (m_AutoReleaseTime < m_AutoReleaseInterval)
                {
                    return;
                }

                Release();
            }

            internal override void Shutdown()
            {
                foreach (KeyValuePair<object, Object<T>> objectInMap in m_ObjectMap)
                {
                    objectInMap.Value.Release(true);
                    ReferencePool.Release(objectInMap.Value);
                }

                m_Objects.Clear();
                m_ObjectMap.Clear();
                m_CachedCanReleaseObjects.Clear();
                m_CachedToReleaseObjects.Clear();
            }

            /// <summary>
            /// 设置对象是否被加锁。
            /// </summary>
            /// <param name="obj">要设置被加锁的对象。</param>
            /// <param name="locked">是否被加锁。</param>
            public void SetLocked(T obj, bool locked)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                SetLocked(obj.Target, locked);
            }

            /// <summary>
            /// 设置对象是否被加锁。
            /// </summary>
            /// <param name="target">要设置被加锁的对象。</param>
            /// <param name="locked">是否被加锁。</param>
            public void SetLocked(object target, bool locked)
            {
                if (target == null)
                {
                    throw new GameFrameworkException("Target is invalid.");
                }

                Object<T> internalObject = GetObject(target);
                if (internalObject != null)
                {
                    internalObject.Locked = locked;
                }
                else
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not find target in object pool '{0}', target type is '{1}', target value is '{2}'.", new TypeNamePair(typeof(T), Name), target.GetType().FullName, target));
                }
            }

            /// <summary>
            /// 设置对象的优先级。
            /// </summary>
            /// <param name="obj">要设置优先级的对象。</param>
            /// <param name="priority">优先级。</param>
            public void SetPriority(T obj, int priority)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                SetPriority(obj.Target, priority);
            }

            /// <summary>
            /// 设置对象的优先级。
            /// </summary>
            /// <param name="target">要设置优先级的对象。</param>
            /// <param name="priority">优先级。</param>
            public void SetPriority(object target, int priority)
            {
                if (target == null)
                {
                    throw new GameFrameworkException("Target is invalid.");
                }

                Object<T> internalObject = GetObject(target);
                if (internalObject != null)
                {
                    internalObject.Priority = priority;
                }
                else
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not find target in object pool '{0}', target type is '{1}', target value is '{2}'.", new TypeNamePair(typeof(T), Name), target.GetType().FullName, target));
                }
            }

            /// <summary>
            /// 创建对象。
            /// PS: 这里的“Register”其实就相当于向对象池中添加对象了，但外部并不会直接调用“Register”，而是直接调用“Spawn”方法
            ///     只有在池子中没有该对象实例时才会自动创建新的对象并使用“Register”方法将其放入对象池中
            /// 注意：这里传递过来的“obj”参数其实已经是完整的继承自“ObjectBase”的对象，如“EntityInstanceObejct”
            ///      而“internalObject”只是为了方便“对象池系统内部使用”而包装出来的
            /// </summary>
            /// <param name="obj">对象。</param>
            /// <param name="spawned">对象是否已被获取。</param>
            public void Register(T obj, bool spawned)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                //这里传递过来的“obj”其实已经是“实际的对象“，ObjectBase的扩展类，如”EntityInstanceObject“等
                Object<T> internalObject = Object<T>.Create(obj, spawned);
                //这里的”Name“是该对象的”prefab“的路径
                m_Objects.Add(obj.Name, internalObject);
                //这里的”Target“是实际的”GameObject“本身
                m_ObjectMap.Add(obj.Target, internalObject);

                //如果容量超过，则需要释放部分对象
                if (Count > m_Capacity)
                {
                    Release();
                }
            }

            /// <summary>
            /// 检查对象。
            /// </summary>
            /// <returns>要检查的对象是否存在。</returns>
            public bool CanSpawn()
            {
                //从对象池中检测目标pfefab(通过name传递)是否存在“实例对象”？如果有，并且可以使用该“实例对象”，则代表“可以获取”
                return CanSpawn(string.Empty);
            }

            /// <summary>
            /// 检查对象。
            /// </summary>
            /// <param name="name">对象名称。</param>
            /// <returns>要检查的对象是否存在。</returns>
            public bool CanSpawn(string name)
            {
                if (name == null)
                {
                    throw new GameFrameworkException("Name is invalid.");
                }

                GameFrameworkLinkedListRange<Object<T>> objectRange = default(GameFrameworkLinkedListRange<Object<T>>);
                if (m_Objects.TryGetValue(name, out objectRange))
                {
                    //“m_Objects”中是以该对象的“prefab”为主键进行存储，该“prefab”创建的多个“internalObject”
                    //会放在同一集合中，即“GameFrameworkLinkedListRange”
                    foreach (Object<T> internalObject in objectRange)
                    {
                        //同一个对象如何被多次获取？？？
                        if (m_AllowMultiSpawn || !internalObject.IsInUse)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// 获取对象。
            /// </summary>
            /// <returns>要获取的对象。</returns>
            public T Spawn()
            {
                return Spawn(string.Empty);
            }

            /// <summary>
            /// 获取对象。
            /// </summary>
            /// <param name="name">对象名称。</param>
            /// <returns>要获取的对象。</returns>
            public T Spawn(string name)
            {
                if (name == null)
                {
                    throw new GameFrameworkException("Name is invalid.");
                }

                GameFrameworkLinkedListRange<Object<T>> objectRange = default(GameFrameworkLinkedListRange<Object<T>>);
                if (m_Objects.TryGetValue(name, out objectRange))
                {
                    foreach (Object<T> internalObject in objectRange)
                    {
                        //从该“prefab”的所有“实例对象”中获取到“第一个可以使用的实例对象”，
                        if (m_AllowMultiSpawn || !internalObject.IsInUse)
                        {
                            return internalObject.Spawn();
                        }
                    }
                }

                return null;
            }

            private Object<T> GetObject(object target)
            {
                if (target == null)
                {
                    throw new GameFrameworkException("Target is invalid.");
                }

                Object<T> internalObject = null;
                if (m_ObjectMap.TryGetValue(target, out internalObject))
                {
                    return internalObject;
                }

                return null;
            }

            /// <summary>
            /// 回收对象。
            /// PS: 这里传递过来的参数“obj”是“ObjectBase”类型的对象
            /// </summary>
            /// <param name="obj">要回收的对象。</param>
            public void Unspawn(T obj)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                //这里传递过来的参数“obj”是完整的“ObjectBase”对象，但这里回收的是“obj.Target”，即“GameObject”本身
                //因为需要回收对象池中实际使用的对象“internalObject”，而在“m_ObjectMap”集合中是以“GameObject”作为key
                //来存储其对应封装的"internalObject"的，因此这里只传递参数“obj.Target”
                Unspawn(obj.Target);
            }

            /// <summary>
            /// 回收对象。
            /// PS: 这里传递过来的参数“target”是“GameObject”对象，即“objectBase.Target”
            /// </summary>
            /// <param name="target">要回收的对象。</param>
            public void Unspawn(object target)
            {
                if (target == null)
                {
                    throw new GameFrameworkException("Target is invalid.");
                }

                //根据该“objectBase.Target”从集合中查找其对象的“internalObject”
                //注意：这里传过来的参数“target”已经是“GameObject”，而非“ObjectBase”
                Object<T> internalObject = GetObject(target);
                if (internalObject != null)
                {
                    //先执行该“internalObject”的“Unspawn”方法，之后是“放入对象池”还是直接“release”则根据“最大容量”来决定
                    internalObject.Unspawn();
                    //这里的判断条件“internalObject.SpawnCount <= 0”是否有必要！！
                    //“Release”方法会遍历当前所有对象，从中选择最合适的“m_CachedToReleaseObjects”
                    //只要满足“Count > m_Capacity”就应该执行“Release”
                    if (Count > m_Capacity && internalObject.SpawnCount <= 0)
                    {
                        Release();
                    }
                }
                else
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not find target in object pool '{0}', target type is '{1}', target value is '{2}'.", new TypeNamePair(typeof(T), Name), target.GetType().FullName, target));
                }
            }

            /// <summary>
            /// 释放对象池中的所有未使用对象。
            /// </summary>
            public override void ReleaseAllUnused()
            {
                m_AutoReleaseTime = 0f;
                GetCanReleaseObjects(m_CachedCanReleaseObjects);
                foreach (T toReleaseObject in m_CachedCanReleaseObjects)
                {
                    ReleaseObject(toReleaseObject);
                }
            }

            /// <summary>
            /// 释放对象。
            /// </summary>
            /// <param name="obj">要释放的对象。</param>
            /// <returns>释放对象是否成功。</returns>
            public bool ReleaseObject(T obj)
            {
                if (obj == null)
                {
                    throw new GameFrameworkException("Object is invalid.");
                }

                return ReleaseObject(obj.Target);
            }

            /// <summary>
            /// 释放对象。
            /// </summary>
            /// <param name="target">要释放的对象。</param>
            /// <returns>释放对象是否成功。</returns>
            public bool ReleaseObject(object target)
            {
                if (target == null)
                {
                    throw new GameFrameworkException("Target is invalid.");
                }

                Object<T> internalObject = GetObject(target);
                if (internalObject == null)
                {
                    return false;
                }

                if (internalObject.IsInUse || internalObject.Locked || !internalObject.CustomCanReleaseFlag)
                {
                    return false;
                }

                m_Objects.Remove(internalObject.Name, internalObject);
                m_ObjectMap.Remove(internalObject.Peek().Target);

                //执行该internalObject本身的“Release”方法
                internalObject.Release(false);
                //必须释放该引用对象
                ReferencePool.Release(internalObject);
                return true;
            }

            /// <summary>
            /// 释放对象池中的可释放对象。
            /// </summary>
            public override void Release()
            {
                Release(Count - m_Capacity, m_DefaultReleaseObjectFilterCallback);
            }

            /// <summary>
            /// 释放对象池中的可释放对象。
            /// </summary>
            /// <param name="toReleaseCount">尝试释放对象数量。</param>
            public override void Release(int toReleaseCount)
            {
                Release(toReleaseCount, m_DefaultReleaseObjectFilterCallback);
            }

            /// <summary>
            /// 释放对象池中的可释放对象。
            /// </summary>
            /// <param name="releaseObjectFilterCallback">释放对象筛选函数。</param>
            public void Release(ReleaseObjectFilterCallback<T> releaseObjectFilterCallback)
            {
                Release(Count - m_Capacity, releaseObjectFilterCallback);
            }

            /// <summary>
            /// 释放对象池中的可释放对象。
            /// </summary>
            /// <param name="toReleaseCount">尝试释放对象数量。</param>
            /// <param name="releaseObjectFilterCallback">释放对象筛选函数。</param>
            public void Release(int toReleaseCount, ReleaseObjectFilterCallback<T> releaseObjectFilterCallback)
            {
                if (releaseObjectFilterCallback == null)
                {
                    throw new GameFrameworkException("Release object filter callback is invalid.");
                }

                //如果“toReleaseCount <= 0”，是否可以直接“return”，无需执行后续代码了
                if (toReleaseCount < 0)
                {
                    toReleaseCount = 0;
                }

                //这傻逼的计算时间方式也是操蛋：
                //当前计算方式是：根据“m_ExpireTime(代表对象在上次使用后间隔‘m_ExpireTime’秒后过期)”
                //因此这里以当前为刻度往前推算，如果对象的“LastUseTime”比这早则说明会过期，否则表示没过期
                DateTime expireTime = DateTime.MinValue;
                if (m_ExpireTime < float.MaxValue)
                {
                    //计算出后续判断“对象”是否过期的时间刻度：早于此时间则过期，比这时间晚则说明没有
                    expireTime = DateTime.UtcNow.AddSeconds(-m_ExpireTime);
                }

                m_AutoReleaseTime = 0f;
                GetCanReleaseObjects(m_CachedCanReleaseObjects);
                List<T> toReleaseObjects = releaseObjectFilterCallback(m_CachedCanReleaseObjects, toReleaseCount, expireTime);
                if (toReleaseObjects == null || toReleaseObjects.Count <= 0)
                {
                    return;
                }

                foreach (T toReleaseObject in toReleaseObjects)
                {
                    ReleaseObject(toReleaseObject);
                }
            }

            private void GetCanReleaseObjects(List<T> results)
            {
                if (results == null)
                {
                    throw new GameFrameworkException("Results is invalid.");
                }

                //传递过来的参数“results”原本就是空的
                results.Clear();
                foreach (KeyValuePair<object, Object<T>> objectInMap in m_ObjectMap)
                {
                    Object<T> internalObject = objectInMap.Value;
                    if (internalObject.IsInUse || internalObject.Locked || !internalObject.CustomCanReleaseFlag)
                    {
                        continue;
                    }

                    //将封装之后的“Object<T>”对象“internalObject”中的“m_Object”放入目标集合中
                    //因为其最终需要释放的是“m_Object”，而与新封装的“内部对象internalObject”没有任何关系
                    results.Add(internalObject.Peek());
                }
            }

            //过滤的规则：
            //绝对不能释放的：该对象还在被使用中，或者被锁定了
            //绝对要释放的：该对象已经过期
            //无论“需要释放的对象数量”是多少，都只能在“m_CachedCanReleaseObjects”中挑选
            //因此有可能存在执行“Release”方法结束后没有达到“必须释放数量要求”的情况
            private List<T> DefaultReleaseObjectFilterCallback(List<T> candidateObjects, int toReleaseCount, DateTime expireTime)
            {
                //这两个傻逼的名字起的实在是太相近了：m_CachedToReleaseObjects 和 m_CachedCanReleaseObjects
                m_CachedToReleaseObjects.Clear();

                //这里传递过来的“expireTime”是判断对象是否过期的“DateTime”类型刻度：
                //如果对象的“LastUseTime < expireTime”，则代表其过期了需要释放
                if (expireTime > DateTime.MinValue)  //这个判断条件真的有效吗？还不如“while(true)”循环
                {
                    //倒序遍历：因为过程中会删除集合的元素
                    for (int i = candidateObjects.Count - 1; i >= 0; i--)
                    {
                        if (candidateObjects[i].LastUseTime <= expireTime)
                        {
                            //这里加入集合的是必须被释放的对象
                            m_CachedToReleaseObjects.Add(candidateObjects[i]);
                            //因后续有可能还会从该集合中挑选“目标释放对象”，因此这里先移除掉已经被挑选的
                            candidateObjects.RemoveAt(i);
                            continue;
                        }
                    }

                    //注意：无论“toReleaseCount”原本的数值是否大于0，以上“已经超出过期时间”的对象都必须被释放
                    toReleaseCount -= m_CachedToReleaseObjects.Count;
                }

                //如果依然没有满足“必须要释放的对象数量”，则执行以下逻辑，但并不一定最后可以凑出完整个数
                for (int i = 0; toReleaseCount > 0 && i < candidateObjects.Count; i++)
                {
                    //从以下逻辑可知：将优先级高的，或者同一优先级但“最近一次使用时间最大，即后使用的”都向后排序
                    //将优先级低或较早期使用的对象释放掉
                    //可以把如下逻辑抽象出来，这里太乱了
                    for (int j = i + 1; j < candidateObjects.Count; j++)
                    {
                        if (candidateObjects[i].Priority > candidateObjects[j].Priority
                            || candidateObjects[i].Priority == candidateObjects[j].Priority && candidateObjects[i].LastUseTime > candidateObjects[j].LastUseTime)
                        {
                            T temp = candidateObjects[i];
                            candidateObjects[i] = candidateObjects[j];
                            candidateObjects[j] = temp;
                        }
                    }

                    m_CachedToReleaseObjects.Add(candidateObjects[i]);
                    toReleaseCount--;
                }

                return m_CachedToReleaseObjects;
            }
        }
    }
}
