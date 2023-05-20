//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.ObjectPool;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            /// <summary>
            /// 资源对象。
            /// PS：这里的“Asset”指代的是一个个具体的资源，如音频文件music_background
            /// 添加日志输出后可知：参数“m_Resource”指代的是该“asset”所在的“Assetbundle”
            /// 参数“m_Target”指代的是该资源本身，如音频文件music_background
            /// </summary>
            private sealed class AssetObject : ObjectBase
            {
                //和“ResourceObject”一样，用于统计本“asset”所依赖的其他asset的集合。在卸载本asset时需要同步更新其依赖的asset的统计信息
                private List<object> m_DependencyAssets;
                //由于会用到“m_ResourceLoader.m_AssetDependencyCount”集合用于更新“依赖计数信息”，
                //所以这里添加该参数，并在“Create”中为其赋值
                private ResourceLoader m_ResourceLoader;

                //本asset所属的“AssetBundle”(注意：这里是使用实打实的AssetBundle，并不是其fullName)
                private object m_Resource;
                //实际卸载该asset时会借助“xxHelper”来实现
                private IResourceHelper m_ResourceHelper;

                public AssetObject()
                {
                    m_DependencyAssets = new List<object>();
                    m_Resource = null;
                    m_ResourceHelper = null;
                    m_ResourceLoader = null;
                }

                public override bool CustomCanReleaseFlag
                {
                    get
                    {
                        int targetReferenceCount = 0;
                        m_ResourceLoader.m_AssetDependencyCount.TryGetValue(Target, out targetReferenceCount);
                        return base.CustomCanReleaseFlag && targetReferenceCount <= 0;
                    }
                }

                public static AssetObject Create(string name, object target, List<object> dependencyAssets, object resource, IResourceHelper resourceHelper, ResourceLoader resourceLoader)
                {
                    if (dependencyAssets == null)
                    {
                        throw new GameFrameworkException("Dependency assets is invalid.");
                    }

                    if (resource == null)
                    {
                        throw new GameFrameworkException("Resource is invalid.");
                    }

                    if (resourceHelper == null)
                    {
                        throw new GameFrameworkException("Resource helper is invalid.");
                    }

                    if (resourceLoader == null)
                    {
                        throw new GameFrameworkException("Resource loader is invalid.");
                    }

                    AssetObject assetObject = ReferencePool.Acquire<AssetObject>();
                    //这里的“target”是加载完成得到的“object”对象，即AssetBundle中实际存在的Asset
                    assetObject.Initialize(name, target);
                    //设置本assetObject所依赖的所有其他asset
                    assetObject.m_DependencyAssets.AddRange(dependencyAssets);
                    //这里的“resource”指代的是本asset所属的“AssetBundle”，而不是其他模块中的“prefab”等，
                    //如“实体系统EntityInstanceObject”中的“entityAsset”
                    assetObject.m_Resource = resource;
                    assetObject.m_ResourceHelper = resourceHelper;
                    assetObject.m_ResourceLoader = resourceLoader;

                    //“resourceLoader.m_AssetDependencyCount”代表的是每个asset被多少个其他asset所依赖
                    //如果为0，则代表该asset没有被任何其他asset所依赖，此时如果需要则可以安全的卸载该asset
                    foreach (object dependencyAsset in dependencyAssets)
                    {
                        int referenceCount = 0;
                        if (resourceLoader.m_AssetDependencyCount.TryGetValue(dependencyAsset, out referenceCount))
                        {
                            resourceLoader.m_AssetDependencyCount[dependencyAsset] = referenceCount + 1;
                        }
                        else
                        {
                            resourceLoader.m_AssetDependencyCount.Add(dependencyAsset, 1);
                        }
                    }

                    //Debug.LogFormat("m_Resource: {0}, Target: {1}", resource, target);
                    return assetObject;
                }

                public override void Clear()
                {
                    base.Clear();
                    m_DependencyAssets.Clear();
                    m_Resource = null;
                    m_ResourceHelper = null;
                    m_ResourceLoader = null;
                }

                //这里代表的是“回收”，而不是“卸载“。该AssetObject的卸载由”对象池系统“内部自动触发。无需这里手动调用
                protected internal override void OnUnspawn()
                {
                    base.OnUnspawn();
                    //当该asset回收后，其所依赖的资源也需要回收。“回收”本质上仅仅只是“修改spawnCount”，
                    //实际的“Release”逻辑交由“对象池系统”负责
                    //因此这里也需要更新其所依赖的其他Asset的”m_SpawnCount“
                    foreach (object dependencyAsset in m_DependencyAssets)
                    {
                        m_ResourceLoader.m_AssetPool.Unspawn(dependencyAsset);
                    }

                    //扩展：这里只是”回收“，但该asset以及其所依赖的其他asset依然还在内存中，
                    //因此"m_AssetDependencyCount"中的”referenceCount“并不会更新
                }

                //将“资源池m_AssetPool”中的某个资源释放时，则必然是因为“对象池系统”内部的“Release逻辑”最终选择该asset对象代表的internalObject
                //那么此时该internalObject所包含的“objectBase”自会执行“release逻辑”
                //因此主要用来处理该与该asset相关的一些内容，如这里的“dependencyCount”
                protected internal override void Release(bool isShutdown)
                {
                    if (!isShutdown)
                    {
                        int targetReferenceCount = 0;
                        //如果该asset当前还被其他asset所依赖，则必然不能释放
                        if (m_ResourceLoader.m_AssetDependencyCount.TryGetValue(Target, out targetReferenceCount) && targetReferenceCount > 0)
                        {
                            throw new GameFrameworkException(Utility.Text.Format("Asset target '{0}' reference count is '{1}' larger than 0.", Name, targetReferenceCount));
                        }

                        //如果当前asset被正常卸载，那么该asset所依赖的其他asset的“依赖数量”必然需要递减
                        foreach (object dependencyAsset in m_DependencyAssets)
                        {
                            int referenceCount = 0;
                            //可以肯定，其“dependencyAsset”的“依赖数量”至少为1(因为当前asset还没有被卸载)
                            if (m_ResourceLoader.m_AssetDependencyCount.TryGetValue(dependencyAsset, out referenceCount))
                            {
                                m_ResourceLoader.m_AssetDependencyCount[dependencyAsset] = referenceCount - 1;
                            }
                            else
                            {
                                //如果该dependencyAsset居然没有记录，则必然是异常情况
                                throw new GameFrameworkException(Utility.Text.Format("Asset target '{0}' dependency asset reference count is invalid.", Name));
                            }
                        }

                        //此时必然需要“回收该asset所属的AssetBundle”(这里的回收其实只是更新spawnCount，当该resource.spawnCount=0时则可以将其卸载了)
                        m_ResourceLoader.m_ResourcePool.Unspawn(m_Resource);
                    }

                    //移除该asset在“m_AssetDependencyCount”中的元素(该集合代表的是某个asset被多少个其他的asset依赖)
                    //如果当前可以卸载该asset，说明必然可以将该集合中的元素删除
                    m_ResourceLoader.m_AssetDependencyCount.Remove(Target);
                    m_ResourceLoader.m_AssetToResourceMap.Remove(Target);

                    //这里才是真正的释放该asset
                    //如果其为AssetBundle中打包的资源则可以直接使用”Resource.Unload“卸载；
                    //如果其为”GameObject“或”Component“等，则不能使用”Resource.Unload“，只能使用”Destroy“来销毁释放
                    m_ResourceHelper.Release(Target);
                }
            }
        }
    }
}
