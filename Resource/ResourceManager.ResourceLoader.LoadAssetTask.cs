//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            private sealed class LoadAssetTask : LoadResourceTaskBase
            {
                //本脚本内部使用的回调。但其执行时机由外部决定，即”LoadResourceTaskBase“中定义的”四个public方法“，
                //其调用时机由”LoadResourceTaskBase“类型变量从外部调用其public方法，以此来调用本脚本内部的相应回调
                private LoadAssetCallbacks m_LoadAssetCallbacks;

                //重写基类的”abstract“属性，用来代表本”子类“的特殊性
                public override bool IsScene
                {
                    get
                    {
                        return false;
                    }
                }

                public static LoadAssetTask Create(string assetName, Type assetType, int priority,
                    ResourceInfo resourceInfo, string[] dependencyAssetNames, LoadAssetCallbacks loadAssetCallbacks,
                    object userData)
                {
                    //当”引用池“中没有可用的”LoadAssetCallback“时会使用”new T()“创建新的T类型对象，
                    //此时会调用本脚本的”构造方法public LoadAssetTask()“
                    LoadAssetTask loadAssetTask = ReferencePool.Acquire<LoadAssetTask>();
                    //调用基类的”Initialize“方法
                    loadAssetTask.Initialize(assetName, assetType, priority, resourceInfo, dependencyAssetNames, userData);
                    //同时为本脚本内private变量赋值
                    loadAssetTask.m_LoadAssetCallbacks = loadAssetCallbacks;
                    return loadAssetTask;
                }

                //在”ReferencePool.Acquire“中会调用该构造方法
                //PS：构造方法的本质作用是“为重要的变量赋值赋值”，如果这个变量赋值与否都不影响则完全没必要专门写”构造方法“
                //   如果是”非static“变量，则只要有该类型的实例，则必然包含该变量。无论是否重写”构造方法“，都具备该变量
                //   构造方法仅仅只是为该变量赋值而已
                public LoadAssetTask()
                {
                    m_LoadAssetCallbacks = null;
                }

                //该方法由”引用池系统“执行。当该”LoadAssetTask“对象被使用完后则会执行”ReferencePool.Release“方法回收该对象
                //而其内部会找到本”LoadAssetTask“引用类型所属的”ReferenceCollection“
                //在执行了该引用类型对象"LoadAssetTask"自身的”Clear“方法后将其放入相应的”ReferenceCollection“中
                //本”Clear“方法即在此时执行
                public override void Clear()
                {
                    base.Clear();
                    m_LoadAssetCallbacks = null;
                }

                #region 加载Asset的回调。注意：这些是普通的public方法，其调用由实例化的“LoadAssetTask”对象从外部调用
                //后续的四个回调的执行时机并不在这里
                //这四个是作为“普通的public方法”而存在，所以其调用在于“loadAssetTask”变量从外部调用
                public override void OnLoadAssetSuccess(LoadResourceAgent agent, object asset, float duration)
                {
                    base.OnLoadAssetSuccess(agent, asset, duration);
                    //这里才是为“赋值过的回调”。好处在于“本普通public方法”的调用完全可以由框架内部来执行
                    //而实际的执行逻辑则由各个业务模块来决定。
                    //这样的设计更利于框架的书写
                    if (m_LoadAssetCallbacks.LoadAssetSuccessCallback != null)
                    {
                        m_LoadAssetCallbacks.LoadAssetSuccessCallback(AssetName, asset, duration, UserData);
                    }
                }

                public override void OnLoadAssetFailure(LoadResourceAgent agent, LoadResourceStatus status, string errorMessage)
                {
                    base.OnLoadAssetFailure(agent, status, errorMessage);
                    if (m_LoadAssetCallbacks.LoadAssetFailureCallback != null)
                    {
                        m_LoadAssetCallbacks.LoadAssetFailureCallback(AssetName, status, errorMessage, UserData);
                    }
                }

                public override void OnLoadAssetUpdate(LoadResourceAgent agent, LoadResourceProgress type, float progress)
                {
                    base.OnLoadAssetUpdate(agent, type, progress);
                    //这里与其他方法相比，增加了一道验证，其实即使没有也没关系。只是已经通过参数传递过来了，因此不用也是浪费而已
                    if (type == LoadResourceProgress.LoadAsset)
                    {
                        if (m_LoadAssetCallbacks.LoadAssetUpdateCallback != null)
                        {
                            m_LoadAssetCallbacks.LoadAssetUpdateCallback(AssetName, progress, UserData);
                        }
                    }
                }

                public override void OnLoadDependencyAsset(LoadResourceAgent agent, string dependencyAssetName, object dependencyAsset)
                {
                    base.OnLoadDependencyAsset(agent, dependencyAssetName, dependencyAsset);
                    if (m_LoadAssetCallbacks.LoadAssetDependencyAssetCallback != null)
                    {
                        m_LoadAssetCallbacks.LoadAssetDependencyAssetCallback(AssetName, dependencyAssetName, LoadedDependencyAssetCount, TotalDependencyAssetCount, UserData);
                    }
                }
                #endregion
            }
        }
    }
}
