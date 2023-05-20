//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.ObjectPool;
using System.Collections.Generic;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            /// <summary>
            /// 资源对象。
            /// PS：无论从哪个角度来考虑，“对象池系统”中的“Unspawn”方法主要是为了更新“其内部对象被获取的次数”，
            ///     因此如果本ResourceObject执行“Unspawn”方法时，也需要将其依赖的“其他AB”也执行“unspawn”方法，
            ///     代表本AB用完后，其依赖的其他AB的获取次数也需要递减
            ///     然而本脚本中却并没有此逻辑 —— 这应该是E大的疏忽！！！！！
            /// </summary>
            private sealed class ResourceObject : ObjectBase
            {
                //本AssetBundle所依赖的其他AB的列表集合。主要作用在于：在卸载本AssetBundle时，更新其所依赖的AB的统计计数比较方便
                private List<object> m_DependencyResources;
                //由于需要用到其中的“m_ResourceDependencyCount”集合，因此这里需要参数“m_ResourceLoader”，并在“Create”中为其赋值
                private ResourceLoader m_ResourceLoader;
                //“资源模块”的“helper”
                private IResourceHelper m_ResourceHelper;

                public ResourceObject()
                {
                    m_DependencyResources = new List<object>();
                    m_ResourceHelper = null;
                    m_ResourceLoader = null;
                }

                //在“ObjectBase”中提供此属性的作用在于：一者其可以被扩展类继承，二者需要使用的地方都可以调用一个“共同的名字”
                //主要是为方便框架中其他模块的使用
                public override bool CustomCanReleaseFlag
                {
                    get
                    {
                        int targetReferenceCount = 0;
                        //使用“ReadLoader”中用于专门记录所有asset被依赖次数的集合“m_ResourceDependencyCount”
                        m_ResourceLoader.m_ResourceDependencyCount.TryGetValue(Target, out targetReferenceCount);
                        return base.CustomCanReleaseFlag && targetReferenceCount <= 0;
                    }
                }

                public static ResourceObject Create(string name, object target, IResourceHelper resourceHelper, ResourceLoader resourceLoader)
                {
                    if (resourceHelper == null)
                    {
                        throw new GameFrameworkException("Resource helper is invalid.");
                    }

                    if (resourceLoader == null)
                    {
                        throw new GameFrameworkException("Resource loader is invalid.");
                    }

                    ResourceObject resourceObject = ReferencePool.Acquire<ResourceObject>();
                    //注意：这里的“target”是加载完成后得到的“AssetBundle”本身，不是该AB的“resource.FullName”，
                    //而是实实在在的AssetBundle资源在内存中
                    resourceObject.Initialize(name, target);
                    resourceObject.m_ResourceHelper = resourceHelper;
                    resourceObject.m_ResourceLoader = resourceLoader;
                    return resourceObject;
                }

                public override void Clear()
                {
                    base.Clear();
                    m_DependencyResources.Clear();
                    m_ResourceHelper = null;
                    m_ResourceLoader = null;
                }

                //这是提供给外部的方法，脚本内部不会调用。其作为“ResourceObject”类型变量的public方法被外部调用
                public void AddDependencyResource(object dependencyResource)
                {
                    if (Target == dependencyResource)
                    {
                        return;
                    }

                    if (m_DependencyResources.Contains(dependencyResource))
                    {
                        return;
                    }

                    m_DependencyResources.Add(dependencyResource);

                    //更新该“dependencyResource”在“m_ResourceDependencyCount”集合中的统计数量
                    int referenceCount = 0;
                    if (m_ResourceLoader.m_ResourceDependencyCount.TryGetValue(dependencyResource, out referenceCount))
                    {
                        m_ResourceLoader.m_ResourceDependencyCount[dependencyResource] = referenceCount + 1;
                    }
                    else
                    {
                        m_ResourceLoader.m_ResourceDependencyCount.Add(dependencyResource, 1);
                    }
                }

                protected internal override void Release(bool isShutdown)
                {
                    //若不是“框架关闭”，如“正式退出游戏”，此时“isShutDown = true”
                    if (!isShutdown)
                    {
                        //在释放该AssetBundle之前需要检测其自身是否被作为“其他AssetBundle”的依赖AB。如果是，则必然不能卸载
                        int targetReferenceCount = 0;
                        if (m_ResourceLoader.m_ResourceDependencyCount.TryGetValue(Target, out targetReferenceCount) && targetReferenceCount > 0)
                        {
                            throw new GameFrameworkException(Utility.Text.Format("Resource target '{0}' reference count is '{1}' larger than 0.", Name, targetReferenceCount));
                        }

                        //如果卸载本AssetBundle，则其所依赖的其他AB的统计信息必然需要更新(递减1)
                        //这也是全局变量“m_DependencyResources”的作用：在更新依赖的AB的统计计数时比较方便
                        foreach (object dependencyResource in m_DependencyResources)
                        {
                            int referenceCount = 0;
                            if (m_ResourceLoader.m_ResourceDependencyCount.TryGetValue(dependencyResource, out referenceCount))
                            {
                                m_ResourceLoader.m_ResourceDependencyCount[dependencyResource] = referenceCount - 1;
                            }
                            else
                            {
                                throw new GameFrameworkException(Utility.Text.Format("Resource target '{0}' dependency asset reference count is invalid.", Name));
                            }
                        }
                    }

                    //若已然释放该AssetBundle，则需要在“m_ResourceDependencyCount”集合中删除该AB的统计信息
                    //PS：其实不删除也没关系，在需要用到其“依赖数量值”时判断一下是否“<= 0”即可
                    m_ResourceLoader.m_ResourceDependencyCount.Remove(Target);

                    //这里才是真正的释放该AssetBundle
                    //AssetBundle的释放应该调用“assetBundle.Unload(true)”方法
                    m_ResourceHelper.Release(Target);
                }
            }
        }
    }
}
