//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.FileSystem;
using GameFramework.ObjectPool;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 加载资源器。
        /// </summary>
        private sealed partial class ResourceLoader
        {
            private readonly ResourceManager m_ResourceManager;

            //直接存储Asset与所属的AssetBundle之间的关系，则这样不用通过”GetAssetInfo/GetResourceInfo“等来获取了
            private readonly Dictionary<object, object> m_AssetToResourceMap;
            //以scene资源的完整路径name为key，以从AssetBundle加载得到的Asset为value，将该场景对应的Asset资源暂存起来，节省再次使用的时间
            //对于“scene”资源并没有使用“对象池系统”，因此基本“scene”资源可以一直存在内存中，除非手动释放(主要为跳转场景时方便)
            private readonly Dictionary<string, object> m_SceneToAssetMap;

            //代表的是该asset被多少个其他的asset所依赖，如果数值为0，则代表其没有被任何其他asset依赖。
            //PS：并不是该asset依赖了多少个其他的asset，而是该asset被多少个其他的asset依赖。不要搞反了
            private readonly Dictionary<object, int> m_AssetDependencyCount;
            private readonly Dictionary<object, int> m_ResourceDependencyCount;

            private readonly LoadBytesCallbacks m_LoadBytesCallbacks;
            //计算hashcode需要用到的参数
            private readonly byte[] m_CachedHashBytes;
            private const int CachedHashBytesLength = 4;
            //专用优化逻辑的方法：任务池、对象池
            private readonly TaskPool<LoadResourceTaskBase> m_TaskPool;
            private IObjectPool<AssetObject> m_AssetPool;
            private IObjectPool<ResourceObject> m_ResourcePool;

            /// <summary>
            /// 初始化加载资源器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public ResourceLoader(ResourceManager resourceManager)
            {
                m_ResourceManager = resourceManager;
                m_TaskPool = new TaskPool<LoadResourceTaskBase>();
                m_AssetDependencyCount = new Dictionary<object, int>();
                m_ResourceDependencyCount = new Dictionary<object, int>();
                m_AssetToResourceMap = new Dictionary<object, object>();
                m_SceneToAssetMap = new Dictionary<string, object>(StringComparer.Ordinal);
                m_LoadBytesCallbacks = new LoadBytesCallbacks(OnLoadBinarySuccess, OnLoadBinaryFailure);
                m_CachedHashBytes = new byte[CachedHashBytesLength];
                m_AssetPool = null;
                m_ResourcePool = null;
            }

            /// <summary>
            /// 通过设置对象池管理器来创建“Asset对象池”以及“Resource对象池”(分别封装成“ResourceLoader”中的“AssetObject”和“ResourceObject”)
            /// </summary>
            /// <param name="objectPoolManager">对象池管理器。</param>
            public void SetObjectPoolManager(IObjectPoolManager objectPoolManager)
            {
                //“资源模块”中的“对象池系统”支持“同一个internalObject被多次获取”
                m_AssetPool = objectPoolManager.CreateMultiSpawnObjectPool<AssetObject>("Asset Pool");
                m_ResourcePool = objectPoolManager.CreateMultiSpawnObjectPool<ResourceObject>("Resource Pool");
            }

            /// <summary>
            /// 增加加载资源代理辅助器。
            /// PS：这傻逼的命名完全有问题，执行逻辑完全是”创建LoadResourceAgent变量”(该代理内部会用到“LoadResourceAgentHelper”)
            /// </summary>
            /// <param name="loadResourceAgentHelper">要增加的加载资源代理辅助器。</param>
            /// <param name="resourceHelper">资源辅助器。</param>
            /// <param name="readOnlyPath">资源只读区路径。</param>
            /// <param name="readWritePath">资源读写区路径。</param>
            /// <param name="decryptResourceCallback">要设置的解密资源回调函数。</param>
            public void AddLoadResourceAgentHelper(ILoadResourceAgentHelper loadResourceAgentHelper,
                IResourceHelper resourceHelper, string readOnlyPath, string readWritePath,
                DecryptResourceCallback decryptResourceCallback)
            {
                if (m_AssetPool == null || m_ResourcePool == null)
                {
                    throw new GameFrameworkException("You must set object pool manager first.");
                }

                LoadResourceAgent agent = new LoadResourceAgent(loadResourceAgentHelper, resourceHelper, this,
                    readOnlyPath, readWritePath, decryptResourceCallback ?? DefaultDecryptResourceCallback);
                //泛型T代表的是某一类型中用到的参数是T，该类型代表的是一种特性
                //从普通逻辑上讲，“任务池”中存放的必然是“任务”(TaskPool<T> where T : TaskBase)，若要向“任务池”中添加元素
                //则必然添加的是“TaskBase”类型的任务，但这里的“agent”显然不是“TaskBase”类型的任务
                //但如果使用“TaskPool”中的其中一个public方法“AddAgent“则没有此限制，这也是此处使用”AddAgent“的合理性
                m_TaskPool.AddAgent(agent);
            }

            /// <summary>
            /// 加载资源器轮询。
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                //任务池的轮询
                m_TaskPool.Update(elapseSeconds, realElapseSeconds);
            }

            /// <summary>
            /// 关闭并清理加载资源器。
            /// </summary>
            public void Shutdown()
            {
                m_TaskPool.Shutdown();
                m_AssetDependencyCount.Clear();
                m_ResourceDependencyCount.Clear();
                m_AssetToResourceMap.Clear();
                m_SceneToAssetMap.Clear();
                LoadResourceAgent.Clear();
            }

            #region 加载“普通文本文件”的AssetBundle资源
            /// <summary>
            /// 异步加载资源。
            /// </summary>
            /// <param name="assetName">要加载资源的名称。</param>
            /// <param name="assetType">要加载资源的类型。</param>
            /// <param name="priority">加载资源的优先级。</param>
            /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
            /// <param name="userData">用户自定义数据。</param>
            public void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData)
            {
                ResourceInfo resourceInfo = null;
                string[] dependencyAssetNames = null;
                if (!CheckAsset(assetName, out resourceInfo, out dependencyAssetNames))
                {
                    //从这里的执行逻辑来看：只要该对象没有下载到本地，则无法加载。但会通过”回调“将”加载失败“的原因进行返回
                    string errorMessage = Utility.Text.Format("Can not load asset '{0}'.", assetName);
                    if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                    {
                        //这里的判断条件”resourceInfo != null && ！resourceInfo.Ready“也是垃圾
                        //只要”resourceInfo == null“，则必然返回结果是”NotExist“
                        //后续的返回完全只需要考虑”resourceInfo.Ready“即可
                        loadAssetCallbacks.LoadAssetFailureCallback(assetName,
                            resourceInfo != null && !resourceInfo.Ready ? LoadResourceStatus.NotReady : LoadResourceStatus.NotExist,
                            errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                //能执行到这里，说明该资源必然在”m_ResourceInfos“中有记录，并且当前或者为”updatableWhilePlaying“模式(可以边玩边下载)，
                //或者该资源已经存储到本地了
                //PS：本方法只适用于加载“普通文本文件”
                if (resourceInfo.IsLoadFromBinary)
                {
                    string errorMessage = Utility.Text.Format("Can not load asset '{0}' which is a binary asset.", assetName);
                    if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                    {
                        loadAssetCallbacks.LoadAssetFailureCallback(assetName, LoadResourceStatus.TypeError,
                            errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                LoadAssetTask mainTask = LoadAssetTask.Create(assetName, assetType, priority, resourceInfo,
                    dependencyAssetNames, loadAssetCallbacks, userData);
                foreach (string dependencyAssetName in dependencyAssetNames)
                {
                    //只要有一层“子依赖资源”在“本地没有”，则加载失败(排除updatableWhilePlaying的情况)
                    if (!LoadDependencyAsset(dependencyAssetName, priority, mainTask, userData))
                    {
                        string errorMessage = Utility.Text.Format("Can not load dependency asset '{0}' when load asset '{1}'.", dependencyAssetName, assetName);
                        if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                        {
                            loadAssetCallbacks.LoadAssetFailureCallback(assetName, LoadResourceStatus.DependencyError, errorMessage, userData);
                            return;
                        }

                        throw new GameFrameworkException(errorMessage);
                    }
                }

                m_TaskPool.AddTask(mainTask);
                //专门处理“updatableWhilePlaying”的情况
                //这里应该会等待“资源下载完成”后再开始执行“mainTask”，但在“mainTask”内部是如何检测“该资源是否下载完毕”的
                //可能是通过检测“mainTask”内部的“resourceInfo.Ready”状态来确定是否开始执行“mainTask”
                //通过查询“LoadResourceAgent”中的“Start”方法可以证实本想法
                if (!resourceInfo.Ready)
                {
                    m_ResourceManager.UpdateResource(resourceInfo.ResourceName);
                }
            }

            /// <summary>
            /// 异步加载场景(只适用于“普通文本文件”的场景加载，“二进制文件“不适用本方法)
            /// </summary>
            /// <param name="sceneAssetName">要加载场景资源的名称。</param>
            /// <param name="priority">加载场景资源的优先级。</param>
            /// <param name="loadSceneCallbacks">加载场景回调函数集。</param>
            /// <param name="userData">用户自定义数据。</param>
            public void LoadScene(string sceneAssetName, int priority, LoadSceneCallbacks loadSceneCallbacks, object userData)
            {
                ResourceInfo resourceInfo = null;
                string[] dependencyAssetNames = null;
                if (!CheckAsset(sceneAssetName, out resourceInfo, out dependencyAssetNames))
                {
                    string errorMessage = Utility.Text.Format("Can not load scene '{0}'.", sceneAssetName);
                    if (loadSceneCallbacks.LoadSceneFailureCallback != null)
                    {
                        loadSceneCallbacks.LoadSceneFailureCallback(sceneAssetName,
                            resourceInfo != null && !resourceInfo.Ready ? LoadResourceStatus.NotReady : LoadResourceStatus.NotExist,
                            errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                //本方法不适用”二进制格式“的场景资源文件
                if (resourceInfo.IsLoadFromBinary)
                {
                    string errorMessage = Utility.Text.Format("Can not load scene asset '{0}' which is a binary asset.", sceneAssetName);
                    if (loadSceneCallbacks.LoadSceneFailureCallback != null)
                    {
                        loadSceneCallbacks.LoadSceneFailureCallback(sceneAssetName, LoadResourceStatus.TypeError, errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                LoadSceneTask mainTask = LoadSceneTask.Create(sceneAssetName, priority, resourceInfo, dependencyAssetNames, loadSceneCallbacks, userData);
                foreach (string dependencyAssetName in dependencyAssetNames)
                {
                    //”Scene“必然不会依然于其他的”scene“资源，所以这里按普通Asset依赖处理即可
                    if (!LoadDependencyAsset(dependencyAssetName, priority, mainTask, userData))
                    {
                        string errorMessage = Utility.Text.Format("Can not load dependency asset '{0}' when load scene '{1}'.", dependencyAssetName, sceneAssetName);
                        if (loadSceneCallbacks.LoadSceneFailureCallback != null)
                        {
                            loadSceneCallbacks.LoadSceneFailureCallback(sceneAssetName, LoadResourceStatus.DependencyError, errorMessage, userData);
                            return;
                        }

                        throw new GameFrameworkException(errorMessage);
                    }
                }

                m_TaskPool.AddTask(mainTask);
                //当设置”m_ResourceMode“为”updatableWhilePlaying“时，所有的”AssetBundle“都会受此影响，只有在使用到该AB时才会下载
                if (!resourceInfo.Ready)
                {
                    m_ResourceManager.UpdateResource(resourceInfo.ResourceName);
                }
            }

            private bool LoadDependencyAsset(string assetName, int priority, LoadResourceTaskBase mainTask, object userData)
            {
                if (mainTask == null)
                {
                    throw new GameFrameworkException("Main task is invalid.");
                }

                ResourceInfo resourceInfo = null;
                string[] dependencyAssetNames = null;
                //只有本地有该asset所属的AssetBundle才可以加载(包含updatableWhilePlaying的情况)
                if (!CheckAsset(assetName, out resourceInfo, out dependencyAssetNames))
                {
                    return false;
                }

                if (resourceInfo.IsLoadFromBinary)
                {
                    return false;
                }

                //“依赖资源”的“依赖” —— 递归“依赖资源”的检测
                //只要其中有一个子层级的“依赖资源”未下载到本地，则加载失败(updatableWhilePlaying的情况排除在外)
                LoadDependencyAssetTask dependencyTask = LoadDependencyAssetTask.Create(assetName, priority,
                    resourceInfo, dependencyAssetNames, mainTask, userData);
                foreach (string dependencyAssetName in dependencyAssetNames)
                {
                    if (!LoadDependencyAsset(dependencyAssetName, priority, dependencyTask, userData))
                    {
                        return false;
                    }
                }
                //这里其实应该加一个“如果创建依赖asset的任务失败，则将本任务的LoadResourceTaskBase对象回收”的逻辑
                //if(loadDependencyAssetFailure){ ReferencePool.Release(本LoadResourceTaskBase对象) }
                //否则只要有一层的任意一个asset失败，则所有的“LoadDependencyAssetTask对象”都会散落各地，实在占用内存

                m_TaskPool.AddTask(dependencyTask);
                //此时会专门用来判断“updatableWhilePlaying”的情况
                if (!resourceInfo.Ready)
                {
                    //该方法会直接下载，而不会等待“resourceUpdater”中的“update轮询”，但也依然是通过“DownloadManager”中的“任务池”来下载
                    //而任务池的执行其实还是通过“update轮询”触发的
                    m_ResourceManager.UpdateResource(resourceInfo.ResourceName);
                }

                return true;
            }

            /// <summary>
            /// 卸载资源。
            /// PS：这里传递过来的“asset”是“GameObject”本身，也即一个个需要加载“asset”对象本身
            /// </summary>
            /// <param name="asset">要卸载的资源。</param>
            public void UnloadAsset(object asset)
            {
                //直接通过“GameObject”本身来调用“对象池的unspawn”方法 ==》调用internalObject.Unspawn ==》objectBase.Upspawn
                //也即本模块中的“resourceLoader.AssetObject”的“unspawn”方法
                m_AssetPool.Unspawn(asset);
            }

            /// <summary>
            /// 异步卸载场景。
            /// PS：对于”scene“资源则没有使用”对象池系统“管理
            /// </summary>
            /// <param name="sceneAssetName">要卸载场景资源的名称。</param>
            /// <param name="unloadSceneCallbacks">卸载场景回调函数集。</param>
            /// <param name="userData">用户自定义数据。</param>
            public void UnloadScene(string sceneAssetName, UnloadSceneCallbacks unloadSceneCallbacks, object userData)
            {
                if (m_ResourceManager.m_ResourceHelper == null)
                {
                    throw new GameFrameworkException("You must set resource helper first.");
                }

                object asset = null;
                //"m_SceneToAssetMap"仅仅用于存储scene资源文件，为了切换场景能快速些
                if (m_SceneToAssetMap.TryGetValue(sceneAssetName, out asset))
                {
                    m_SceneToAssetMap.Remove(sceneAssetName);
                    //scene的资源也在“对象池系统”中。
                    //如果是这样的话，“m_SceneToAssetMap”的作用实在是完全没有体现出来，
                    //而且如果要卸载场景的资源，直接从“m_AssetPool”中查找即可，完全没必要从“m_SceneToAssetMap”中再转一次
                    //除非说，即使该scene的asset资源的internalObject的引用为0，也完全没地方会依赖该场景asset，
                    //即使是这样，也依然不释放该scene的asset资源。但是也不用通过“m_SceneToAssetMap”来实现这种效果啊
                    m_AssetPool.Unspawn(asset);

                    //对于“对象池系统”，在卸载某个asset时，这里传递过来的“asset”参数是“实际的asset对象本身”
                    //因此如果是“卸载场景scene”，那此时的“asset”参数代表的则必然是“虚假的，没有内容的，仅仅起标识作用的 sceneAsset对象”而已
                    //在这种情况强制Release有必要吗？并且如果不强制Release，后续如果要用到该“虚假asset”，还可以直接使用，这多好
                    //再者，这里之所以可以直接Release该场景scene的“虚假asset”，是因为可以肯定“对于场景scene对象，其必然不可能被其他的asset所依赖”
                    //所以这里不需要等待“对象池系统”内部逻辑驱动，直接在这里就可以调用“对象池系统专属的ReleaseObject”方法来专门释放某个asset
                    //但是从“整个对象池系统及其应用逻辑”来看，这里不建议在外部直接调用“ReleaseObject”方法
                    m_AssetPool.ReleaseObject(asset);
                }
                else
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not find asset of scene '{0}'.", sceneAssetName));
                }

                //问题1：与“普通asset”相比，在“Unload”后为什么还要专门的调用“SceneManager.UnloadSceneAsync”方法卸载“指定scene”
                //解答：“普通asset”在调用“Unload”方法“卸载”前，其“被使用的地方如UI等”已经被处理过了，如直接已经“Close”掉指定UI，所以剩下的只需要“卸载该UI使用到的asset”而已
                //    而”场景scene“则不同，其在调用”本UnloadScene“方法前，该”场景scene“基本没有处理。
                //    外部是使用这个方法来处理”跟场景scene“相关的逻辑。
                //    所以在处理了”该场景scene使用到的虚假asset“后，还需要专门”使用SceneManager.UnloadSceneAsync“来卸载该场景
                //
                //但是从”本质来讲“：”Resource模块“处理的是”asset的加载和卸载“，是站在”物理资源本身的角度“来看待，而不是该”asset的真实使用逻辑“来看待
                //而这里的”卸载真实的场景scene“其实已经”进入asset的应用情景“里了。所以放在这里不合适
                //问题：为什么在”加载场景scene的asset“时，又会使用”SceneManager.LoadSceneAsync“来处理”场景scene的应用情景“呢？
                //解答：使用“SceneManager.LoadSceneAsync”的原本目的只是为了“加载场景scene的物理asset对象”，但“场景scene”又没有“任何形式的可以代表scene的物理载体对象”，
                //     因此即使使用“SceneManager.LoadSceneAsync”方法也无法得到“scene的真实物理载体对象”
                //     并且该方法还直接控制了“scene在应用层面上的显示”
                //     当前又没有其他任何方法可以“达到仅仅只是加载场景scene的物理载体对象”而又不会“控制scene在应用层面上的显示”的效果
                //     因此也就只能使用“SceneManager.LoadSceneAsync”了。
                //     虽然看起来不伦不类，但基于当前机制，只能如此了
                //解决方案：应该将以下“SceneManager.UnloadSceneAsync”的逻辑放置在“专属的SceneComponent模块”中调用
                //        这里只处理该“场景scene”的“物理对象本身asset的卸载”。
                //        由于“场景scene”其实没有“能够完全代表其所有数据的物理对象载体”，所以其实也没必要处理。
                //        但使用“m_AssetPool.Unspawn”回收“虚假sceneObject”还是有一点作用的
                m_ResourceManager.m_ResourceHelper.UnloadScene(sceneAssetName, unloadSceneCallbacks, userData);
            }

            #endregion

            #region 二进制格式的AssetBundle资源
            /// <summary>
            /// 关键方法：异步加载二进制资源，分成从“文件系统”中加载该二进制数据(使用FileSystem模块中的方法读取数据)，
            /// 和从“二进制文件”本身加载数据(使用LoadBytes方法 —— 借助UnityWebRequest加载本地文件，提供加载回调)
            /// </summary>
            /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
            /// <param name="loadBinaryCallbacks">加载二进制资源回调函数集。</param>
            /// <param name="userData">用户自定义数据。</param>
            public void LoadBinary(string binaryAssetName, LoadBinaryCallbacks loadBinaryCallbacks, object userData)
            {
                #region 此段逻辑其实和“普通AB”的“CheckAsset”作用基本一样，都是为了确定该AB在本地是否有下载好的文件
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    //“asset”所在的“AssetBundle”是以“二进制格式”存储
                    string errorMessage = Utility.Text.Format("Can not load binary '{0}' which is not exist.", binaryAssetName);
                    if (loadBinaryCallbacks.LoadBinaryFailureCallback != null)
                    {
                        //这里的“NotExist”指的是该asset所在的“AssetBundle”并不存在
                        loadBinaryCallbacks.LoadBinaryFailureCallback(binaryAssetName,
                            LoadResourceStatus.NotExist, errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                //这里逻辑有问题：完全没考虑“updatableWhilePlaying”导致没Ready的情况
                //还有一种可能：使用“LoadFromBinary”相关类型的AB基本可以断定是属于“进入游戏必须要具备的资源”，因此其一定要在“热更新界面”全部下载完毕，否则不能进入游戏
                //所以这里忽略“UptableWhilePlaying”模式也就可以说通了
                if (!resourceInfo.Ready)
                {
                    string errorMessage = Utility.Text.Format("Can not load binary '{0}' which is not ready.", binaryAssetName);
                    if (loadBinaryCallbacks.LoadBinaryFailureCallback != null)
                    {
                        loadBinaryCallbacks.LoadBinaryFailureCallback(binaryAssetName,
                            LoadResourceStatus.NotReady, errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }
                #endregion

                //该加载方法只适用于使用“二进制格式”存储的文件
                if (!resourceInfo.IsLoadFromBinary)
                {
                    string errorMessage = Utility.Text.Format("Can not load binary '{0}' which is not a binary asset.", binaryAssetName);
                    if (loadBinaryCallbacks.LoadBinaryFailureCallback != null)
                    {
                        loadBinaryCallbacks.LoadBinaryFailureCallback(binaryAssetName,
                            LoadResourceStatus.TypeError, errorMessage, userData);
                        return;
                    }

                    throw new GameFrameworkException(errorMessage);
                }

                if (resourceInfo.UseFileSystem)
                {
                    //旧版把逻辑放在一句代码里，不合适，这里拆分下
                    byte[] binaryBytes = LoadBinaryFromFileSystem(binaryAssetName);
                    loadBinaryCallbacks.LoadBinarySuccessCallback(binaryAssetName, binaryBytes, 0f, userData);
                }
                else
                {
                    //若未使用“FileSystem”，则说明其在本地有直接的文件(二进制格式的文件)
                    //注意：二进制仅仅是一种数据格式
                    string path = Utility.Path.GetRemotePath(Path.Combine(resourceInfo.StorageInReadOnly ?
                        m_ResourceManager.m_ReadOnlyPath : m_ResourceManager.m_ReadWritePath,
                        resourceInfo.ResourceName.FullName));
                    //LoadBinaryInfo类型仅仅就是“鸡肋”。
                    //这里创建的“LoadBinaryInfo”对象仅仅只是作为“m_LoadBytesCallbakcs”的参数传递数据以及后期校验用的，没啥特别
                    m_ResourceManager.m_ResourceHelper.LoadBytes(path, m_LoadBytesCallbacks,
                        LoadBinaryInfo.Create(binaryAssetName, resourceInfo, loadBinaryCallbacks, userData));
                }
            }

            #region 加载回调，专用于加载“没有配置fileSystemName”的AB独立文件(使用UnityWebRequest加载本地文件“file:///”前缀)
            //对从“本地二进制格式的AB独立文件中读取到的bytes数据”进行解析，然后通知“调用本LoadBinary方法的外部”该消息
            private void OnLoadBinarySuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                LoadBinaryInfo loadBinaryInfo = (LoadBinaryInfo)userData;
                if (loadBinaryInfo == null)
                {
                    throw new GameFrameworkException("Load binary info is invalid.");
                }

                //根据“m_ResourceHelper.LoadBytes”中传入的“LoadBinaryInfo”对象进行解析
                ResourceInfo resourceInfo = loadBinaryInfo.ResourceInfo;
                if (resourceInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt || resourceInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                {
                    DecryptResourceCallback decryptResourceCallback = m_ResourceManager.m_DecryptResourceCallback ?? DefaultDecryptResourceCallback;
                    //这里直接把“resourceInfo”传递过去，由“decryptResourceCallback”内部自由选择要用的参数不是更好吗？
                    decryptResourceCallback(bytes, 0, bytes.Length, resourceInfo.ResourceName.Name,
                        resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension, resourceInfo.StorageInReadOnly,
                        resourceInfo.FileSystemName, (byte)resourceInfo.LoadType, resourceInfo.Length, resourceInfo.HashCode);
                }
                //经过以上步骤后将bytes数组中的数据解密，此时“bytes”中存放的则是“解密之后的字节数据”

                //这里的命名是不是有问题：“OnLoadBinarySuccess”，“LoadBinarySuccessCallback”都是加载二进制文件成功的回调
                //前者是“本脚本中”加载二进制文件的回调，后者则是外部调用本“LoadBinary”方法时传递进来的回调
                //这里只是使用该回调通知外部而已
                loadBinaryInfo.LoadBinaryCallbacks.LoadBinarySuccessCallback(loadBinaryInfo.BinaryAssetName,
                    bytes, duration, loadBinaryInfo.UserData);
                ReferencePool.Release(loadBinaryInfo);
            }

            //加载“本地的二进制格式的AB独立文件”的失败回调
            private void OnLoadBinaryFailure(string fileUri, string errorMessage, object userData)
            {
                LoadBinaryInfo loadBinaryInfo = (LoadBinaryInfo)userData;
                if (loadBinaryInfo == null)
                {
                    throw new GameFrameworkException("Load binary info is invalid.");
                }

                if (loadBinaryInfo.LoadBinaryCallbacks.LoadBinaryFailureCallback != null)
                {
                    loadBinaryInfo.LoadBinaryCallbacks.LoadBinaryFailureCallback(loadBinaryInfo.BinaryAssetName, LoadResourceStatus.AssetError, errorMessage, loadBinaryInfo.UserData);
                }

                ReferencePool.Release(loadBinaryInfo);
            }
            #endregion

            #endregion

            #region 针对”LoadFromBinaryQuickDecrypt/Decrypt“或”LoadFromMemoryQuickDecrypt/Decrypt“加载类型的AB的“解密方法”
            //从执行逻辑来看，该数据的“加密”和“解密”均是通过该AssetBundle的“hashcode”执行的(注意：这里是”解压缩之后“的hashcode)
            private void DefaultDecryptResourceCallback(byte[] bytes, int startIndex, int count, string name,
                string variant, string extension, bool storageInReadOnly, string fileSystem, byte loadType,
                int length, int hashCode)
            {
                //将“hashcode”转换成“bytes”字节数组
                Utility.Converter.GetBytes(hashCode, m_CachedHashBytes);
                //使用如上”转换过的hashCode“对该“bytes字节数组“解密，解密之后的结果会替换掉bytes中所有原有数据
                switch ((LoadType)loadType)
                {
                    case LoadType.LoadFromMemoryAndQuickDecrypt:
                    case LoadType.LoadFromBinaryAndQuickDecrypt:
                        Utility.Encryption.GetQuickSelfXorBytes(bytes, m_CachedHashBytes);
                        break;

                    case LoadType.LoadFromMemoryAndDecrypt:
                    case LoadType.LoadFromBinaryAndDecrypt:
                        Utility.Encryption.GetSelfXorBytes(bytes, m_CachedHashBytes);
                        break;

                    default:
                        throw new GameFrameworkException("Not supported load type when decrypt resource.");
                }

                Array.Clear(m_CachedHashBytes, 0, CachedHashBytesLength);
            }
            #endregion

            #region 任务池代理相关的属性
            /// <summary>
            /// 获取加载资源代理总数量。
            /// </summary>
            public int TotalAgentCount
            {
                get
                {
                    return m_TaskPool.TotalAgentCount;
                }
            }

            /// <summary>
            /// 获取可用加载资源代理数量。
            /// </summary>
            public int FreeAgentCount
            {
                get
                {
                    return m_TaskPool.FreeAgentCount;
                }
            }

            /// <summary>
            /// 获取工作中加载资源代理数量。
            /// </summary>
            public int WorkingAgentCount
            {
                get
                {
                    return m_TaskPool.WorkingAgentCount;
                }
            }

            /// <summary>
            /// 获取等待加载资源任务数量。
            /// </summary>
            public int WaitingTaskCount
            {
                get
                {
                    return m_TaskPool.WaitingTaskCount;
                }
            }
            #endregion

            #region “Asset对象池”，以及“AssetBundle对象池”相关的属性
            /// <summary>
            /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
            /// </summary>
            public float AssetAutoReleaseInterval
            {
                get
                {
                    return m_AssetPool.AutoReleaseInterval;
                }
                set
                {
                    m_AssetPool.AutoReleaseInterval = value;
                }
            }

            /// <summary>
            /// 获取或设置Asset对象池的容量。
            /// </summary>
            public int AssetCapacity
            {
                get
                {
                    return m_AssetPool.Capacity;
                }
                set
                {
                    m_AssetPool.Capacity = value;
                }
            }

            /// <summary>
            /// 获取或设置Asset对象池对象过期秒数。
            /// </summary>
            public float AssetExpireTime
            {
                get
                {
                    return m_AssetPool.ExpireTime;
                }
                set
                {
                    m_AssetPool.ExpireTime = value;
                }
            }

            /// <summary>
            /// 获取或设置Asset对象池的优先级。
            /// </summary>
            public int AssetPriority
            {
                get
                {
                    return m_AssetPool.Priority;
                }
                set
                {
                    m_AssetPool.Priority = value;
                }
            }

            /// <summary>
            /// 获取或设置AssetBundle资源对象池自动释放可释放对象的间隔秒数。
            /// </summary>
            public float ResourceAutoReleaseInterval
            {
                get
                {
                    return m_ResourcePool.AutoReleaseInterval;
                }
                set
                {
                    m_ResourcePool.AutoReleaseInterval = value;
                }
            }

            /// <summary>
            /// 获取或设置AssetBundle资源对象池的容量。
            /// </summary>
            public int ResourceCapacity
            {
                get
                {
                    return m_ResourcePool.Capacity;
                }
                set
                {
                    m_ResourcePool.Capacity = value;
                }
            }

            /// <summary>
            /// 获取或设置AssetBundle资源对象池对象过期秒数。
            /// </summary>
            public float ResourceExpireTime
            {
                get
                {
                    return m_ResourcePool.ExpireTime;
                }
                set
                {
                    m_ResourcePool.ExpireTime = value;
                }
            }

            /// <summary>
            /// 获取或设置AssetBundle资源对象池的优先级。
            /// </summary>
            public int ResourcePriority
            {
                get
                {
                    return m_ResourcePool.Priority;
                }
                set
                {
                    m_ResourcePool.Priority = value;
                }
            }
            #endregion

            #region 故弄玄虚：“FileSystem模块”提供给外部的用于“从指定fileSystem对象中读取指定文件的指定大小的数据”
            //1.这些方法的实质：仅仅只是“fileSystem模块”的“读取数据”的一些应用而已
            //2.这些傻逼的方法完全不需要在内部添加“resourceInfo.IsLoadFromBinary”的判断
            //“FileSystem模块”分成“加载文件所有数据”, “加载该文件部分数据”, “从文件指定位置起开始加载数据”，“从文件指定位置起加载指定数量的数据”
            //但不论是哪种，得到的都只是“字节数组”形式的数据。之后需要使用专属方法将这些“bytes数据”转化

            /// <summary>
            /// 从文件系统中加载二进制资源，并将其解密
            /// </summary>
            /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
            /// <returns>存储加载二进制资源的二进制流。</returns>
            public byte[] LoadBinaryFromFileSystem(string binaryAssetName)
            {
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not exist.", binaryAssetName));
                }

                if (!resourceInfo.Ready)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not ready.", binaryAssetName));
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not a binary asset.", binaryAssetName));
                }

                if (!resourceInfo.UseFileSystem)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not use file system.", binaryAssetName));
                }

                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName, resourceInfo.StorageInReadOnly);
                //不论该文件本身是“二进制格式”，还是“可打印的普通文本文件”，从“fileSytem”中读取数据时使用的都是“ReadFile”
                //并且从“fileSystem”中读取文件数据时直接使用该文件的“FullName”即可(因为fileSystem的写入逻辑即是此)
                byte[] bytes = fileSystem.ReadFile(resourceInfo.ResourceName.FullName);
                if (bytes == null)
                {
                    return null;
                }

                //如果该“二进制文件”还“加密过”则需要将其解密，否则直接返回读取到的“bytes”数据即可
                if (resourceInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt
                    || resourceInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                {
                    //使用“？？”，若前者为null，则直接使用后者
                    DecryptResourceCallback decryptResourceCallback = m_ResourceManager.m_DecryptResourceCallback ?? DefaultDecryptResourceCallback;
                    decryptResourceCallback(bytes, 0, bytes.Length,
                        resourceInfo.ResourceName.Name, resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension,
                        resourceInfo.StorageInReadOnly, resourceInfo.FileSystemName, (byte)resourceInfo.LoadType,
                        resourceInfo.Length, resourceInfo.HashCode);
                }

                return bytes;
            }

            /// <summary>
            /// 从文件系统中加载指定字节数量的二进制资源，并返回实际加载得到的字节数量
            /// </summary>
            /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
            /// <param name="buffer">加载二进制资源的二进制流。</param>
            /// <param name="startIndex">加载二进制资源的二进制流的起始位置。</param>
            /// <param name="length">加载二进制资源的二进制流的长度。</param>
            /// <returns>实际加载了多少字节。</returns>
            public int LoadBinaryFromFileSystem(string binaryAssetName, byte[] buffer, int startIndex, int length)
            {
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not exist.", binaryAssetName));
                }

                if (!resourceInfo.Ready)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not ready.", binaryAssetName));
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not a binary asset.", binaryAssetName));
                }

                if (!resourceInfo.UseFileSystem)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not use file system.", binaryAssetName));
                }

                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName, resourceInfo.StorageInReadOnly);
                //“fileSystem”中的“ReadFile”方法支持直接读取其中的文件(通过resource.FullName)，
                //也支持从resource.FullName读取指定字节数量的数据
                int bytesRead = fileSystem.ReadFile(resourceInfo.ResourceName.FullName, buffer, startIndex, length);
                //解密字节数据(由于buffer为引用类型数据，并且后续逻辑其实是作用于buffer内部的，因此这里不使用out或ref关键字都可以)
                //也是因为该原因，这里需要将buffer数组中存放的数据进行解密(虽然后续只是将“bytesRead”作为方法返回值)
                if (resourceInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt
                    || resourceInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                {
                    DecryptResourceCallback decryptResourceCallback = m_ResourceManager.m_DecryptResourceCallback ?? DefaultDecryptResourceCallback;
                    decryptResourceCallback(buffer, startIndex, bytesRead, resourceInfo.ResourceName.Name,
                        resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension,
                        resourceInfo.StorageInReadOnly, resourceInfo.FileSystemName, (byte)resourceInfo.LoadType,
                        resourceInfo.Length, resourceInfo.HashCode);
                }

                return bytesRead;
            }

            /// <summary>
            /// 从文件系统中加载二进制资源的片段。
            /// </summary>
            /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
            /// <param name="offset">要加载片段的偏移。</param>
            /// <param name="length">要加载片段的长度。</param>
            /// <returns>存储加载二进制资源片段内容的二进制流。</returns>
            public byte[] LoadBinarySegmentFromFileSystem(string binaryAssetName, int offset, int length)
            {
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not exist.", binaryAssetName));
                }

                if (!resourceInfo.Ready)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not ready.", binaryAssetName));
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not a binary asset.", binaryAssetName));
                }

                if (!resourceInfo.UseFileSystem)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not use file system.", binaryAssetName));
                }

                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName, resourceInfo.StorageInReadOnly);
                byte[] bytes = fileSystem.ReadFileSegment(resourceInfo.ResourceName.FullName, offset, length);
                if (bytes == null)
                {
                    return null;
                }

                if (resourceInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt || resourceInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                {
                    DecryptResourceCallback decryptResourceCallback = m_ResourceManager.m_DecryptResourceCallback ?? DefaultDecryptResourceCallback;
                    decryptResourceCallback(bytes, 0, bytes.Length, resourceInfo.ResourceName.Name, resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension, resourceInfo.StorageInReadOnly, resourceInfo.FileSystemName, (byte)resourceInfo.LoadType, resourceInfo.Length, resourceInfo.HashCode);
                }

                return bytes;
            }

            /// <summary>
            /// 从文件系统中加载二进制资源的片段。
            /// </summary>
            /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
            /// <param name="offset">要加载片段的偏移。</param>
            /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
            /// <param name="startIndex">存储加载二进制资源片段内容的二进制流的起始位置。</param>
            /// <param name="length">要加载片段的长度。</param>
            /// <returns>实际加载了多少字节。</returns>
            public int LoadBinarySegmentFromFileSystem(string binaryAssetName, int offset, byte[] buffer, int startIndex, int length)
            {
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not exist.", binaryAssetName));
                }

                if (!resourceInfo.Ready)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not ready.", binaryAssetName));
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not a binary asset.", binaryAssetName));
                }

                if (!resourceInfo.UseFileSystem)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not load binary '{0}' from file system which is not use file system.", binaryAssetName));
                }

                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName, resourceInfo.StorageInReadOnly);
                int bytesRead = fileSystem.ReadFileSegment(resourceInfo.ResourceName.FullName, offset, buffer, startIndex, length);
                if (resourceInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt || resourceInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                {
                    DecryptResourceCallback decryptResourceCallback = m_ResourceManager.m_DecryptResourceCallback ?? DefaultDecryptResourceCallback;
                    decryptResourceCallback(buffer, startIndex, bytesRead, resourceInfo.ResourceName.Name, resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension, resourceInfo.StorageInReadOnly, resourceInfo.FileSystemName, (byte)resourceInfo.LoadType, resourceInfo.Length, resourceInfo.HashCode);
                }

                return bytesRead;
            }
            #endregion

            #region 工具方法
            /// <summary>
            /// 提供给外部用于检测“目标asset”是否在本地有“现成的AB数据”。该方法并不在“AB的加载逻辑中”使用，“AB的加载逻辑”中使用的是“CheckAsset”
            /// 该方法主要提供给外部其他需求来使用
            /// PS：执行逻辑上看，是检测该AB在本地是否有“数据”，但是这个“方法命名HasAsset”实在是垃圾啊
            /// </summary>
            /// <param name="assetName">要检查资源的名称。</param>
            /// <returns>检查资源是否存在的结果。</returns>
            public HasAssetResult HasAsset(string assetName)
            {
                ResourceInfo resourceInfo = GetResourceInfo(assetName);
                if (resourceInfo == null)
                {
                    return HasAssetResult.NotExist;
                }

                //"NotReady"代表的是“无法加载，必须退出”的情况
                if (!resourceInfo.Ready && m_ResourceManager.m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
                {
                    return HasAssetResult.NotReady;
                }

                //这里逻辑有问题：存在该resource并未Ready，但其可以“updatableWhilePlaying”的情况，但此时该Resource在本地并没有资源
                //即其既不存在于“Disk”，也不存在于“FileSystem”
                if (resourceInfo.UseFileSystem)
                {
                    //这傻逼的命名也是够乱：fileSystem对象中存放的是AB，而不是直接的“asset”。“asset”只能包含在“AB”中(一个AB可能包含多个asset)
                    //然后将AB写入“fileSystem对象”中。
                    //所以这里怎么能直接用“Asset”，应该是“HasAssetResult.IncludedInBinaryABOnFileSystem”
                    return resourceInfo.IsLoadFromBinary ? HasAssetResult.BinaryOnFileSystem : HasAssetResult.AssetOnFileSystem;
                }
                else
                {
                    return resourceInfo.IsLoadFromBinary ? HasAssetResult.BinaryOnDisk : HasAssetResult.AssetOnDisk;
                }
            }

            //该方法主要用于检测该asset所数的Resource是否在本地有文件(如果当前资源模式ResourceMode为u庞大table While Playing则直接返回true)
            //返回结果true：m_ResourceInfos中有该资源的记录信息，并且当前的“m_ResourceMode”为“updatableWhilePlaying”，此时无论该资源
            //           在本地是否存在，都返回true。
            //返回resourInfo.Ready： m_ResourceInfos中有该资源的记录信息，但“m_ResourceMode”不是“updatableWhilePlaying”,
            //           则直接返回“resourceInfo.Ready”的情况：若为true，则代表该资源在本地已存在，否则不存在
            private bool CheckAsset(string assetName, out ResourceInfo resourceInfo, out string[] dependencyAssetNames)
            {
                resourceInfo = null;
                dependencyAssetNames = null;

                if (string.IsNullOrEmpty(assetName))
                {
                    return false;
                }

                AssetInfo assetInfo = m_ResourceManager.GetAssetInfo(assetName);
                if (assetInfo == null)
                {
                    return false;
                }

                resourceInfo = m_ResourceManager.GetResourceInfo(assetInfo.ResourceName);
                if (resourceInfo == null)
                {
                    return false;
                }

                dependencyAssetNames = assetInfo.GetDependencyAssetNames();
                //如果当前为“updatableWhilePlaying”，则无论该资源在本地是否存在，都返回true，因为其可以“边玩边下载”
                //否则直接返回“resourceInfo.Ready”即可
                return m_ResourceManager.m_ResourceMode == ResourceMode.UpdatableWhilePlaying ? true : resourceInfo.Ready;
            }

            //通过“ResourceManager”中的“m_AssetInfos”集合来查找到该“assetName”对应的”AssetInfo“
            //而”AssetInfo“对象在创建时会为其赋值”resourceName“参数
            //因此自然也就可以获取到该”assetName“所属的”AssetBundle“的信息 —— ResourceInfo
            private ResourceInfo GetResourceInfo(string assetName)
            {
                if (string.IsNullOrEmpty(assetName))
                {
                    return null;
                }

                AssetInfo assetInfo = m_ResourceManager.GetAssetInfo(assetName);
                if (assetInfo == null)
                {
                    return null;
                }

                return m_ResourceManager.GetResourceInfo(assetInfo.ResourceName);
            }

            /// <summary>
            /// 提供给外部用于获取“二进制格式AB”的本地文件路径(鸡肋方法，居然不支持配置了fileSystemName的AB)
            /// </summary>
            /// <param name="binaryAssetName">要获取实际路径的二进制资源的名称。</param>
            /// <returns>二进制资源的实际路径。</returns>
            /// <remarks>此方法仅适用于二进制资源存储在磁盘（而非文件系统）中的情况。若二进制资源存储在文件系统中时，返回值将始终为空。</remarks>
            public string GetBinaryPath(string binaryAssetName)
            {
                //这里的”binaryAssetName“指的是普通的”assetName“，只是该“asset”所属的“AssetBundle”是以“二进制文件”的格式存储的
                //但其获取方式与普通的”asset“并无不同，皆是通过”m_AssetInfos、m_ResourceInfos“来获取
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    return null;
                }

                if (!resourceInfo.Ready)
                {
                    return null;
                }

                //该方法只适用于”二进制文件“
                if (!resourceInfo.IsLoadFromBinary)
                {
                    return null;
                }

                //如果该”二进制文件“使用了”FileSytem“，则该”二进制文件“没有直接的存储路径(其包含在”文件系统“的文件中)
                if (resourceInfo.UseFileSystem)
                {
                    return null;
                }

                //能执行到这里说明该Resource必然已经存储在本地(存储在”只读区“或”读写区“)
                return Utility.Path.GetRegularPath(Path.Combine(resourceInfo.StorageInReadOnly ?
                        m_ResourceManager.m_ReadOnlyPath : m_ResourceManager.m_ReadWritePath,
                    resourceInfo.ResourceName.FullName));
            }

            /// <summary>
            /// 获取“二进制文件”的本地存储路径(相对路径，只包含最终文件名)——支持“非文件系统”的二进制文件
            /// 注意：这里得到的相对路径只包含最终文件名(如果是未配置fileSytem的二进制文件，则只包含Resource.FullName，并没有专用的二进制文件后缀名)
            /// </summary>
            /// <param name="binaryAssetName">要获取实际路径的二进制资源的名称。</param>
            /// <param name="storageInReadOnly">二进制资源是否存储在只读区中。</param>
            /// <param name="storageInFileSystem">二进制资源是否存储在文件系统中。</param>
            /// <param name="relativePath">二进制资源或存储二进制资源的文件系统，相对于只读区或者读写区的相对路径。</param>
            /// <param name="fileName">若二进制资源存储在文件系统中，则指示二进制资源在文件系统中的名称，否则此参数返回空。</param>
            /// <returns>是否获取二进制资源的实际路径成功。</returns>
            public bool GetBinaryPath(string binaryAssetName, out bool storageInReadOnly, out bool storageInFileSystem, out string relativePath, out string fileName)
            {
                //这傻逼的命名，既然执行逻辑得到的是“relativePath”，那命名中怎么没有“relative”关键字！！！
                storageInReadOnly = false;
                storageInFileSystem = false;
                //这里的”relativePath“分两种情况：
                //1.如果该“二进制文件”的AssetBundle配置了“文件系统”，那么“relativePath”指代其“文件系统”的文件名
                //2.如果没有配置“文件系统”，则“releativePath”是“资源的FullName”
                //  (注意：该二进制文件并没有提供专用文件后缀名，这里只有resource.FullName)
                relativePath = null;
                fileName = null;

                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    return false;
                }

                if (!resourceInfo.Ready)
                {
                    return false;
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    return false;
                }

                storageInReadOnly = resourceInfo.StorageInReadOnly;
                if (resourceInfo.UseFileSystem)
                {
                    storageInFileSystem = true;
                    //当该“二进制文件”的AssetBundle配置了“文件系统”时，则直接使用“文件系统”的名字
                    relativePath = Utility.Text.Format("{0}.{1}", resourceInfo.FileSystemName, DefaultExtension);
                    fileName = resourceInfo.ResourceName.FullName;
                }
                else
                {
                    //若没有配置“文件系统”，则直接使用“resource.FullName”(二进制文件没有专用的文件名后缀)
                    relativePath = resourceInfo.ResourceName.FullName;
                }

                return true;
            }

            /// <summary>
            /// 获取二进制资源的长度。
            /// </summary>
            /// <param name="binaryAssetName">要获取长度的二进制资源的名称。</param>
            /// <returns>二进制资源的长度。</returns>
            public int GetBinaryLength(string binaryAssetName)
            {
                ResourceInfo resourceInfo = GetResourceInfo(binaryAssetName);
                if (resourceInfo == null)
                {
                    return -1;
                }

                if (!resourceInfo.Ready)
                {
                    return -1;
                }

                if (!resourceInfo.IsLoadFromBinary)
                {
                    return -1;
                }

                return resourceInfo.Length;
            }

            /// <summary>
            /// 获取所有加载资源任务的信息。
            /// </summary>
            /// <returns>所有加载资源任务的信息。</returns>
            public TaskInfo[] GetAllLoadAssetInfos()
            {
                return m_TaskPool.GetAllTaskInfos();
            }

            /// <summary>
            /// 获取所有加载资源任务的信息。
            /// </summary>
            /// <param name="results">所有加载资源任务的信息。</param>
            public void GetAllLoadAssetInfos(List<TaskInfo> results)
            {
                m_TaskPool.GetAllTaskInfos(results);
            }

            #endregion

        }
    }
}
