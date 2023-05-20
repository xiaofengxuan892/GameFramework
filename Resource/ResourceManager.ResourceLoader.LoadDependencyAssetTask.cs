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
            private sealed class LoadDependencyAssetTask : LoadResourceTaskBase
            {
                private LoadResourceTaskBase m_MainTask;

                public override bool IsScene
                {
                    get
                    {
                        return false;
                    }
                }

                public static LoadDependencyAssetTask Create(string assetName, int priority, ResourceInfo resourceInfo,
                    string[] dependencyAssetNames, LoadResourceTaskBase mainTask, object userData)
                {
                    LoadDependencyAssetTask loadDependencyAssetTask = ReferencePool.Acquire<LoadDependencyAssetTask>();
                    //参数“dependencyAssetNames”代表本“dependencyAsset”所依赖的其他资源集合
                    loadDependencyAssetTask.Initialize(assetName, null, priority, resourceInfo,
                        dependencyAssetNames, userData);
                    //传递过来的参数”mainTask“有可能是”LoadAssetTask“，也有可能是”LoadSceneTask“
                    //(因为scene也会有其依赖的资源，所以也需要该”LoadDependencyTask“)
                    loadDependencyAssetTask.m_MainTask = mainTask;
                    //更新其”mainTask“ —— LoadAssetTask或LoadSceneTask —— 已创建的“dependencyTask”的数量
                    loadDependencyAssetTask.m_MainTask.TotalDependencyAssetCount++;
                    return loadDependencyAssetTask;
                }

                //当”ReferencePool.Acquire“中该类型引用对象不够时，则会触发本构造方法”new T()“向引用池中加入新的T对象
                public LoadDependencyAssetTask()
                {
                    m_MainTask = null;
                }

                public override void Clear()
                {
                    base.Clear();
                    m_MainTask = null;
                }

                #region 加载某个asset的依赖资源的”public方法(作用类似于回调，但由该LoadDependencyTask变量从外部像普通的pubic方法一样调用)“
                public override void OnLoadAssetSuccess(LoadResourceAgent agent, object asset, float duration)
                {
                    base.OnLoadAssetSuccess(agent, asset, duration);
                    //不论是”LoadAssetTask“，还是”LoadSceneTask“，最终都会执行共同基类”LoadResourceTaskBase“的”OnLoadDependencyAsset“方法
                    //而在基类”LoadResourceTaskBase.OnLoadDependencyAsset“方法中则会为该asset添加其”加载完成的dependencyAsset“
                    //这和之前的预判”基类中的m_DependencyAssets“集合的作用一致，并且更新该集合中元素的时机也一致
                    m_MainTask.OnLoadDependencyAsset(agent, AssetName, asset);
                }

                public override void OnLoadAssetFailure(LoadResourceAgent agent, LoadResourceStatus status, string errorMessage)
                {
                    base.OnLoadAssetFailure(agent, status, errorMessage);
                    //本逻辑有问题：“OnLoadAssetFailure”的作用仅仅代表“本任务所负责的asset”加载失败时执行
                    //这里绝对不应该调用“mainTask”的同名方法，应该在“mainTask”中增加“OnLoadDependencyAssetFailure”方法
                    //现在这样调用虽然也能正常使用，但对于框架的顺畅整洁性很不好
                    m_MainTask.OnLoadAssetFailure(agent, LoadResourceStatus.DependencyError,
                        Utility.Text.Format("Can not load dependency asset '{0}', internal status '{1}', internal error message '{2}'.",
                            AssetName, status, errorMessage));
                }
                #endregion
            }
        }
    }
}
