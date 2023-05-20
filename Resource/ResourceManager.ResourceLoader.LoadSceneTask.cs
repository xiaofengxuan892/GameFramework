//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            private sealed class LoadSceneTask : LoadResourceTaskBase
            {
                //提供给外部的“回调”，但其赋值只能在“Create”中，并且其调用时机由该“LoadScenTask”类型变量从外部调用本脚本内的“public方法”
                //而后在方法体内部执行该“回调”
                //如此的执行流程主要是为了方便框架编写，并且具有很好的适用性
                private LoadSceneCallbacks m_LoadSceneCallbacks;

                public override bool IsScene
                {
                    get
                    {
                        return true;
                    }
                }

                public static LoadSceneTask Create(string sceneAssetName, int priority, ResourceInfo resourceInfo,
                    string[] dependencyAssetNames, LoadSceneCallbacks loadSceneCallbacks, object userData)
                {
                    LoadSceneTask loadSceneTask = ReferencePool.Acquire<LoadSceneTask>();
                    loadSceneTask.Initialize(sceneAssetName, null, priority, resourceInfo,
                        dependencyAssetNames, userData);
                    loadSceneTask.m_LoadSceneCallbacks = loadSceneCallbacks;
                    return loadSceneTask;
                }

                public LoadSceneTask()
                {
                    m_LoadSceneCallbacks = null;
                }

                public override void Clear()
                {
                    base.Clear();
                    m_LoadSceneCallbacks = null;
                }

                #region 提供给本LoadSceneTask类型变量的public方法，由该类型变量从外部像其他普通的public方法一样调用
                public override void OnLoadAssetSuccess(LoadResourceAgent agent, object asset, float duration)
                {
                    base.OnLoadAssetSuccess(agent, asset, duration);
                    if (m_LoadSceneCallbacks.LoadSceneSuccessCallback != null)
                    {
                        m_LoadSceneCallbacks.LoadSceneSuccessCallback(AssetName, duration, UserData);
                    }
                }

                public override void OnLoadAssetFailure(LoadResourceAgent agent, LoadResourceStatus status, string errorMessage)
                {
                    base.OnLoadAssetFailure(agent, status, errorMessage);
                    if (m_LoadSceneCallbacks.LoadSceneFailureCallback != null)
                    {
                        m_LoadSceneCallbacks.LoadSceneFailureCallback(AssetName, status, errorMessage, UserData);
                    }
                }

                public override void OnLoadAssetUpdate(LoadResourceAgent agent, LoadResourceProgress type, float progress)
                {
                    base.OnLoadAssetUpdate(agent, type, progress);
                    if (type == LoadResourceProgress.LoadScene)
                    {
                        if (m_LoadSceneCallbacks.LoadSceneUpdateCallback != null)
                        {
                            m_LoadSceneCallbacks.LoadSceneUpdateCallback(AssetName, progress, UserData);
                        }
                    }
                }

                public override void OnLoadDependencyAsset(LoadResourceAgent agent, string dependencyAssetName, object dependencyAsset)
                {
                    base.OnLoadDependencyAsset(agent, dependencyAssetName, dependencyAsset);
                    if (m_LoadSceneCallbacks.LoadSceneDependencyAssetCallback != null)
                    {
                        m_LoadSceneCallbacks.LoadSceneDependencyAssetCallback(AssetName, dependencyAssetName, LoadedDependencyAssetCount, TotalDependencyAssetCount, UserData);
                    }
                }
                #endregion
            }
        }
    }
}
