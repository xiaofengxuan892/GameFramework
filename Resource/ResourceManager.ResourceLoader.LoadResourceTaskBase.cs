//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            private abstract class LoadResourceTaskBase : TaskBase
            {
                //与”TaskBase“基类中的”m_SerialId“不同。该参数的主要目的在于为”资源模块“中的”任务池系统“的所有任务，
                //不论其是”LoadAssetTask“, ”LoadDependencyTask“, ”LoadSceneTask“，
                //都使用其基类”LoadResourceTaskBase“中的”s_Serial“来递增
                //PS：从该逻辑可以看出，每个不同的任务池中各个任务的编号是独立的，即TaskPool<T1>和TaskPool<T2>中的任务编号都可以从1开始，互不影响
                private static int s_Serial = 0;

                //从某种角度来看，并不会直接的加载某个AssetBundle，必然是因为需要加载该AB中的某个asset，因此导致需要加载其所属的AssetBundle
                //所以这里全部以AssetBundle中的”Asest“为中心
                private string m_AssetName;
                private Type m_AssetType;
                private ResourceInfo m_ResourceInfo;  //该asset所属的AssetBundle的信息
                private ResourceObject m_ResourceObject; //这里是resourceInfo指代的AssetBundle对象本身

                private string[] m_DependencyAssetNames; //依赖的其他assets的names
                private readonly List<object> m_DependencyAssets;  //与上述相比，这里是name指代的实际asset对象
                //代表该asset总共依赖的其他asset的数量。在创建”LoadAssetTask“或”LoadSceneTask“时会同时检测其”依赖assets“的数量，
                //并以此创建相应的”LoadDependencyTask“，此时会更新该参数，每创建一个”LoadDependencyTask“则将该参数递增1
                private int m_TotalDependencyAssetCount;
                //扩展：以上三个参数的作用
                //”m_DependencyAssetNames“代表本asset所依赖的其他asset的name的集合，由于只是”string[]“的name数组，
                //                        其实并不具备太强的约束力，大概只是起到”说明描述“的作用
                //m_DependencyAssets代表本asset依赖的所有asests中当前已经加载完成的assets的列表，
                //                        该参数只会在”OnLoadDependencyAsset“方法中被更新
                //m_TotalDependencyAssetCount：由于该参数只在”创建LoadDependencyTask“时更新，
                //                        因此其代表为当前asset创建的dependencyAsset的”LoadDependencyTask“的数量
                //                    注意：该参数与”m_DependencyAssetNames“的作用完全不同，不可混淆

                private DateTime m_StartTime;  //任务开始时间

                public LoadResourceTaskBase()
                {
                    m_AssetName = null;
                    m_AssetType = null;
                    m_ResourceInfo = null;
                    m_DependencyAssetNames = null;
                    m_DependencyAssets = new List<object>();
                    m_ResourceObject = null;
                    m_StartTime = default(DateTime);
                    m_TotalDependencyAssetCount = 0;
                }

                protected void Initialize(string assetName, Type assetType, int priority, ResourceInfo resourceInfo,
                    string[] dependencyAssetNames, object userData)
                {
                    Initialize(++s_Serial, null, priority, userData);
                    m_AssetName = assetName;
                    m_AssetType = assetType;
                    m_ResourceInfo = resourceInfo;
                    m_DependencyAssetNames = dependencyAssetNames;
                }

                public override void Clear()
                {
                    base.Clear();
                    m_AssetName = null;
                    m_AssetType = null;
                    m_ResourceInfo = null;
                    m_DependencyAssetNames = null;
                    m_DependencyAssets.Clear();
                    m_ResourceObject = null;
                    m_StartTime = default(DateTime);
                    m_TotalDependencyAssetCount = 0;
                }

                //本脚本的关键方法：当该asset所属的”AssetBundle“加载完毕后(通过resourceObject将其所属的AssetBundle对象传递过来)，其正式开始加载该asset
                //PS：这里的命名太过随意，”xxMain“无法显著代表内部的执行逻辑
                public void LoadMain(LoadResourceAgent agent, ResourceObject resourceObject)
                {
                    m_ResourceObject = resourceObject;
                    //注意：”ResourceObject“是将实际的”AssetBundle“封装后的对象，其”Target“才是真实的”AssetBundle“
                    agent.Helper.LoadAsset(resourceObject.Target, AssetName, AssetType, IsScene);
                }

                #region 无法从外部直接修改的参数
                public string AssetName
                {
                    get
                    {
                        return m_AssetName;
                    }
                }

                public Type AssetType
                {
                    get
                    {
                        return m_AssetType;
                    }
                }

                public ResourceInfo ResourceInfo
                {
                    get
                    {
                        return m_ResourceInfo;
                    }
                }

                public ResourceObject ResourceObject
                {
                    get
                    {
                        return m_ResourceObject;
                    }
                }

                public int LoadedDependencyAssetCount
                {
                    get
                    {
                        return m_DependencyAssets.Count;
                    }
                }
                #endregion

                #region 提供给扩展子类重写的属性，每个扩展子类根据其自身需求自主重写该属性以代表其特性
                public override string Description
                {
                    get
                    {
                        return m_AssetName;
                    }
                }

                public abstract bool IsScene
                {
                    get;
                }
                #endregion

                #region 提供给外部的public方法，虽然作用与”回调“有些类似，但其用法和普通的public方法一样。
                //主要由该”LoadResourceTaskBase“类型变量从外部调用其public方法来执行相应逻辑
                //同时使用”virtual“关键字提供给”子类“来自由编写执行内部执行逻辑
                public virtual void OnLoadAssetSuccess(LoadResourceAgent agent, object asset, float duration)
                {
                }

                public virtual void OnLoadAssetFailure(LoadResourceAgent agent, LoadResourceStatus status, string errorMessage)
                {
                }

                public virtual void OnLoadAssetUpdate(LoadResourceAgent agent, LoadResourceProgress type, float progress)
                {
                }

                //当需要的依赖资源每成功加载一次，则更新”m_DependencyAssets“集中中的元素
                public virtual void OnLoadDependencyAsset(LoadResourceAgent agent, string dependencyAssetName, object dependencyAsset)
                {
                    m_DependencyAssets.Add(dependencyAsset);
                }
                #endregion

                #region 其他普通属性或方法
                public DateTime StartTime
                {
                    get
                    {
                        return m_StartTime;
                    }
                    set
                    {
                        m_StartTime = value;
                    }
                }

                //代表本asset的所有dependencyAssets中已为其创建了”LoadDependencyTask“的数量
                //因此需要具备”set“和”get“方法
                public int TotalDependencyAssetCount
                {
                    get
                    {
                        return m_TotalDependencyAssetCount;
                    }
                    set
                    {
                        m_TotalDependencyAssetCount = value;
                    }
                }

                public string[] GetDependencyAssetNames()
                {
                    return m_DependencyAssetNames;
                }

                public List<object> GetDependencyAssets()
                {
                    return m_DependencyAssets;
                }
                #endregion
            }
        }
    }
}
