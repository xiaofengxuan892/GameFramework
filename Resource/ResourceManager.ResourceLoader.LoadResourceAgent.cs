//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            /// <summary>
            /// 加载资源代理。
            /// PS：该agent负责处理的是“LoadResourceTaskBase”类型的对象
            /// </summary>
            private sealed partial class LoadResourceAgent : ITaskAgent<LoadResourceTaskBase>
            {
                private LoadResourceTaskBase m_Task;

                //这参数是真鸡肋，仅仅只是因为部分功能会用到整个“资源模块”的“Helper”辅助器，因此这里提供本参数“m_ResourceHelper”，
                //在创建“LoadResourceAgent”对象时会为该参数赋值
                private readonly IResourceHelper m_ResourceHelper;
                //该“agent”自身对应的”helper“。需要跟上面的”整个模块的总helper“分开来，不能混淆
                private readonly ILoadResourceAgentHelper m_Helper;

                private static readonly Dictionary<string, string> s_CachedResourceNames = new Dictionary<string, string>(StringComparer.Ordinal);
                //以下两个参数仅仅代表该asset或所属的AssetBundle当前是否正在加载中，只有其正在加载中，才会存在本集合中
                //注意：当该asset或AssetBundle已经加载结束或者加载失败(通过回调知道)，
                //那么会从”s_LoadingAssetNames“，”s_LoadingResourceNames“集合中移除该元素
                private static readonly HashSet<string> s_LoadingAssetNames = new HashSet<string>(StringComparer.Ordinal);
                private static readonly HashSet<string> s_LoadingResourceNames = new HashSet<string>(StringComparer.Ordinal);

                //加载该asset所属的AssetBundle时会用到的相关参数
                private readonly ResourceLoader m_ResourceLoader;
                private readonly string m_ReadOnlyPath;
                private readonly string m_ReadWritePath;
                //加载资源完成后，对于”加密过“的资源则需要将其解密
                private readonly DecryptResourceCallback m_DecryptResourceCallback;

                /// <summary>
                /// 初始化加载资源代理的新实例。
                /// </summary>
                /// <param name="loadResourceAgentHelper">加载资源代理辅助器。</param>
                /// <param name="resourceHelper">资源辅助器。</param>
                /// <param name="resourceLoader">加载资源器。</param>
                /// <param name="readOnlyPath">资源只读区路径。</param>
                /// <param name="readWritePath">资源读写区路径。</param>
                /// <param name="decryptResourceCallback">解密资源回调函数。</param>
                public LoadResourceAgent(ILoadResourceAgentHelper loadResourceAgentHelper, IResourceHelper resourceHelper,
                    ResourceLoader resourceLoader, string readOnlyPath, string readWritePath,
                    DecryptResourceCallback decryptResourceCallback)
                {
                    //以下四个参数”LoadResourceAgent“的逻辑中都会使用到，所以这里提前检测
                    if (loadResourceAgentHelper == null)
                    {
                        throw new GameFrameworkException("Load resource agent helper is invalid.");
                    }

                    if (resourceHelper == null)
                    {
                        throw new GameFrameworkException("Resource helper is invalid.");
                    }

                    if (resourceLoader == null)
                    {
                        throw new GameFrameworkException("Resource loader is invalid.");
                    }

                    if (decryptResourceCallback == null)
                    {
                        throw new GameFrameworkException("Decrypt resource callback is invalid.");
                    }

                    m_Helper = loadResourceAgentHelper;
                    m_ResourceHelper = resourceHelper;
                    m_ResourceLoader = resourceLoader;
                    m_ReadOnlyPath = readOnlyPath;
                    m_ReadWritePath = readWritePath;
                    m_DecryptResourceCallback = decryptResourceCallback;
                    m_Task = null;
                }

                #region 本agent脚本核心方法
                /// <summary>
                /// 开始处理加载资源任务。
                /// </summary>
                /// <param name="task">要处理的加载资源任务。</param>
                /// <returns>开始处理任务的状态。</returns>
                public StartTaskStatus Start(LoadResourceTaskBase task)
                {
                    if (task == null)
                    {
                        throw new GameFrameworkException("Task is invalid.");
                    }

                    m_Task = task;
                    //从外部设置该”xxTaskBase“的”StartTime“。
                    //PS：由于创建该”LoadResourceTaskBase“对象时只是将其加入”任务池系统“中。之后会经由”任务池系统的Update轮询“来执行
                    //   池子中的各个”xxTaskBase“。因此创建”LoadResourceTaskBase“对象时该任务并未开始，所以无法设置其”startTime“
                    //   基于此原因，”LoadResourceTaskBase“中的”StartTime“属性提供”get“和”set“方法
                    //   (注意：”TaskBase“基类本身并没有”StartTime“属性，该属性是”LoadResourceTaskBase“自身特有的)
                    m_Task.StartTime = DateTime.UtcNow;

                    #region 先排除该asset的三种特殊情况：
                    //情况1.该asset所属的AssetBundle当前并没有下载到本地
                    ResourceInfo resourceInfo = m_Task.ResourceInfo;
                    //该资源在本地并没有(这里其实包含了UpdatableWhilePlaying模式下的资源的“notReady”情况)
                    if (!resourceInfo.Ready)
                    {
                        m_Task.StartTime = default(DateTime);
                        return StartTaskStatus.HasToWait;
                    }

                    //情况2.该资源正在加载中，则等待
                    if (IsAssetLoading(m_Task.AssetName))
                    {
                        //这里赋值的”default“其实没有任何意义
                        m_Task.StartTime = default(DateTime);
                        return StartTaskStatus.HasToWait;
                    }
                    //执行到这里，说明该asset所属的”AssetBundle“在本地有”下载好的文件“，并且当前该”asset“并没有”正处在从AB中加载到内存里“的过程中

                    //情况3.该asset已经存在内存当中，因此直接使用对象池从其中spawn即可
                    if (!m_Task.IsScene)
                    {
                        AssetObject assetObject = m_ResourceLoader.m_AssetPool.Spawn(m_Task.AssetName);
                        //代表该assetName在”对象池“中已经有现成的”internalObject“了
                        if (assetObject != null)
                        {
                            //由于对象池中已经有现成的internalObject，因此直接使用该assetObject即可。
                            //注意：internalObject是对象池系统内部使用的对象，当使用Spawn获取后提供给外部使用时，此时的”assetObject“
                            //    则是”ObjectBase“对象
                            OnAssetObjectReady(assetObject);
                            return StartTaskStatus.Done;
                        }
                    }
                    //注意：如果是“场景scene”，那么即使其在“AssetPool对象池”中也是需要“重新加载”的，只是该“场景scene”所属的AB并不需要重新加载了
                    #endregion

                    #region 正式开始加载资源
                    //能执行到这里，说明：
                    //1.本地读写区或只读区有该asset所属的AssetBundle
                    //2.该asset当前并不是正在加载中
                    //3.对象池中没有该assetName对应的internalObject
                    //因此正式开始加载该asset：
                    //第一步.首先加载其依赖资源(注意：这里第一步并不是加载该asset所属的AssetBundle，而是先加载其所依赖的其他asset)
                    foreach (string dependencyAssetName in m_Task.GetDependencyAssetNames())
                    {
                        //说明所依赖的assets尚且没有加载完成，需要等待。
                        //只有等到“依赖的资源”加载完成后(此时则可以直接从对象池中获取该依赖asset对应的internalObject了)
                        if (!m_ResourceLoader.m_AssetPool.CanSpawn(dependencyAssetName))
                        {
                            m_Task.StartTime = default(DateTime);
                            return StartTaskStatus.HasToWait;
                        }
                    }
                    //能执行到这里说明：该assetName所依赖的资源都已加载到内存中

                    //第二步.开始加载该asset所属的AssetBundle
                    //加载asset分为三个阶段：加载所属AssetBundle到内存
                    //                   ==》从AssetBundle中加载目标Asset到内存(正在加载中，从”s_LoadingAssetNames“集合来判定)
                    //                       —— 该阶段可能花费一定时间，因此这里单独提取出来作为一个独立阶段来判定
                    //                   ==》该asset已在内存中(则可以使用”对象池的spawn“直接获取)
                    //首先需要检测该asset所属的AssetBundle是否已经加载到内存中
                    string resourceName = resourceInfo.ResourceName.Name;
                    //如果该asset所属的AssetBundle当前正在加载中，则只能等待(注意：resourceInfo.Ready只说明其下载到本地了而已)
                    if (IsResourceLoading(resourceName))
                    {
                        m_Task.StartTime = default(DateTime);
                        return StartTaskStatus.HasToWait;
                    }

                    //执行到这里说明该asset所属的AssetBundle当前并没有正在加载中，
                    //注意：只有在该asset”正在加载中“时，其才会存在”s_LoadingAssetNames“集合中。
                    //     当该asset加载结束(无论加载成功或失败，都会从集合中移除该元素)
                    s_LoadingAssetNames.Add(m_Task.AssetName); //因为正式开始加载该asset，所以将其加入集合中
                    //加载asset的第一步：加载其所属的AssetBundle
                    //这里首先检测其是否已经在内存中(因为上述的”IsResourceLoading“仅仅只是确定该AssetBundle是否正在加载中，所以存在两种情况：1.完全没加载过  2.当前已经在内存中)
                    ResourceObject resourceObject = m_ResourceLoader.m_ResourcePool.Spawn(resourceName);
                    if (resourceObject != null)
                    {
                        //如果该”AssetBundle“当前已在"ResourcePool"对象池中，则直接使用该AssetBundle加载(包括加载scene和普通的asset资源都可以)
                        OnResourceObjectReady(resourceObject);
                        return StartTaskStatus.CanResume;
                    }
                    //能执行到这里，说明只有一种情况：该asset所属的AssetBundle没有存在内存中，所以需要”加载“。
                    //因此先加入”s_LoadingResourceNames“集合中，代表其特殊状态，避免反复加载同一个AssetBundle到内存中
                    s_LoadingResourceNames.Add(resourceName);

                    //正式加载”AssetBundle“的过程：
                    //读取方式：不论该AssetBundle是否使用”fileSystem“，均使用”AssetBundle.LoadFromFileAsync“读取指定路径的文件
                    //这里”s_CachedResourceNames“集合其实完全没必要，唯一的作用仅仅只是”避免再次拼接该AssetBundle的本地存储路径“
                    //这作用其实很有限。并且如果该AssetBundle如果使用了”fileSystem“，则不需要读取此拼接的”fullPath“
                    string fullPath = null;
                    if (!s_CachedResourceNames.TryGetValue(resourceName, out fullPath))
                    {
                        fullPath = Utility.Path.GetRegularPath(Path.Combine(resourceInfo.StorageInReadOnly
                            ? m_ReadOnlyPath : m_ReadWritePath,
                            resourceInfo.UseFileSystem ? resourceInfo.FileSystemName : resourceInfo.ResourceName.FullName));
                        s_CachedResourceNames.Add(resourceName, fullPath);
                    }

                    //根据以下逻辑可知：
                    //若”LoadFromFile“，则使用”AssetBundle.LoadFromFileAsync“读取指定path的文件 —— ”agentHelper“中的”ReadFile“方法
                    //若”LoadFromMemory“，则使用”UnityWebRequest.Get“读取”file:///“前缀的本地文件 —— ”agentHelper“中的”ReadBytes“方法
                    if (resourceInfo.LoadType == LoadType.LoadFromFile)
                    {
                        //以下无论是否使用”fileSystem“，都使用”AssetBundle.LoadFromFileAsync“读取指定path的文件
                        if (resourceInfo.UseFileSystem)
                        {
                            IFileSystem fileSystem = m_ResourceLoader.m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName,
                                resourceInfo.StorageInReadOnly);
                            m_Helper.ReadFile(fileSystem, resourceInfo.ResourceName.FullName);
                        }
                        else
                        {
                            m_Helper.ReadFile(fullPath);
                        }
                    }
                    else if (resourceInfo.LoadType == LoadType.LoadFromMemory
                             || resourceInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt
                             || resourceInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt)
                    {
                        //不论其是否使用“fileSytem”，该“fullPath”都必为“本地文件的路径”
                        //若使用UnityWebRequest.Get/Post，则会将”fullPath“包装成”file:///“前缀的远程路径
                        if (resourceInfo.UseFileSystem)
                        {
                            IFileSystem fileSystem = m_ResourceLoader.m_ResourceManager.GetFileSystem(resourceInfo.FileSystemName, resourceInfo.StorageInReadOnly);
                            m_Helper.ReadBytes(fileSystem, resourceInfo.ResourceName.FullName);
                        }
                        else
                        {
                            m_Helper.ReadBytes(fullPath);
                        }
                    }
                    else
                    {
                        throw new GameFrameworkException(Utility.Text.Format("Resource load type '{0}' is not supported.", resourceInfo.LoadType));
                    }
                    //扩展：这里不包含“二进制文件”的情况，只存在“LoadFromFile”或“LoadFromMemory”
                    //    因为”二进制文件“的读取不通过”任务池系统“，都是直接就读取了

                    //"canResume"代表该任务可以继续执行，无需等待(因为上述的读取AssetBundle都是从本地读取，因此过程极快，并不需要占用太长时间)
                    return StartTaskStatus.CanResume;

                    //注意：执行到这里仅仅只是加载该asset所属的AsseetBundle而已，并没有加载到该asset。
                    //但在得到”加载AssetBundle“的回调前，”s_LoadingResourceNames“，”s_LoadingAssetNames“集合中必然会包含该元素
                    //因此虽然TaskStatus为”CanResume“，但依然需要继续等待(那时会切换为”HasToWait“状态)
                    #endregion
                }

                //目标asset加载成功(或者是从”对象池“中直接获取的”已有internalObject“，或者是根据加载出来的asset封装之后得到的"AssetObject")
                private void OnAssetObjectReady(AssetObject assetObject)
                {
                    m_Helper.Reset();

                    //由于是”资源模块“的对象池系统，因此这里的”objectBase.Target“实际是”AssetBundle中的asset本身“
                    object asset = assetObject.Target;
                    if (m_Task.IsScene)
                    {
                        //此时的“assetObject.Target”其实只是一个“毫无内容的SceneObject”对象
                        //这里记录下来该“target”其实应该在“SceneManager.LoadSceneAsync”的回调中直接从本集合中查找目标“sceneObject”
                        //而不应该又重新创建一个新的“SceneObject”对象 —— 这是浪费
                        m_ResourceLoader.m_SceneToAssetMap.Add(m_Task.AssetName, asset);
                    }

                    //无论是为”scene“还是普通的”asset“对象，都会执行本方法，即”asset加载成功“，
                    //此时会执行各个任务如”LoadScene/Asset/DependecnyTask“中的回调
                    m_Task.OnLoadAssetSuccess(this, asset, (float)(DateTime.UtcNow - m_Task.StartTime).TotalSeconds);
                    //由于”对象池“中有现成的asset对应的internalObject，因此加载此asset的Task可以直接结束
                    m_Task.Done = true;
                }

                private void OnResourceObjectReady(ResourceObject resourceObject)
                {
                    m_Task.LoadMain(this, resourceObject);
                }
                #endregion

                #region 监听”加载资源“各类事件的执行逻辑
                //以下所有事件的监听全部由”LoadResourceAgentHelper“中的”Update轮询“内部遍历所有的”UnityWebRequest“或”Async加载file“等来决定
                #region 加载asset所属的AssetBundle的回调
                //从本地读取asset所属的”LoadFromFile“类型的AssetBundle的文件结束(包含”使用fileSystem“和”未使用FileSystem“的两种情况)
                private void OnLoadResourceAgentHelperReadFileComplete(object sender, LoadResourceAgentHelperReadFileCompleteEventArgs e)
                {
                    ResourceObject resourceObject = ResourceObject.Create(m_Task.ResourceInfo.ResourceName.Name,
                        e.Resource, m_ResourceHelper, m_ResourceLoader);
                    m_ResourceLoader.m_ResourcePool.Register(resourceObject, true);
                    //由于该”AssetBundle“加载结束，因此必须从”s_LoadingResourceNames“中移除该元素
                    s_LoadingResourceNames.Remove(m_Task.ResourceInfo.ResourceName.Name);
                    //此时”AssetBundle“加载到内存，开始加载其中的目标asset
                    OnResourceObjectReady(resourceObject);
                }

                //从本地读取asset所属的”LoadFromMemory“类型文件结束
                private void OnLoadResourceAgentHelperReadBytesComplete(object sender, LoadResourceAgentHelperReadBytesCompleteEventArgs e)
                {
                    byte[] bytes = e.GetBytes();
                    ResourceInfo resourceInfo = m_Task.ResourceInfo;
                    //若该assetBundle加密过(当前仅支持”LoadFromMemory“类型的AB，对于”LoadFromFile“类型的AB则不支持)
                    if (resourceInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt
                        || resourceInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt)
                    {
                        //这里直接将”resourceInfo“变量传递过去多简洁
                        m_DecryptResourceCallback(bytes, 0, bytes.Length, resourceInfo.ResourceName.Name,
                            resourceInfo.ResourceName.Variant, resourceInfo.ResourceName.Extension,
                            resourceInfo.StorageInReadOnly, resourceInfo.FileSystemName, (byte)resourceInfo.LoadType,
                            resourceInfo.Length, resourceInfo.HashCode);
                    }

                    //1.如果是“Decrypt/QuickDecrypt”类型还需要等“解密执行完后”才能开始匹配
                    //2.又开启一次匹配的”xxRequest“(内部调用”AssetBundle.LoadFromMemoryAsync“来匹配bytes数据)，
                    //  同样需要”xxAgentHelper“内部的”update轮询机制“来遍历检测
                    m_Helper.ParseBytes(bytes);
                }

                //针对”LoadFromMemory“类型的AssetBundle，在加载完成后还需要”解密匹配“(匹配过程也是”Async“的)
                //因此这里是”匹配完成“后的回调
                //针对”LoadFromMemory“类型的AssetBundle，此时才是真正的加载到内存了。然后就可以从中加载目标asset了，即执行”OnResourceObjectReady“方法
                private void OnLoadResourceAgentHelperParseBytesComplete(object sender, LoadResourceAgentHelperParseBytesCompleteEventArgs e)
                {
                    ResourceObject resourceObject = ResourceObject.Create(m_Task.ResourceInfo.ResourceName.Name, e.Resource, m_ResourceHelper, m_ResourceLoader);
                    m_ResourceLoader.m_ResourcePool.Register(resourceObject, true);
                    //由于该assetBundle已经加载结束，所以从”s_LoadingResourceNames“中移除该元素
                    s_LoadingResourceNames.Remove(m_Task.ResourceInfo.ResourceName.Name);
                    OnResourceObjectReady(resourceObject);
                }
                #endregion

                #region 从AssetBundle中加载目标asset的回调
                //从AssetBundle中加载目标asset结束
                private void OnLoadResourceAgentHelperLoadComplete(object sender, LoadResourceAgentHelperLoadCompleteEventArgs e)
                {
                    //能执行本回调，证明已经从”目标asset所属的AssetBundle“中成功的加载出该asset了
                    //但是加载目标asset的方式可能是“assetBundle.LoadAssetAysnc”或者“SceneManager.LoadSceneAysnc”
                    //两种情况下得到的"e.Asset"是不同的：
                    //如果是前者，那么此时“e.Asset”则是“从assetBundle中加载到的asset本身”
                    //而如果是后者，则“e.Asset”是新封装的类型“SceneAsset”的一个变量而已(该类型内部是空的，相当于只是起到“标识区分”的作用)
                    AssetObject assetObject = null;
                    //这里其实完全没必要针对“场景scene”做处理。在创建“sceneObject对象”时加个验证即可，而不要在这里加。这里处理的话实在是太不伦不类了
                    if (m_Task.IsScene)
                    {
                        assetObject = m_ResourceLoader.m_AssetPool.Spawn(m_Task.AssetName);
                    }
                    //将这里加载得到的“e.Asset”作为“AssetObject.Target”，所以从某种角度看“AssetObject.Target”本身并不重要
                    //重要的是对象池需要用到的“AssetObject”本身
                    //就出现了一个特殊的判断情况：
                    //在“LoadResourceAgent.Start”方法中需要提前检测该asset在“m_AssetPool”中是否有现成的“AssetObject”，如果有则直接使用该AssetObject
                    //但是这里添加了“m_Task.IsScene”的判断，专门把“scene”场景排除在“对象池”检测范围外
                    //也是这个原因：因为在“m_AssetPool”中为“scene所对应的AssetObject”所存储的“Target”仅仅只是一个“毫无内容的SceneObject”对象
                    //无法通过加载该“毫无内容的SceneObject”来重新加载目标“scene”
                    //而对于普通的“目标asset”，则可以直接通过“AssetObject.Target”来直接使用
                    //问：那为什么又要把“scene”封装成“AssetObject”方法“m_AssetPool”中呢？
                    //答：虽然即使“m_AssetPool”中有“同一scene的AssetName”的“AssetObject”，却依然要重新加载该scene
                    //   但这里为了方便管理(因为这里scene和普通的asset一样都可以打到“AssetBundle”中存储)，
                    //   因此这里其实可以在“scene”情况的“assetObject”参数非null时直接将“e.Asset”中赋值的新的“SceneObject”对象置为null
                    //   这样其实可以释放该新创建的“SceneObject”占用的内存
                    //   但是注意：由于“scene”情况下的“AssetObject.Target”并没有明确价值，所以“OnAssetObjectReady”传递过去时也不要过多的使用该“Target”
                    //   除非在“SceneObject”类型的脚本中新声明一些方法

                    //扩展：1.“SceneObject”的作用与“AssetObject”，“ResourceObject”完全不可同日而语。建议不要使用这样的名字，容易混淆
                    //     2."ResourceLoader.m_SceneToAssetMap"的作用或许在此，即当“SceneManager.LoadSceneAsync”加载场景完成后
                    //       不需要使用“m_SceneToAssetMap”重新创建新的“SceneObject”对象，直接使用本集合中已然存储的“之前创建过的SceneObject”
                    //       这样是不是更好！！！

                    if (assetObject == null)
                    {
                        //如果该asset的依赖dependencyAssets没有加载完成的话，是不是开始加载目标asset的，因此这里可以直接赋值
                        List<object> dependencyAssets = m_Task.GetDependencyAssets();
                        assetObject = AssetObject.Create(m_Task.AssetName, e.Asset, dependencyAssets,
                            m_Task.ResourceObject.Target, m_ResourceHelper, m_ResourceLoader);
                        m_ResourceLoader.m_AssetPool.Register(assetObject, true);
                        //添加”目标asset“和”其所属的AssetBundle“之间的关系，并存储在”m_AssetToResourceMap“集合中
                        m_ResourceLoader.m_AssetToResourceMap.Add(e.Asset, m_Task.ResourceObject.Target);

                        //更新目标asset所在的”AssetBundle“，与目标asset所依赖的其他asset所属的"AssetBundle"之间的”AssetBundle依赖关系“
                        //本质上和”asset之间的依赖关系“类似，但这里是从”目标asset的AssetBundle层级“来展示依赖关系
                        //这里就直接体现了”m_AssetToResourceMap“集合的”重要作用“了
                        foreach (object dependencyAsset in dependencyAssets)
                        {
                            object dependencyResource = null;
                            if (m_ResourceLoader.m_AssetToResourceMap.TryGetValue(dependencyAsset, out dependencyResource))
                            {
                                m_Task.ResourceObject.AddDependencyResource(dependencyResource);
                            }
                            else
                            {
                                throw new GameFrameworkException("Can not find dependency resource.");
                            }
                        }
                    }

                    //由于目标asset已加载完毕，因此从”s_LoadingAssetNames“集合中移除该元素
                    s_LoadingAssetNames.Remove(m_Task.AssetName);
                    OnAssetObjectReady(assetObject);
                }
                #endregion

                #region "加载AB"或“从AB中加载目标asset”均可使用的回调
                //“加载过程中”的回调
                private void OnLoadResourceAgentHelperUpdate(object sender, LoadResourceAgentHelperUpdateEventArgs e)
                {
                    m_Task.OnLoadAssetUpdate(this, e.Type, e.Progress);
                }

                //“加载失败”的回调
                private void OnLoadResourceAgentHelperError(object sender, LoadResourceAgentHelperErrorEventArgs e)
                {
                    OnError(e.Status, e.ErrorMessage);
                }

                private void OnError(LoadResourceStatus status, string errorMessage)
                {
                    m_Helper.Reset();
                    //任何任务”LoadAssetTask/LoadSceneTask/LoadDependencyTask“，都是以目标asset为中心，”其所属的AssetBundle“不过是过程而已
                    m_Task.OnLoadAssetFailure(this, status, errorMessage);
                    s_LoadingAssetNames.Remove(m_Task.AssetName);
                    s_LoadingResourceNames.Remove(m_Task.ResourceInfo.ResourceName.Name);
                    m_Task.Done = true;
                }
                #endregion

                #endregion

                #region ”任务池系统“中会用到的”xxTaskAgent“的部分重要方法
                /// <summary>
                /// 初始化加载资源代理。
                /// </summary>
                public void Initialize()
                {
                    //为该”LoadResourceAgent“的”Helper“的”event事件(观察者模式)“添加”监听“
                    m_Helper.LoadResourceAgentHelperUpdate += OnLoadResourceAgentHelperUpdate;
                    m_Helper.LoadResourceAgentHelperReadFileComplete += OnLoadResourceAgentHelperReadFileComplete;
                    m_Helper.LoadResourceAgentHelperReadBytesComplete += OnLoadResourceAgentHelperReadBytesComplete;
                    m_Helper.LoadResourceAgentHelperParseBytesComplete += OnLoadResourceAgentHelperParseBytesComplete;
                    m_Helper.LoadResourceAgentHelperLoadComplete += OnLoadResourceAgentHelperLoadComplete;
                    m_Helper.LoadResourceAgentHelperError += OnLoadResourceAgentHelperError;
                }

                /// <summary>
                /// 加载资源代理轮询。
                /// </summary>
                /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
                /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
                public void Update(float elapseSeconds, float realElapseSeconds)
                {
                }

                /// <summary>
                /// 关闭并清理加载资源代理。
                /// </summary>
                public void Shutdown()
                {
                    //虽然“LoadResourceAgent”自身没有“FileStream或UnityWebRequest对象”，因此无需实现“IDisposable”接口。但其“m_Helper”包含这些对象
                    //因此这里其实应该将“自身的Reset方法”细分为两种情况：一种是专门提供给“Shutdown释放内存占用”的“m_Helper.Dispose()”，
                    //另一种则是仅仅为了“重置相关参数用的m_Helper.Reset()”
                    Reset();

                    m_Helper.LoadResourceAgentHelperUpdate -= OnLoadResourceAgentHelperUpdate;
                    m_Helper.LoadResourceAgentHelperReadFileComplete -= OnLoadResourceAgentHelperReadFileComplete;
                    m_Helper.LoadResourceAgentHelperReadBytesComplete -= OnLoadResourceAgentHelperReadBytesComplete;
                    m_Helper.LoadResourceAgentHelperParseBytesComplete -= OnLoadResourceAgentHelperParseBytesComplete;
                    m_Helper.LoadResourceAgentHelperLoadComplete -= OnLoadResourceAgentHelperLoadComplete;
                    m_Helper.LoadResourceAgentHelperError -= OnLoadResourceAgentHelperError;
                }

                /// <summary>
                /// 重置加载资源代理。
                /// </summary>
                public void Reset()
                {
                    //1.这里的命名太随意了，应该在命名中增加”agent“关键字，代表是该”LoadResourceAgent“的”helper辅助器“
                    //2.由于“m_Helper”中包含“UnityWebRequest”对象，因此这里在调用应该区分是“Shutdown销毁其资源占用”，还是“简单的重置Reset相关参数”
                    //  如果是前者，则此时应该调用“m_Helper.Dispose()”才行；如果是后者则可以直接调用“m_Helper.Reset()”
                    m_Helper.Reset();
                    m_Task = null;
                }
                #endregion

                #region 提供给外部调用的属性或为某个具体功能而声明的方法(包含public外部调用和private只在内部使用的)
                public static void Clear()
                {
                    s_CachedResourceNames.Clear();  //本集合”s_CachedResourceNames“其实作用极为有限，没有也没关系
                    //以下两个参数代表该asset或者AssetBundle当前是否正在加载中，以避免重复加载同一个asset或AssetBundle到内存中
                    s_LoadingAssetNames.Clear();
                    s_LoadingResourceNames.Clear();
                }

                private static bool IsAssetLoading(string assetName)
                {
                    return s_LoadingAssetNames.Contains(assetName);
                }

                private static bool IsResourceLoading(string resourceName)
                {
                    return s_LoadingResourceNames.Contains(resourceName);
                }

                public ILoadResourceAgentHelper Helper
                {
                    get
                    {
                        return m_Helper;
                    }
                }

                /// <summary>
                /// 获取加载资源任务。
                /// </summary>
                public LoadResourceTaskBase Task
                {
                    get
                    {
                        return m_Task;
                    }
                }
                #endregion
            }
        }
    }
}
