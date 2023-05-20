//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Download;
using GameFramework.FileSystem;
using GameFramework.ObjectPool;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameFramework.Resource
{
    /// <summary>
    /// 资源管理器。
    /// </summary>
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        #region 各个参数分类
        #region “更新资源”中各个阶段用到的“控制器”以及“加载资源”时需要用到的“关键参数”
        //”单机模式“资源初始化器
        private ResourceIniter m_ResourceIniter;
        //”资源热更新模式“下的“版本列表GameFrameworkVersion.dat”文件“管理器”
        private VersionListProcessor m_VersionListProcessor;
        private ResourceVerifier m_ResourceVerifier;
        private ResourceChecker m_ResourceChecker;
        private ResourceUpdater m_ResourceUpdater;
        private ResourceLoader m_ResourceLoader;
        private IFileSystemManager m_FileSystemManager;  //这里的“FileSystem”是真正的“文件系统模块”中的“FileSystem”

        private Dictionary<string, AssetInfo> m_AssetInfos;
        //保存所有的“AssetBundle”的集合，以包含“AssetBundle全名”的封装类型“ResourceName”为key来存储
        private Dictionary<ResourceName, ResourceInfo> m_ResourceInfos;
        private readonly Dictionary<string, ResourceGroup> m_ResourceGroups;  //资源分组集合
        private SortedDictionary<ResourceName, ReadWriteResourceInfo> m_ReadWriteResourceInfos;
        private readonly Dictionary<string, IFileSystem> m_ReadOnlyFileSystems;
        private readonly Dictionary<string, IFileSystem> m_ReadWriteFileSystems;
        #endregion

        #region 版本列表文件以及用到的各个序列化器
        //远程服务器上的“版本列表文件”，每次“热更新”时都会将服务器上该文件下载下来存储在“PersistentDataPath”中
        //代表服务器上所有最新“资源”的信息
        private const string RemoteVersionListFileName = "GameFrameworkVersion.dat";
        //存储在本地“PerisistentDataPath“中，代表”当前本地已经更新完成的所有资源的信息“。
        //每次在”VerifyResource以及CheckResource“时都会通过比较”PersistentDataPath“中的”GameFrameworkVersion.dat“
        //与”GameFrameworkList.dat“文件来决定还有哪些资源需要继续下载，这样避免每次都”重新下载全部资源“
        //也是基于这样的原因，在”下载服务器上的资源时“，每下载”一定字节数的资源“就需要重新生成一次”GameFrameworkList.dat“文件
        //避免因为中途网络中断导致下载失败，以致”下载记录丢失“导致需要重复下载的情况出现
        private const string LocalVersionListFileName = "GameFrameworkList.dat";
        //针对不同模式及版本的“资源序列化器”。这些序列化器的回调的注册在”ResourceComponent.SetResourceMode“中
        private PackageVersionListSerializer m_PackageVersionListSerializer;
        private UpdatableVersionListSerializer m_UpdatableVersionListSerializer;
        private ResourcePackVersionListSerializer m_ResourcePackVersionListSerializer;
        private ReadOnlyVersionListSerializer m_ReadOnlyVersionListSerializer;
        private ReadWriteVersionListSerializer m_ReadWriteVersionListSerializer;
        #endregion

        #region 更新资源或加载资源时用到的其他一些参数
        //以下都是从“服务器的版本列表文件GameFrameworkVersion.dat”中读取到的“最新版本信息”，即目标版本
        private string m_ApplicableGameVersion;
        private int m_InternalResourceVersion;
        //注意：无论是“编辑器模式/单机模式/可更新模式”，其“只读区路径”都是“streamingAssets路径”
        private string m_ReadOnlyPath;
        //除非“Inspector”面板中特意设定“读写路径”为“temporaryPath”，否则都是“persistentDataPath”
        private string m_ReadWritePath;
        private ResourceMode m_ResourceMode;
        //无论是”单机模式“还是”热更新模式“都有一些公用的参数，如“m_ResourceMode”, "m_ReadOnlyPath", "m_ReadWritePath"等，
        //这些参数只能在”正式启动单机模式或热更新模式的流程“前设置。若已经进入了该模式的流程，则无法改变
        //"m_RefuseSetFlag"即代表当前流程是否已经进入了“对应模式的关键流程”中，若是，则标记“m_RefuseSetFlag = true”
        private bool m_RefuseSetFlag;
        //当前加载资源的目标变体版本：如游戏调整“SD,HD“后会重新登录，此时会加载目标变体版本的资源
        private string m_CurrentVariant;
        private string m_UpdatePrefixUri;
        #endregion

        #region 用到的各个回调
        //单机模式
        private InitResourcesCompleteCallback m_InitResourcesCompleteCallback;

        //热更新模式
        private UpdateVersionListCallbacks m_UpdateVersionListCallbacks;
        private VerifyResourcesCompleteCallback m_VerifyResourcesCompleteCallback;
        private CheckResourcesCompleteCallback m_CheckResourcesCompleteCallback;
        private ApplyResourcesCompleteCallback m_ApplyResourcesCompleteCallback;
        private UpdateResourcesCompleteCallback m_UpdateResourcesCompleteCallback;
        private EventHandler<ResourceVerifyStartEventArgs> m_ResourceVerifyStartEventHandler;
        private EventHandler<ResourceVerifySuccessEventArgs> m_ResourceVerifySuccessEventHandler;
        private EventHandler<ResourceVerifyFailureEventArgs> m_ResourceVerifyFailureEventHandler;
        private EventHandler<ResourceUpdateStartEventArgs> m_ResourceUpdateStartEventHandler;
        private EventHandler<ResourceUpdateChangedEventArgs> m_ResourceUpdateChangedEventHandler;
        private EventHandler<ResourceUpdateSuccessEventArgs> m_ResourceUpdateSuccessEventHandler;
        private EventHandler<ResourceUpdateFailureEventArgs> m_ResourceUpdateFailureEventHandler;
        private EventHandler<ResourceUpdateAllCompleteEventArgs> m_ResourceUpdateAllCompleteEventHandler;
        //应用“资源包”相关回调
        private EventHandler<ResourceApplyStartEventArgs> m_ResourceApplyStartEventHandler;
        private EventHandler<ResourceApplySuccessEventArgs> m_ResourceApplySuccessEventHandler;
        private EventHandler<ResourceApplyFailureEventArgs> m_ResourceApplyFailureEventHandler;
        #endregion

        #region 用到的“工具性质”的参数
        private MemoryStream m_CachedStream;
        private IResourceHelper m_ResourceHelper;
        private DecryptResourceCallback m_DecryptResourceCallback;
        //用到的“常量”参数
        private const string DefaultExtension = "dat";
        private const string TempExtension = "tmp";
        private const int FileSystemMaxFileCount = 1024 * 16;
        private const int FileSystemMaxBlockCount = 1024 * 256;
        #endregion

        #endregion

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        internal override int Priority
        {
            get
            {
                return 3;
            }
        }

        #region 核心方法1：加载或卸载“普通asset”以及“场景scene”，加载二进制AB”LoadBinary“,”LoadBinaryFromFileSystem“,”LoadBinarySegmentFromFileSystem“
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
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, assetType, priority, loadAssetCallbacks, userData);
        }

        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。</param>
        public void UnloadAsset(object asset)
        {
            if (asset == null)
            {
                throw new GameFrameworkException("Asset is invalid.");
            }

            if (m_ResourceLoader == null)
            {
                return;
            }

            m_ResourceLoader.UnloadAsset(asset);
            //Debug.LogFormat("ResourceManager: UnloadAsset: {0}", asset);
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="sceneAssetName">要加载场景资源的名称。</param>
        /// <param name="priority">加载场景资源的优先级。</param>
        /// <param name="loadSceneCallbacks">加载场景回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadScene(string sceneAssetName, int priority, LoadSceneCallbacks loadSceneCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (loadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Load scene callbacks is invalid.");
            }

            m_ResourceLoader.LoadScene(sceneAssetName, priority, loadSceneCallbacks, userData);
        }

        /// <summary>
        /// 异步卸载场景。
        /// </summary>
        /// <param name="sceneAssetName">要卸载场景资源的名称。</param>
        /// <param name="unloadSceneCallbacks">卸载场景回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void UnloadScene(string sceneAssetName, UnloadSceneCallbacks unloadSceneCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (unloadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Unload scene callbacks is invalid.");
            }

            m_ResourceLoader.UnloadScene(sceneAssetName, unloadSceneCallbacks, userData);
        }

        /// <summary>
        /// 异步加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <param name="loadBinaryCallbacks">加载二进制资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadBinary(string binaryAssetName, LoadBinaryCallbacks loadBinaryCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (loadBinaryCallbacks == null)
            {
                throw new GameFrameworkException("Load binary callbacks is invalid.");
            }

            m_ResourceLoader.LoadBinary(binaryAssetName, loadBinaryCallbacks, userData);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源的二进制流。</param>
        /// <param name="startIndex">存储加载二进制资源的二进制流的起始位置。</param>
        /// <param name="length">存储加载二进制资源的二进制流的长度。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinaryFromFileSystem(string binaryAssetName, byte[] buffer, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinaryFromFileSystem(binaryAssetName, buffer, startIndex, length);
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
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, offset, buffer, startIndex, length);
        }

        #endregion

        #region 核心方法2：”单机模式“和”热更新模式“下设置或获取部分重要参数的方法
        /// <summary>
        /// 提供给外部用于设置当前游戏的资源模式，并根据该模式为”用到的序列化器“赋值，同时为”模式中各个阶段“添加”监听事件“
        /// PS：在启动后则会设置，并且是根据”ResourceComponent“的”Inspector面板“中的数值来设置
        /// </summary>
        /// <param name="resourceMode">资源模式。</param>
        public void SetResourceMode(ResourceMode resourceMode)
        {
            if (resourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("Resource mode is invalid.");
            }

            if (m_RefuseSetFlag)
            {
                throw new GameFrameworkException("You can not set resource mode at this time.");
            }

            //”m_ResourceMode“参数初始值必然为”ResourceMode.Unspecified“，本方法提供给外部用来设置”当前的ResourceMode“
            //并且一旦设置后则不会再次改变
            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                //外部设置该”m_ResourceMode“时只有”Package/Updatable/UpdatableWhilePlaying“三种，并可以传递过来”Unspecified“，
                //因此需要”ResourceComponent“的”Inspector面板“中不要提供”Unspecified“选项
                m_ResourceMode = resourceMode;

                if (m_ResourceMode == ResourceMode.Package)
                {
                    //其他的”序列化器“由于本模式下不会用到，因此无需赋值
                    m_PackageVersionListSerializer = new PackageVersionListSerializer();

                    m_ResourceIniter = new ResourceIniter(this);
                    m_ResourceIniter.ResourceInitComplete += OnIniterResourceInitComplete;
                }
                else if (m_ResourceMode == ResourceMode.Updatable || m_ResourceMode == ResourceMode.UpdatableWhilePlaying)
                {
                    //热更新模式“UpdatableVesionList”对象的序列化器，保存在“读写区PersistentDataPath”，文件名为“GameFrameworkVersion.dat”
                    m_UpdatableVersionListSerializer = new UpdatableVersionListSerializer();
                    //单机模式“PackageVersionList”对象的序列化器，但保存在“只读区StreamingAssetPath”，文件名同样是“GameFrameworkVersion.dat”
                    m_ReadOnlyVersionListSerializer = new ReadOnlyVersionListSerializer();
                    //热更新模式“LocalVersionList”对象的序列化器，保存在“读写区PersistentDataPath”，文件名为“GameFrameworkList.dat”
                    //主要用于热更新中“比较本地已经下载完成的资源”和“服务器上一共需要下载的资源”，避免“中途下载失败导致重复下载全部资源”的情况
                    //“LocalVersionList”的作用在于：比对之后，就只用下载还没有成功下载的资源即可
                    m_ReadWriteVersionListSerializer = new ReadWriteVersionListSerializer();
                    m_ResourcePackVersionListSerializer = new ResourcePackVersionListSerializer();

                    m_VersionListProcessor = new VersionListProcessor(this);
                    m_VersionListProcessor.VersionListUpdateSuccess += OnVersionListProcessorUpdateSuccess;
                    m_VersionListProcessor.VersionListUpdateFailure += OnVersionListProcessorUpdateFailure;

                    m_ResourceChecker = new ResourceChecker(this);
                    m_ResourceChecker.ResourceNeedUpdate += OnCheckerResourceNeedUpdate;
                    m_ResourceChecker.ResourceCheckComplete += OnCheckerResourceCheckComplete;

                    m_ResourceUpdater = new ResourceUpdater(this);
                    m_ResourceUpdater.ResourceApplyStart += OnUpdaterResourceApplyStart;
                    m_ResourceUpdater.ResourceApplySuccess += OnUpdaterResourceApplySuccess;
                    m_ResourceUpdater.ResourceApplyFailure += OnUpdaterResourceApplyFailure;
                    m_ResourceUpdater.ResourceApplyComplete += OnUpdaterResourceApplyComplete;
                    m_ResourceUpdater.ResourceUpdateStart += OnUpdaterResourceUpdateStart;
                    m_ResourceUpdater.ResourceUpdateChanged += OnUpdaterResourceUpdateChanged;
                    m_ResourceUpdater.ResourceUpdateSuccess += OnUpdaterResourceUpdateSuccess;
                    m_ResourceUpdater.ResourceUpdateFailure += OnUpdaterResourceUpdateFailure;
                    m_ResourceUpdater.ResourceUpdateComplete += OnUpdaterResourceUpdateComplete;
                    m_ResourceUpdater.ResourceUpdateAllComplete += OnUpdaterResourceUpdateAllComplete;
                }
            }
            else if (m_ResourceMode != resourceMode)
            {
                throw new GameFrameworkException("You can not change resource mode at this time.");
            }
        }

        /// <summary>
        /// 资源管理器轮询。
        /// PS：“单机模式”或”热更新模式“下所有需要”Update轮询“的操作都可以通过本”入口“来主动调用
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            //针对该“xxMananger”中所有需要“轮询”的对象执行其“Update”方法
            if (m_ResourceVerifier != null)
            {
                m_ResourceVerifier.Update(elapseSeconds, realElapseSeconds);
                return;
            }

            if (m_ResourceUpdater != null)
            {
                m_ResourceUpdater.Update(elapseSeconds, realElapseSeconds);
            }

            //“加载器”中所有“Asset”的加载都是通过“任务池”执行，即先将目标加载对象放入“任务池”，然后执行这里的“Update轮询”来遍历
            //“资源任务池”中的所有任务
            m_ResourceLoader.Update(elapseSeconds, realElapseSeconds);
        }

        //当游戏机制支持”边玩边下载“时，专门用来”从资源服务器“下载”指定AB文件“的方法
        private void UpdateResource(ResourceName resourceName)
        {
            m_ResourceUpdater.UpdateResource(resourceName);
        }

        //注意：如果”m_ResourceGroups“中没有”指定resourceGroupName“的分组，则自动创建新的”ResourceGroup变量“，并放入”m_ResourceGroups“集合中
        private ResourceGroup GetOrAddResourceGroup(string resourceGroupName)
        {
            if (resourceGroupName == null)
            {
                resourceGroupName = string.Empty;
            }

            ResourceGroup resourceGroup = null;
            if (!m_ResourceGroups.TryGetValue(resourceGroupName, out resourceGroup))
            {
                //如果没有该resourceGroup，这里居然把装有所有ResourceInfo的集合传递过去
                //“resourceGroup”内部的“resourceNames”才是真正代表该group内部包含的resource列表的
                resourceGroup = new ResourceGroup(resourceGroupName, m_ResourceInfos);
                m_ResourceGroups.Add(resourceGroupName, resourceGroup);
            }

            return resourceGroup;
        }

        //获取”指定fileSystemName“的fileSystem对象，如果没有则”使用FileSystem模块的方法创建新的fileSystem对象“，并放入相应的集合中
        //PS：这个方法命名垃圾，应该改名
        private IFileSystem GetFileSystem(string fileSystemName, bool storageInReadOnly)
        {
            if (string.IsNullOrEmpty(fileSystemName))
            {
                throw new GameFrameworkException("File system name is invalid.");
            }

            IFileSystem fileSystem = null;
            if (storageInReadOnly)
            {
                if (!m_ReadOnlyFileSystems.TryGetValue(fileSystemName, out fileSystem))
                {
                    string fullPath = Utility.Path.GetRegularPath(Path.Combine(m_ReadOnlyPath, Utility.Text.Format("{0}.{1}", fileSystemName, DefaultExtension)));
                    //检测”FileSystem模块“中是否有该”指定fileSystemName“的fileSystem对象，如果没有则创建新的fileSystem对象，
                    //否则应该将其加入”m_ReadOnlyFileSystems“集合中(这里缺少了”本情况下加入m_ReadOnlyFileSystems集合中的语句“)
                    fileSystem = m_FileSystemManager.GetFileSystem(fullPath);
                    if (fileSystem == null)
                    {
                        //在"FileSystem模块"中添加”指定fileSystemName“的”fileSystem对象“，并添加到”m_ReadOnlyFileSystems“集合中
                        fileSystem = m_FileSystemManager.LoadFileSystem(fullPath, FileSystemAccess.Read);
                        m_ReadOnlyFileSystems.Add(fileSystemName, fileSystem);
                    }
                }
            }
            else
            {
                if (!m_ReadWriteFileSystems.TryGetValue(fileSystemName, out fileSystem))
                {
                    string fullPath = Utility.Path.GetRegularPath(Path.Combine(m_ReadWritePath, Utility.Text.Format("{0}.{1}", fileSystemName, DefaultExtension)));
                    fileSystem = m_FileSystemManager.GetFileSystem(fullPath);
                    if (fileSystem == null)
                    {
                        //"读写区"具有”写入权限“
                        if (File.Exists(fullPath))
                        {
                            fileSystem = m_FileSystemManager.LoadFileSystem(fullPath, FileSystemAccess.ReadWrite);
                        }
                        else
                        {
                            string directory = Path.GetDirectoryName(fullPath);
                            if (!Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            fileSystem = m_FileSystemManager.CreateFileSystem(fullPath, FileSystemAccess.ReadWrite, FileSystemMaxFileCount, FileSystemMaxBlockCount);
                        }

                        m_ReadWriteFileSystems.Add(fileSystemName, fileSystem);
                    }
                }
            }

            return fileSystem;
        }

        #endregion

        #region 核心方法3：提供给外部用于启动”更新资源“或”加载资源“中关键步骤的控制器
        /// <summary>
        /// 使用单机模式并初始化资源。
        /// </summary>
        /// <param name="initResourcesCompleteCallback">使用单机模式并初始化资源完成时的回调函数。</param>
        public void InitResources(InitResourcesCompleteCallback initResourcesCompleteCallback)
        {
            if (initResourcesCompleteCallback == null)
            {
                throw new GameFrameworkException("Init resources complete callback is invalid.");
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Package)
            {
                throw new GameFrameworkException("You can not use InitResources without package resource mode.");
            }

            if (m_ResourceIniter == null)
            {
                throw new GameFrameworkException("You can not use InitResources at this time.");
            }

            //1.只有”单机模式“下才可以调用本方法
            //2.正式进入”单机模式“的流程中，之后无法再改变”公用参数“的数值，如”resourceMode, readOnlyPath, readWritePath“等
            m_RefuseSetFlag = true;
            m_InitResourcesCompleteCallback = initResourcesCompleteCallback;
            m_ResourceIniter.InitResources(m_CurrentVariant);
        }

        /// <summary>
        /// 使用可更新模式并检查版本资源列表。
        /// </summary>
        /// <param name="latestInternalResourceVersion">最新的内部资源版本号。</param>
        /// <returns>检查版本资源列表结果。</returns>
        public CheckVersionListResult CheckVersionList(int latestInternalResourceVersion)
        {
            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use CheckVersionList without updatable resource mode.");
            }

            if (m_VersionListProcessor == null)
            {
                throw new GameFrameworkException("You can not use CheckVersionList at this time.");
            }

            return m_VersionListProcessor.CheckVersionList(latestInternalResourceVersion);
        }

        /// <summary>
        /// 使用可更新模式并更新版本资源列表。
        /// </summary>
        /// <param name="versionListLength">版本资源列表大小。</param>
        /// <param name="versionListHashCode">版本资源列表哈希值。</param>
        /// <param name="versionListCompressedLength">版本资源列表压缩后大小。</param>
        /// <param name="versionListCompressedHashCode">版本资源列表压缩后哈希值。</param>
        /// <param name="updateVersionListCallbacks">版本资源列表更新回调函数集。</param>
        public void UpdateVersionList(int versionListLength, int versionListHashCode, int versionListCompressedLength, int versionListCompressedHashCode, UpdateVersionListCallbacks updateVersionListCallbacks)
        {
            if (updateVersionListCallbacks == null)
            {
                throw new GameFrameworkException("Update version list callbacks is invalid.");
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use UpdateVersionList without updatable resource mode.");
            }

            if (m_VersionListProcessor == null)
            {
                throw new GameFrameworkException("You can not use UpdateVersionList at this time.");
            }

            m_UpdateVersionListCallbacks = updateVersionListCallbacks;
            m_VersionListProcessor.UpdateVersionList(versionListLength, versionListHashCode, versionListCompressedLength, versionListCompressedHashCode);
        }

        /// <summary>
        /// 使用可更新模式并校验资源。
        /// </summary>
        /// <param name="verifyResourceLengthPerFrame">每帧至少校验资源的大小，以字节为单位。</param>
        /// <param name="verifyResourcesCompleteCallback">使用可更新模式并校验资源完成时的回调函数。</param>
        public void VerifyResources(int verifyResourceLengthPerFrame, VerifyResourcesCompleteCallback verifyResourcesCompleteCallback)
        {
            if (verifyResourcesCompleteCallback == null)
            {
                throw new GameFrameworkException("Verify resources complete callback is invalid.");
            }

            //默认从“Inspector”面板中只有“Package/Updatable/Updatable while playing”三种“ResourceMode”
            //从理论上讲，是没有出现“ResourceMode.Unspecified”的可能，
            //并且“ResourceComponent”脚本内部默认设置“m_ResourceMode”参数值为“ResourceMode.Package”
            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            //如果当前不是“热更新资源模式”，则报异常(从当前框架设计来看，只有“热更”模式才需要“VerifyResource”)，所以这里的逻辑没问题
            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use VerifyResources without updatable resource mode.");
            }

            if (m_RefuseSetFlag)
            {
                throw new GameFrameworkException("You can not verify resources at this time.");
            }

            m_ResourceVerifier = new ResourceVerifier(this);
            m_ResourceVerifier.ResourceVerifyStart += OnVerifierResourceVerifyStart;
            m_ResourceVerifier.ResourceVerifySuccess += OnVerifierResourceVerifySuccess;
            m_ResourceVerifier.ResourceVerifyFailure += OnVerifierResourceVerifyFailure;
            m_ResourceVerifier.ResourceVerifyComplete += OnVerifierResourceVerifyComplete;
            //把外部传送过来的回调存储起来，当“ResourceVerifier”执行完毕后会执行自身的“m_ResourceVerifier.ResourceVerifyComplete”
            //因此带动本脚本内部的“OnVerifierResourceVerifyComplete”
            //而该“OnVerifierResourceVerifyComplete”又会在内部调用“这里存储下来的外部回调verifyResourcesCompleteCallback”
            //如此的循环嵌套执行
            m_VerifyResourcesCompleteCallback = verifyResourcesCompleteCallback;
            m_ResourceVerifier.VerifyResources(verifyResourceLengthPerFrame);
        }

        /// <summary>
        /// 使用可更新模式并检查资源。
        /// </summary>
        /// <param name="ignoreOtherVariant">是否忽略处理其它变体的资源，若不忽略，将会移除其它变体的资源。</param>
        /// <param name="checkResourcesCompleteCallback">使用可更新模式并检查资源完成时的回调函数。</param>
        public void CheckResources(bool ignoreOtherVariant, CheckResourcesCompleteCallback checkResourcesCompleteCallback)
        {
            if (checkResourcesCompleteCallback == null)
            {
                throw new GameFrameworkException("Check resources complete callback is invalid.");
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use CheckResources without updatable resource mode.");
            }

            if (m_ResourceChecker == null)
            {
                throw new GameFrameworkException("You can not use CheckResources at this time.");
            }

            //TODO：在此时才设置这个参数太晚了吧。应该在“VersionListProcesser”中更新“版本列表列表”时就设置，因为这已经代表其进入“热更新模式”了。这个要改
            m_RefuseSetFlag = true;
            m_CheckResourcesCompleteCallback = checkResourcesCompleteCallback;
            m_ResourceChecker.CheckResources(m_CurrentVariant, ignoreOtherVariant);
        }

        /// <summary>
        /// 使用可更新模式并应用资源包资源(从执行逻辑看，”资源包“只适用于”热更新模式“)
        /// </summary>
        /// <param name="resourcePackPath">要应用的资源包路径。</param>
        /// <param name="applyResourcesCompleteCallback">使用可更新模式并应用资源包资源完成时的回调函数。</param>
        public void ApplyResources(string resourcePackPath, ApplyResourcesCompleteCallback applyResourcesCompleteCallback)
        {
            if (string.IsNullOrEmpty(resourcePackPath))
            {
                throw new GameFrameworkException("Resource pack path is invalid.");
            }

            if (!File.Exists(resourcePackPath))
            {
                throw new GameFrameworkException(Utility.Text.Format("Resource pack '{0}' is not exist.", resourcePackPath));
            }

            if (applyResourcesCompleteCallback == null)
            {
                throw new GameFrameworkException("Apply resources complete callback is invalid.");
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use ApplyResources without updatable resource mode.");
            }

            if (m_ResourceUpdater == null)
            {
                throw new GameFrameworkException("You can not use ApplyResources at this time.");
            }

            m_ApplyResourcesCompleteCallback = applyResourcesCompleteCallback;
            m_ResourceUpdater.ApplyResources(resourcePackPath);
        }

        /// <summary>
        /// “热更新模式”中使用“string.Empty”分组与“ResourceUpdater.m_UpdateCandidateInfo”集合比对，以更新所有需要Updated的资源
        /// </summary>
        /// <param name="updateResourcesCompleteCallback">使用可更新模式并更新默认资源组完成时的回调函数。</param>
        public void UpdateResources(UpdateResourcesCompleteCallback updateResourcesCompleteCallback)
        {
            UpdateResources(string.Empty, updateResourcesCompleteCallback);
        }

        /// <summary>
        /// 使用可更新模式并更新指定资源组的资源。
        /// </summary>
        /// <param name="resourceGroupName">要更新的资源组名称。</param>
        /// <param name="updateResourcesCompleteCallback">使用可更新模式并更新指定资源组完成时的回调函数。</param>
        public void UpdateResources(string resourceGroupName, UpdateResourcesCompleteCallback updateResourcesCompleteCallback)
        {
            if (updateResourcesCompleteCallback == null)
            {
                throw new GameFrameworkException("Update resources complete callback is invalid.");
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use UpdateResources without updatable resource mode.");
            }

            if (m_ResourceUpdater == null)
            {
                throw new GameFrameworkException("You can not use UpdateResources at this time.");
            }

            ResourceGroup resourceGroup = (ResourceGroup)GetResourceGroup(resourceGroupName);
            if (resourceGroup == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Can not find resource group '{0}'.", resourceGroupName));
            }

            m_UpdateResourcesCompleteCallback = updateResourcesCompleteCallback;
            m_ResourceUpdater.UpdateResources(resourceGroup);
        }
        #endregion

        #region 重要方法：各模式下其他一些用到的重要方法
        /// <summary>
        /// 初始化资源管理器的新实例。
        /// </summary>
        public ResourceManager()
        {
            //如果变量的赋值都是“null”，那么这里是否还有必要将其写出来呢？不写也没影响吧！！
            m_AssetInfos = null;
            m_ResourceInfos = null;
            m_ReadWriteResourceInfos = null;
            m_ReadOnlyFileSystems = new Dictionary<string, IFileSystem>(StringComparer.Ordinal);
            m_ReadWriteFileSystems = new Dictionary<string, IFileSystem>(StringComparer.Ordinal);
            m_ResourceGroups = new Dictionary<string, ResourceGroup>(StringComparer.Ordinal);

            m_PackageVersionListSerializer = null;
            m_UpdatableVersionListSerializer = null;
            m_ReadOnlyVersionListSerializer = null;
            m_ReadWriteVersionListSerializer = null;
            m_ResourcePackVersionListSerializer = null;

            m_ResourceIniter = null;
            m_VersionListProcessor = null;
            m_ResourceVerifier = null;
            m_ResourceChecker = null;
            m_ResourceUpdater = null;
            m_ResourceLoader = new ResourceLoader(this);

            m_ResourceHelper = null;
            m_ReadOnlyPath = null;
            m_ReadWritePath = null;
            m_ResourceMode = ResourceMode.Unspecified;
            m_RefuseSetFlag = false;
            m_CurrentVariant = null;
            m_UpdatePrefixUri = null;
            m_ApplicableGameVersion = null;
            m_InternalResourceVersion = 0;
            m_CachedStream = null;
            m_DecryptResourceCallback = null;
            m_InitResourcesCompleteCallback = null;
            m_UpdateVersionListCallbacks = null;
            m_VerifyResourcesCompleteCallback = null;
            m_CheckResourcesCompleteCallback = null;
            m_ApplyResourcesCompleteCallback = null;
            m_UpdateResourcesCompleteCallback = null;
            m_ResourceVerifySuccessEventHandler = null;
            m_ResourceVerifyFailureEventHandler = null;
            m_ResourceApplySuccessEventHandler = null;
            m_ResourceApplyFailureEventHandler = null;
            m_ResourceUpdateStartEventHandler = null;
            m_ResourceUpdateChangedEventHandler = null;
            m_ResourceUpdateSuccessEventHandler = null;
            m_ResourceUpdateFailureEventHandler = null;
            m_ResourceUpdateAllCompleteEventHandler = null;
        }

        /// <summary>
        /// 关闭并清理资源管理器。
        /// 注意：这些参数不只是将其内部清空，之后还会专门将其赋值为null
        /// </summary>
        internal override void Shutdown()
        {
            if (m_ResourceIniter != null)
            {
                m_ResourceIniter.Shutdown();
                m_ResourceIniter = null;
            }

            if (m_VersionListProcessor != null)
            {
                m_VersionListProcessor.VersionListUpdateSuccess -= OnVersionListProcessorUpdateSuccess;
                m_VersionListProcessor.VersionListUpdateFailure -= OnVersionListProcessorUpdateFailure;
                m_VersionListProcessor.Shutdown();
                m_VersionListProcessor = null;
            }

            if (m_ResourceVerifier != null)
            {
                m_ResourceVerifier.ResourceVerifyStart -= OnVerifierResourceVerifyStart;
                m_ResourceVerifier.ResourceVerifySuccess -= OnVerifierResourceVerifySuccess;
                m_ResourceVerifier.ResourceVerifyFailure -= OnVerifierResourceVerifyFailure;
                m_ResourceVerifier.ResourceVerifyComplete -= OnVerifierResourceVerifyComplete;
                m_ResourceVerifier.Shutdown();
                m_ResourceVerifier = null;
            }

            if (m_ResourceChecker != null)
            {
                m_ResourceChecker.ResourceNeedUpdate -= OnCheckerResourceNeedUpdate;
                m_ResourceChecker.ResourceCheckComplete -= OnCheckerResourceCheckComplete;
                m_ResourceChecker.Shutdown();
                m_ResourceChecker = null;
            }

            if (m_ResourceUpdater != null)
            {
                m_ResourceUpdater.ResourceApplyStart -= OnUpdaterResourceApplyStart;
                m_ResourceUpdater.ResourceApplySuccess -= OnUpdaterResourceApplySuccess;
                m_ResourceUpdater.ResourceApplyFailure -= OnUpdaterResourceApplyFailure;
                m_ResourceUpdater.ResourceApplyComplete -= OnUpdaterResourceApplyComplete;
                m_ResourceUpdater.ResourceUpdateStart -= OnUpdaterResourceUpdateStart;
                m_ResourceUpdater.ResourceUpdateChanged -= OnUpdaterResourceUpdateChanged;
                m_ResourceUpdater.ResourceUpdateSuccess -= OnUpdaterResourceUpdateSuccess;
                m_ResourceUpdater.ResourceUpdateFailure -= OnUpdaterResourceUpdateFailure;
                m_ResourceUpdater.ResourceUpdateComplete -= OnUpdaterResourceUpdateComplete;
                m_ResourceUpdater.ResourceUpdateAllComplete -= OnUpdaterResourceUpdateAllComplete;
                m_ResourceUpdater.Shutdown();
                m_ResourceUpdater = null;

                if (m_ReadWriteResourceInfos != null)
                {
                    m_ReadWriteResourceInfos.Clear();
                    m_ReadWriteResourceInfos = null;
                }

                FreeCachedStream();
            }

            if (m_ResourceLoader != null)
            {
                m_ResourceLoader.Shutdown();
                m_ResourceLoader = null;
            }

            if (m_AssetInfos != null)
            {
                m_AssetInfos.Clear();
                m_AssetInfos = null;
            }

            if (m_ResourceInfos != null)
            {
                m_ResourceInfos.Clear();
                m_ResourceInfos = null;
            }

            //这些变量”readOnly“，无法为其重新赋值，但可以改变其内部的数据，因此使用”Clear“清空该集合内部元素是可以的，但无法再为其赋值为null
            m_ReadOnlyFileSystems.Clear();
            m_ReadWriteFileSystems.Clear();
            m_ResourceGroups.Clear();
        }

        /// <summary>
        /// 设置对象池管理器。
        /// </summary>
        /// <param name="objectPoolManager">对象池管理器。</param>
        public void SetObjectPoolManager(IObjectPoolManager objectPoolManager)
        {
            if (objectPoolManager == null)
            {
                throw new GameFrameworkException("Object pool manager is invalid.");
            }

            m_ResourceLoader.SetObjectPoolManager(objectPoolManager);
        }

        /// <summary>
        /// 设置文件系统管理器。
        /// </summary>
        /// <param name="fileSystemManager">文件系统管理器。</param>
        public void SetFileSystemManager(IFileSystemManager fileSystemManager)
        {
            if (fileSystemManager == null)
            {
                throw new GameFrameworkException("File system manager is invalid.");
            }

            m_FileSystemManager = fileSystemManager;
        }

        /// <summary>
        /// 设置下载管理器。
        /// </summary>
        /// <param name="downloadManager">下载管理器。</param>
        public void SetDownloadManager(IDownloadManager downloadManager)
        {
            if (downloadManager == null)
            {
                throw new GameFrameworkException("Download manager is invalid.");
            }

            if (m_VersionListProcessor != null)
            {
                //下载”远程服务器版本文件“时需要使用(UnityWebRequest.Get只能读取到远程服务器上的文件的数据，不能直接将其下载到本地，所以需要用到”Download模块“)
                m_VersionListProcessor.SetDownloadManager(downloadManager);
            }

            if (m_ResourceUpdater != null)
            {
                m_ResourceUpdater.SetDownloadManager(downloadManager);
            }
        }

        /// <summary>
        /// 通过外部增加”TaskAgentHelper“来为”任务池系统“增加”LoadResourceAgent“。
        /// PS：这傻逼的命名也是无语
        /// </summary>
        /// <param name="loadResourceAgentHelper">要增加的加载资源代理辅助器。</param>
        public void AddLoadResourceAgentHelper(ILoadResourceAgentHelper loadResourceAgentHelper)
        {
            if (m_ResourceHelper == null)
            {
                throw new GameFrameworkException("Resource helper is invalid.");
            }

            if (string.IsNullOrEmpty(m_ReadOnlyPath))
            {
                throw new GameFrameworkException("Read-only path is invalid.");
            }

            if (string.IsNullOrEmpty(m_ReadWritePath))
            {
                throw new GameFrameworkException("Read-write path is invalid.");
            }

            m_ResourceLoader.AddLoadResourceAgentHelper(loadResourceAgentHelper, m_ResourceHelper, m_ReadOnlyPath, m_ReadWritePath, m_DecryptResourceCallback);
        }

        /// <summary>
        /// 在“热更新流程“中”ResourceUpdater.cs”内校验资源包时使用。
        /// </summary>
        /// <param name="resourcePackPath">要校验的资源包路径。</param>
        /// <returns>是否校验资源包成功。</returns>
        public bool VerifyResourcePack(string resourcePackPath)
        {
            if (string.IsNullOrEmpty(resourcePackPath))
            {
                throw new GameFrameworkException("Resource pack path is invalid.");
            }

            if (!File.Exists(resourcePackPath))
            {
                throw new GameFrameworkException(Utility.Text.Format("Resource pack '{0}' is not exist.", resourcePackPath));
            }

            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use VerifyResourcePack without updatable resource mode.");
            }

            if (m_ResourcePackVersionListSerializer == null)
            {
                throw new GameFrameworkException("You can not use VerifyResourcePack at this time.");
            }

            try
            {
                long length = 0L;
                ResourcePackVersionList versionList = default(ResourcePackVersionList);
                using (FileStream fileStream = new FileStream(resourcePackPath, FileMode.Open, FileAccess.Read))
                {
                    length = fileStream.Length;
                    versionList = m_ResourcePackVersionListSerializer.Deserialize(fileStream);
                }

                if (!versionList.IsValid)
                {
                    return false;
                }

                if (versionList.Offset + versionList.Length != length)
                {
                    return false;
                }

                int hashCode = 0;
                using (FileStream fileStream = new FileStream(resourcePackPath, FileMode.Open, FileAccess.Read))
                {
                    //“versionList.Offset”代表“资源包中AB数据真实开始存放的位置”
                    //“从该位置开始到fileStream末尾”，这区间所有数据的“哈希码”，而非“该fileStream全体数据”的哈希码
                    fileStream.Position = versionList.Offset;
                    hashCode = Utility.Verifier.GetCrc32(fileStream);
                }

                if (versionList.HashCode != hashCode)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        /// <param name="assetName">要检查资源的名称。</param>
        /// <returns>检查资源是否存在的结果。</returns>
        public HasAssetResult HasAsset(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            return m_ResourceLoader.HasAsset(assetName);
        }

        private void PrepareCachedStream()
        {
            if (m_CachedStream == null)
            {
                m_CachedStream = new MemoryStream();
            }

            m_CachedStream.Position = 0L;
            m_CachedStream.SetLength(0L);
        }

        private void FreeCachedStream()
        {
            if (m_CachedStream != null)
            {
                m_CachedStream.Dispose();
                m_CachedStream = null;
            }
        }

        #endregion

        #region 所有监听事件的回调
        private void OnIniterResourceInitComplete()
        {
            m_ResourceIniter.ResourceInitComplete -= OnIniterResourceInitComplete;
            m_ResourceIniter.Shutdown();
            //注意：这里还专门将该变量设置为”null“，如此方便”GC回收“
            m_ResourceIniter = null;

            m_InitResourcesCompleteCallback();
            m_InitResourcesCompleteCallback = null;
        }

        private void OnVersionListProcessorUpdateSuccess(string downloadPath, string downloadUri)
        {
            m_UpdateVersionListCallbacks.UpdateVersionListSuccessCallback(downloadPath, downloadUri);
        }

        private void OnVersionListProcessorUpdateFailure(string downloadUri, string errorMessage)
        {
            if (m_UpdateVersionListCallbacks.UpdateVersionListFailureCallback != null)
            {
                m_UpdateVersionListCallbacks.UpdateVersionListFailureCallback(downloadUri, errorMessage);
            }
        }

        //count和totalLength分别代表通过本地读写区的”GameFrameworkList.dat“文件读取到的”已经下载无误”的Resource的数量和总大小
        private void OnVerifierResourceVerifyStart(int count, long totalLength)
        {
            if (m_ResourceVerifyStartEventHandler != null)
            {
                ResourceVerifyStartEventArgs resourceVerifyStartEventArgs = ResourceVerifyStartEventArgs.Create(count, totalLength);
                m_ResourceVerifyStartEventHandler(this, resourceVerifyStartEventArgs);
                ReferencePool.Release(resourceVerifyStartEventArgs);
            }
        }

        private void OnVerifierResourceVerifySuccess(ResourceName resourceName, int length)
        {
            if (m_ResourceVerifySuccessEventHandler != null)
            {
                ResourceVerifySuccessEventArgs resourceVerifySuccessEventArgs = ResourceVerifySuccessEventArgs.Create(resourceName.FullName, length);
                m_ResourceVerifySuccessEventHandler(this, resourceVerifySuccessEventArgs);
                ReferencePool.Release(resourceVerifySuccessEventArgs);
            }
        }

        private void OnVerifierResourceVerifyFailure(ResourceName resourceName)
        {
            if (m_ResourceVerifyFailureEventHandler != null)
            {
                ResourceVerifyFailureEventArgs resourceVerifyFailureEventArgs = ResourceVerifyFailureEventArgs.Create(resourceName.FullName);
                m_ResourceVerifyFailureEventHandler(this, resourceVerifyFailureEventArgs);
                ReferencePool.Release(resourceVerifyFailureEventArgs);
            }
        }

        private void OnVerifierResourceVerifyComplete(bool result)
        {
            m_VerifyResourcesCompleteCallback(result);
            m_ResourceVerifier.ResourceVerifyStart -= OnVerifierResourceVerifyStart;
            m_ResourceVerifier.ResourceVerifySuccess -= OnVerifierResourceVerifySuccess;
            m_ResourceVerifier.ResourceVerifyFailure -= OnVerifierResourceVerifyFailure;
            m_ResourceVerifier.ResourceVerifyComplete -= OnVerifierResourceVerifyComplete;
            m_ResourceVerifier.Shutdown();
            m_ResourceVerifier = null;
        }

        //这个方法命名也是真垃圾：明显就是在”ResourceChecker“的过程中发现”需要Update的AB“，然后将其通过”ResourceUpdater“放入”m_UpdateCandidateInfo“集合中
        //这名字是个啥啊
        private void OnCheckerResourceNeedUpdate(ResourceName resourceName, string fileSystemName,
            LoadType loadType, int length, int hashCode, int compressedLength, int compressedHashCode)
        {
            m_ResourceUpdater.AddResourceUpdate(resourceName, fileSystemName, loadType,
                length, hashCode, compressedLength, compressedHashCode,
                Utility.Path.GetRegularPath(Path.Combine(m_ReadWritePath, resourceName.FullName)));
        }

        private void OnCheckerResourceCheckComplete(int movedCount, int removedCount, int updateCount,
            long updateTotalLength, long updateTotalCompressedLength)
        {
            //“远程版本列表”处理器使用完毕，这里将其关闭
            m_VersionListProcessor.VersionListUpdateSuccess -= OnVersionListProcessorUpdateSuccess;
            m_VersionListProcessor.VersionListUpdateFailure -= OnVersionListProcessorUpdateFailure;
            m_VersionListProcessor.Shutdown();
            m_VersionListProcessor = null;
            m_UpdateVersionListCallbacks = null;

            //注销相应的委托监听并关闭“resourceChecker”
            m_ResourceChecker.ResourceNeedUpdate -= OnCheckerResourceNeedUpdate;
            //注意：这里注销的委托方法是“本身”
            m_ResourceChecker.ResourceCheckComplete -= OnCheckerResourceCheckComplete;
            m_ResourceChecker.Shutdown();
            m_ResourceChecker = null;

            //经过“ResourceChecker”的处理后，“本地版本文件”可能被修改，因此需要对其重新生成
            //但是只生成“读写区”的本地版本文件，因为“只读区”没有”写入权限“
            m_ResourceUpdater.CheckResourceComplete(movedCount > 0 || removedCount > 0);

            //说明这种情况下无需更新，此时注销“ResourceUpdater”的所有监听，并shutdown即可
            if (updateCount <= 0)
            {
                m_ResourceUpdater.ResourceApplyStart -= OnUpdaterResourceApplyStart;
                m_ResourceUpdater.ResourceApplySuccess -= OnUpdaterResourceApplySuccess;
                m_ResourceUpdater.ResourceApplyFailure -= OnUpdaterResourceApplyFailure;
                m_ResourceUpdater.ResourceApplyComplete -= OnUpdaterResourceApplyComplete;
                m_ResourceUpdater.ResourceUpdateStart -= OnUpdaterResourceUpdateStart;
                m_ResourceUpdater.ResourceUpdateChanged -= OnUpdaterResourceUpdateChanged;
                m_ResourceUpdater.ResourceUpdateSuccess -= OnUpdaterResourceUpdateSuccess;
                m_ResourceUpdater.ResourceUpdateFailure -= OnUpdaterResourceUpdateFailure;
                m_ResourceUpdater.ResourceUpdateComplete -= OnUpdaterResourceUpdateComplete;
                m_ResourceUpdater.ResourceUpdateAllComplete -= OnUpdaterResourceUpdateAllComplete;
                m_ResourceUpdater.Shutdown();
                m_ResourceUpdater = null;

                //清空集合中的元素并没有对该集合本身做处理，所以这里还需要将其“置为null”才行
                m_ReadWriteResourceInfos.Clear();
                m_ReadWriteResourceInfos = null;

                //“m_CachedStream”是专用于“读取数据时的中间载体”。由于“更新过程”已经结束，因此这里将其释放
                FreeCachedStream();
            }

            //这里的“m_CheckResourcesCompleteCallback”才是外部调用时传递过来的回调，也即“ProcedureCheckResource”中传递过来的回调
            m_CheckResourcesCompleteCallback(movedCount, removedCount, updateCount,
                updateTotalLength, updateTotalCompressedLength);
            m_CheckResourcesCompleteCallback = null;
        }

        private void OnUpdaterResourceApplyStart(string resourcePackPath, int count, long totalLength)
        {
            if (m_ResourceApplyStartEventHandler != null)
            {
                ResourceApplyStartEventArgs resourceApplyStartEventArgs = ResourceApplyStartEventArgs.Create(resourcePackPath, count, totalLength);
                m_ResourceApplyStartEventHandler(this, resourceApplyStartEventArgs);
                ReferencePool.Release(resourceApplyStartEventArgs);
            }
        }

        private void OnUpdaterResourceApplySuccess(ResourceName resourceName, string applyPath, string resourcePackPath, int length, int compressedLength)
        {
            if (m_ResourceApplySuccessEventHandler != null)
            {
                ResourceApplySuccessEventArgs resourceApplySuccessEventArgs = ResourceApplySuccessEventArgs.Create(resourceName.FullName, applyPath, resourcePackPath, length, compressedLength);
                m_ResourceApplySuccessEventHandler(this, resourceApplySuccessEventArgs);
                ReferencePool.Release(resourceApplySuccessEventArgs);
            }
        }

        private void OnUpdaterResourceApplyFailure(ResourceName resourceName, string resourcePackPath, string errorMessage)
        {
            if (m_ResourceApplyFailureEventHandler != null)
            {
                ResourceApplyFailureEventArgs resourceApplyFailureEventArgs = ResourceApplyFailureEventArgs.Create(resourceName.FullName, resourcePackPath, errorMessage);
                m_ResourceApplyFailureEventHandler(this, resourceApplyFailureEventArgs);
                ReferencePool.Release(resourceApplyFailureEventArgs);
            }
        }

        private void OnUpdaterResourceApplyComplete(string resourcePackPath, bool result)
        {
            ApplyResourcesCompleteCallback applyResourcesCompleteCallback = m_ApplyResourcesCompleteCallback;
            m_ApplyResourcesCompleteCallback = null;
            applyResourcesCompleteCallback(resourcePackPath, result);
        }

        private void OnUpdaterResourceUpdateStart(ResourceName resourceName, string downloadPath, string downloadUri, int currentLength, int compressedLength, int retryCount)
        {
            if (m_ResourceUpdateStartEventHandler != null)
            {
                ResourceUpdateStartEventArgs resourceUpdateStartEventArgs = ResourceUpdateStartEventArgs.Create(resourceName.FullName, downloadPath, downloadUri, currentLength, compressedLength, retryCount);
                m_ResourceUpdateStartEventHandler(this, resourceUpdateStartEventArgs);
                ReferencePool.Release(resourceUpdateStartEventArgs);
            }
        }

        private void OnUpdaterResourceUpdateChanged(ResourceName resourceName, string downloadPath, string downloadUri, int currentLength, int compressedLength)
        {
            if (m_ResourceUpdateChangedEventHandler != null)
            {
                ResourceUpdateChangedEventArgs resourceUpdateChangedEventArgs = ResourceUpdateChangedEventArgs.Create(resourceName.FullName, downloadPath, downloadUri, currentLength, compressedLength);
                m_ResourceUpdateChangedEventHandler(this, resourceUpdateChangedEventArgs);
                ReferencePool.Release(resourceUpdateChangedEventArgs);
            }
        }

        private void OnUpdaterResourceUpdateSuccess(ResourceName resourceName, string downloadPath, string downloadUri, int length, int compressedLength)
        {
            if (m_ResourceUpdateSuccessEventHandler != null)
            {
                ResourceUpdateSuccessEventArgs resourceUpdateSuccessEventArgs = ResourceUpdateSuccessEventArgs.Create(resourceName.FullName, downloadPath, downloadUri, length, compressedLength);
                m_ResourceUpdateSuccessEventHandler(this, resourceUpdateSuccessEventArgs);
                ReferencePool.Release(resourceUpdateSuccessEventArgs);
            }
        }

        private void OnUpdaterResourceUpdateFailure(ResourceName resourceName, string downloadUri, int retryCount, int totalRetryCount, string errorMessage)
        {
            if (m_ResourceUpdateFailureEventHandler != null)
            {
                ResourceUpdateFailureEventArgs resourceUpdateFailureEventArgs = ResourceUpdateFailureEventArgs.Create(resourceName.FullName, downloadUri, retryCount, totalRetryCount, errorMessage);
                m_ResourceUpdateFailureEventHandler(this, resourceUpdateFailureEventArgs);
                ReferencePool.Release(resourceUpdateFailureEventArgs);
            }
        }

        private void OnUpdaterResourceUpdateComplete(ResourceGroup resourceGroup, bool result)
        {
            Utility.Path.RemoveEmptyDirectory(m_ReadWritePath);
            UpdateResourcesCompleteCallback updateResourcesCompleteCallback = m_UpdateResourcesCompleteCallback;
            m_UpdateResourcesCompleteCallback = null;
            updateResourcesCompleteCallback(resourceGroup, result);
        }

        private void OnUpdaterResourceUpdateAllComplete()
        {
            m_ResourceUpdater.ResourceApplyStart -= OnUpdaterResourceApplyStart;
            m_ResourceUpdater.ResourceApplySuccess -= OnUpdaterResourceApplySuccess;
            m_ResourceUpdater.ResourceApplyFailure -= OnUpdaterResourceApplyFailure;
            m_ResourceUpdater.ResourceApplyComplete -= OnUpdaterResourceApplyComplete;
            m_ResourceUpdater.ResourceUpdateStart -= OnUpdaterResourceUpdateStart;
            m_ResourceUpdater.ResourceUpdateChanged -= OnUpdaterResourceUpdateChanged;
            m_ResourceUpdater.ResourceUpdateSuccess -= OnUpdaterResourceUpdateSuccess;
            m_ResourceUpdater.ResourceUpdateFailure -= OnUpdaterResourceUpdateFailure;
            m_ResourceUpdater.ResourceUpdateComplete -= OnUpdaterResourceUpdateComplete;
            m_ResourceUpdater.ResourceUpdateAllComplete -= OnUpdaterResourceUpdateAllComplete;
            m_ResourceUpdater.Shutdown();
            m_ResourceUpdater = null;

            m_ReadWriteResourceInfos.Clear();
            m_ReadWriteResourceInfos = null;

            FreeCachedStream();
            Utility.Path.RemoveEmptyDirectory(m_ReadWritePath);

            if (m_ResourceUpdateAllCompleteEventHandler != null)
            {
                ResourceUpdateAllCompleteEventArgs resourceUpdateAllCompleteEventArgs = ResourceUpdateAllCompleteEventArgs.Create();
                m_ResourceUpdateAllCompleteEventHandler(this, resourceUpdateAllCompleteEventArgs);
                ReferencePool.Release(resourceUpdateAllCompleteEventArgs);
            }
        }

        #endregion

        #region 工具方法，包括提供给外部的public方法，也包含”模式内部需要使用的private方法“
        /// <summary>
        /// 设置资源只读区路径。
        /// </summary>
        /// <param name="readOnlyPath">资源只读区路径。</param>
        public void SetReadOnlyPath(string readOnlyPath)
        {
            if (string.IsNullOrEmpty(readOnlyPath))
            {
                throw new GameFrameworkException("Read-only path is invalid.");
            }

            if (m_RefuseSetFlag)
            {
                throw new GameFrameworkException("You can not set read-only path at this time.");
            }

            if (m_ResourceLoader.TotalAgentCount > 0)
            {
                throw new GameFrameworkException("You must set read-only path before add load resource agent helper.");
            }

            m_ReadOnlyPath = readOnlyPath;
        }

        /// <summary>
        /// 设置资源读写区路径。
        /// </summary>
        /// <param name="readWritePath">资源读写区路径。</param>
        public void SetReadWritePath(string readWritePath)
        {
            if (string.IsNullOrEmpty(readWritePath))
            {
                throw new GameFrameworkException("Read-write path is invalid.");
            }

            if (m_RefuseSetFlag)
            {
                throw new GameFrameworkException("You can not set read-write path at this time.");
            }

            if (m_ResourceLoader.TotalAgentCount > 0)
            {
                throw new GameFrameworkException("You must set read-write path before add load resource agent helper.");
            }

            m_ReadWritePath = readWritePath;
        }

        /// <summary>
        /// 设置当前变体。
        /// </summary>
        /// <param name="currentVariant">当前变体。</param>
        public void SetCurrentVariant(string currentVariant)
        {
            if (m_RefuseSetFlag)
            {
                throw new GameFrameworkException("You can not set current variant at this time.");
            }

            m_CurrentVariant = currentVariant;
        }

        /// <summary>
        /// 设置解密资源回调函数。
        /// </summary>
        /// <param name="decryptResourceCallback">要设置的解密资源回调函数。</param>
        /// <remarks>如果不设置，将使用默认的解密资源回调函数。</remarks>
        public void SetDecryptResourceCallback(DecryptResourceCallback decryptResourceCallback)
        {
            if (m_ResourceLoader.TotalAgentCount > 0)
            {
                throw new GameFrameworkException("You must set decrypt resource callback before add load resource agent helper.");
            }

            m_DecryptResourceCallback = decryptResourceCallback;
        }

        /// <summary>
        /// 设置资源辅助器。
        /// </summary>
        /// <param name="resourceHelper">资源辅助器。</param>
        public void SetResourceHelper(IResourceHelper resourceHelper)
        {
            if (resourceHelper == null)
            {
                throw new GameFrameworkException("Resource helper is invalid.");
            }

            if (m_ResourceLoader.TotalAgentCount > 0)
            {
                throw new GameFrameworkException("You must set resource helper before add load resource agent helper.");
            }

            m_ResourceHelper = resourceHelper;
        }

        private AssetInfo GetAssetInfo(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (m_AssetInfos == null)
            {
                return null;
            }

            AssetInfo assetInfo = null;
            if (m_AssetInfos.TryGetValue(assetName, out assetInfo))
            {
                return assetInfo;
            }

            return null;
        }

        private ResourceInfo GetResourceInfo(ResourceName resourceName)
        {
            if (m_ResourceInfos == null)
            {
                return null;
            }

            ResourceInfo resourceInfo = null;
            if (m_ResourceInfos.TryGetValue(resourceName, out resourceInfo))
            {
                return resourceInfo;
            }

            return null;
        }

        /// <summary>
        /// 检查资源组是否存在。
        /// </summary>
        /// <param name="resourceGroupName">要检查资源组的名称。</param>
        /// <returns>资源组是否存在。</returns>
        public bool HasResourceGroup(string resourceGroupName)
        {
            return m_ResourceGroups.ContainsKey(resourceGroupName ?? string.Empty);
        }

        /// <summary>
        /// 获取资源组。
        /// </summary>
        /// <param name="resourceGroupName">要获取的资源组名称。</param>
        /// <returns>要获取的资源组。</returns>
        public IResourceGroup GetResourceGroup(string resourceGroupName)
        {
            ResourceGroup resourceGroup = null;
            if (m_ResourceGroups.TryGetValue(resourceGroupName ?? string.Empty, out resourceGroup))
            {
                return resourceGroup;
            }

            return null;
        }

        /// <summary>
        /// 获取所有资源组。
        /// </summary>
        /// <returns>所有资源组。</returns>
        public IResourceGroup[] GetAllResourceGroups()
        {
            int index = 0;
            IResourceGroup[] results = new IResourceGroup[m_ResourceGroups.Count];
            foreach (KeyValuePair<string, ResourceGroup> resourceGroup in m_ResourceGroups)
            {
                results[index++] = resourceGroup.Value;
            }

            return results;
        }

        /// <summary>
        /// 获取二进制资源的实际路径。
        /// </summary>
        /// <param name="binaryAssetName">要获取实际路径的二进制资源的名称。</param>
        /// <returns>二进制资源的实际路径。</returns>
        /// <remarks>此方法仅适用于二进制资源存储在磁盘（而非文件系统）中的情况。若二进制资源存储在文件系统中时，返回值将始终为空。</remarks>
        public string GetBinaryPath(string binaryAssetName)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            return m_ResourceLoader.GetBinaryPath(binaryAssetName);
        }

        /// <summary>
        /// 获取二进制资源的实际路径。
        /// </summary>
        /// <param name="binaryAssetName">要获取实际路径的二进制资源的名称。</param>
        /// <param name="storageInReadOnly">二进制资源是否存储在只读区中。</param>
        /// <param name="storageInFileSystem">二进制资源是否存储在文件系统中。</param>
        /// <param name="relativePath">二进制资源或存储二进制资源的文件系统，相对于只读区或者读写区的相对路径。</param>
        /// <param name="fileName">若二进制资源存储在文件系统中，则指示二进制资源在文件系统中的名称，否则此参数返回空。</param>
        /// <returns>是否获取二进制资源的实际路径成功。</returns>
        public bool GetBinaryPath(string binaryAssetName, out bool storageInReadOnly, out bool storageInFileSystem, out string relativePath, out string fileName)
        {
            return m_ResourceLoader.GetBinaryPath(binaryAssetName, out storageInReadOnly, out storageInFileSystem, out relativePath, out fileName);
        }

        /// <summary>
        /// 获取二进制资源的长度。
        /// </summary>
        /// <param name="binaryAssetName">要获取长度的二进制资源的名称。</param>
        /// <returns>二进制资源的长度。</returns>
        public int GetBinaryLength(string binaryAssetName)
        {
            return m_ResourceLoader.GetBinaryLength(binaryAssetName);
        }

        #endregion

        #region 提供给外部获取的相关属性
        /// <summary>
        /// 获取资源只读区路径。
        /// </summary>
        public string ReadOnlyPath
        {
            get
            {
                return m_ReadOnlyPath;
            }
        }

        /// <summary>
        /// 获取资源读写区路径。
        /// </summary>
        public string ReadWritePath
        {
            get
            {
                return m_ReadWritePath;
            }
        }

        /// <summary>
        /// 获取资源模式。
        /// </summary>
        public ResourceMode ResourceMode
        {
            get
            {
                return m_ResourceMode;
            }
        }

        /// <summary>
        /// 获取当前变体。
        /// </summary>
        public string CurrentVariant
        {
            get
            {
                return m_CurrentVariant;
            }
        }

        /// <summary>
        /// 获取单机模式版本资源列表序列化器。
        /// </summary>
        public PackageVersionListSerializer PackageVersionListSerializer
        {
            get
            {
                return m_PackageVersionListSerializer;
            }
        }

        /// <summary>
        /// 获取可更新模式版本资源列表序列化器。
        /// </summary>
        public UpdatableVersionListSerializer UpdatableVersionListSerializer
        {
            get
            {
                return m_UpdatableVersionListSerializer;
            }
        }

        /// <summary>
        /// 获取本地只读区版本资源列表序列化器。
        /// </summary>
        public ReadOnlyVersionListSerializer ReadOnlyVersionListSerializer
        {
            get
            {
                return m_ReadOnlyVersionListSerializer;
            }
        }

        /// <summary>
        /// 获取本地读写区版本资源列表序列化器。
        /// </summary>
        public ReadWriteVersionListSerializer ReadWriteVersionListSerializer
        {
            get
            {
                return m_ReadWriteVersionListSerializer;
            }
        }

        /// <summary>
        /// 获取资源包版本资源列表序列化器。
        /// </summary>
        public ResourcePackVersionListSerializer ResourcePackVersionListSerializer
        {
            get
            {
                return m_ResourcePackVersionListSerializer;
            }
        }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion
        {
            get
            {
                return m_ApplicableGameVersion;
            }
        }

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion
        {
            get
            {
                return m_InternalResourceVersion;
            }
        }

        /// <summary>
        /// 获取资源数量。
        /// PS：该模式下所有Assets的信息
        /// </summary>
        public int AssetCount
        {
            get
            {
                return m_AssetInfos != null ? m_AssetInfos.Count : 0;
            }
        }

        /// <summary>
        /// 获取资源数量。
        /// </summary>
        public int ResourceCount
        {
            get
            {
                return m_ResourceInfos != null ? m_ResourceInfos.Count : 0;
            }
        }

        /// <summary>
        /// 获取资源组数量。
        /// </summary>
        public int ResourceGroupCount
        {
            get
            {
                return m_ResourceGroups.Count;
            }
        }

        /// <summary>
        /// 获取或设置资源更新下载地址前缀。
        /// </summary>
        public string UpdatePrefixUri
        {
            get
            {
                return m_UpdatePrefixUri;
            }
            set
            {
                m_UpdatePrefixUri = value;
            }
        }

        /// <summary>
        /// 获取或设置每更新多少字节的资源，重新生成一次版本资源列表。
        /// PS：“重写本地版本文件”应该不只是“ResourceUpdater”中会用到吧，在“ResourceVerify”和“ResourceChecker”中也会用到
        /// </summary>
        public int GenerateReadWriteVersionListLength
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.GenerateReadWriteVersionListLength : 0;
            }
            set
            {
                if (m_ResourceUpdater == null)
                {
                    throw new GameFrameworkException("You can not use GenerateReadWriteVersionListLength at this time.");
                }

                m_ResourceUpdater.GenerateReadWriteVersionListLength = value;
            }
        }

        /// <summary>
        /// 获取正在应用的资源包路径。
        /// </summary>
        public string ApplyingResourcePackPath
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.ApplyingResourcePackPath : null;
            }
        }

        /// <summary>
        /// 获取等待应用资源数量。
        /// </summary>
        public int ApplyWaitingCount
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.ApplyWaitingCount : 0;
            }
        }

        /// <summary>
        /// 获取或设置资源更新重试次数。
        /// PS：这里设置的应该是所有AB的“最大重试次数”
        /// </summary>
        public int UpdateRetryCount
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.UpdateRetryCount : 0;
            }
            set
            {
                if (m_ResourceUpdater == null)
                {
                    throw new GameFrameworkException("You can not use UpdateRetryCount at this time.");
                }

                m_ResourceUpdater.UpdateRetryCount = value;
            }
        }

        /// <summary>
        /// 获取正在更新的资源组。
        /// </summary>
        public IResourceGroup UpdatingResourceGroup
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.UpdatingResourceGroup : null;
            }
        }

        /// <summary>
        /// 获取等待更新资源数量。
        /// </summary>
        public int UpdateWaitingCount
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.UpdateWaitingCount : 0;
            }
        }

        /// <summary>
        /// 获取使用时下载的等待更新资源数量。
        /// </summary>
        public int UpdateWaitingWhilePlayingCount
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.UpdateWaitingWhilePlayingCount : 0;
            }
        }

        /// <summary>
        /// 获取候选更新资源数量。
        /// </summary>
        public int UpdateCandidateCount
        {
            get
            {
                return m_ResourceUpdater != null ? m_ResourceUpdater.UpdateCandidateCount : 0;
            }
        }

        /// <summary>
        /// 获取加载资源代理总数量。
        /// </summary>
        public int LoadTotalAgentCount
        {
            get
            {
                return m_ResourceLoader.TotalAgentCount;
            }
        }

        /// <summary>
        /// 获取可用加载资源代理数量。
        /// </summary>
        public int LoadFreeAgentCount
        {
            get
            {
                return m_ResourceLoader.FreeAgentCount;
            }
        }

        /// <summary>
        /// 获取工作中加载资源代理数量。
        /// </summary>
        public int LoadWorkingAgentCount
        {
            get
            {
                return m_ResourceLoader.WorkingAgentCount;
            }
        }

        /// <summary>
        /// 获取等待加载资源任务数量。
        /// </summary>
        public int LoadWaitingTaskCount
        {
            get
            {
                return m_ResourceLoader.WaitingTaskCount;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get
            {
                return m_ResourceLoader.AssetAutoReleaseInterval;
            }
            set
            {
                m_ResourceLoader.AssetAutoReleaseInterval = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int AssetCapacity
        {
            get
            {
                return m_ResourceLoader.AssetCapacity;
            }
            set
            {
                m_ResourceLoader.AssetCapacity = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get
            {
                return m_ResourceLoader.AssetExpireTime;
            }
            set
            {
                m_ResourceLoader.AssetExpireTime = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int AssetPriority
        {
            get
            {
                return m_ResourceLoader.AssetPriority;
            }
            set
            {
                m_ResourceLoader.AssetPriority = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float ResourceAutoReleaseInterval
        {
            get
            {
                return m_ResourceLoader.ResourceAutoReleaseInterval;
            }
            set
            {
                m_ResourceLoader.ResourceAutoReleaseInterval = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int ResourceCapacity
        {
            get
            {
                return m_ResourceLoader.ResourceCapacity;
            }
            set
            {
                m_ResourceLoader.ResourceCapacity = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float ResourceExpireTime
        {
            get
            {
                return m_ResourceLoader.ResourceExpireTime;
            }
            set
            {
                m_ResourceLoader.ResourceExpireTime = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int ResourcePriority
        {
            get
            {
                return m_ResourceLoader.ResourcePriority;
            }
            set
            {
                m_ResourceLoader.ResourcePriority = value;
            }
        }
        #endregion

        #region 当外部调用“资源系统”执行某些操作时，提供给外部用于监听“资源系统部分阶段”的事件
        /// <summary>
        /// 资源校验开始事件。
        /// </summary>
        public event EventHandler<ResourceVerifyStartEventArgs> ResourceVerifyStart
        {
            add
            {
                m_ResourceVerifyStartEventHandler += value;
            }
            remove
            {
                m_ResourceVerifyStartEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源校验成功事件。
        /// </summary>
        public event EventHandler<ResourceVerifySuccessEventArgs> ResourceVerifySuccess
        {
            add
            {
                m_ResourceVerifySuccessEventHandler += value;
            }
            remove
            {
                m_ResourceVerifySuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源校验失败事件。
        /// </summary>
        public event EventHandler<ResourceVerifyFailureEventArgs> ResourceVerifyFailure
        {
            add
            {
                m_ResourceVerifyFailureEventHandler += value;
            }
            remove
            {
                m_ResourceVerifyFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源应用开始事件。
        /// </summary>
        public event EventHandler<ResourceApplyStartEventArgs> ResourceApplyStart
        {
            add
            {
                m_ResourceApplyStartEventHandler += value;
            }
            remove
            {
                m_ResourceApplyStartEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源应用成功事件。
        /// </summary>
        public event EventHandler<ResourceApplySuccessEventArgs> ResourceApplySuccess
        {
            add
            {
                m_ResourceApplySuccessEventHandler += value;
            }
            remove
            {
                m_ResourceApplySuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源应用失败事件。
        /// </summary>
        public event EventHandler<ResourceApplyFailureEventArgs> ResourceApplyFailure
        {
            add
            {
                m_ResourceApplyFailureEventHandler += value;
            }
            remove
            {
                m_ResourceApplyFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源更新开始事件。
        /// </summary>
        public event EventHandler<ResourceUpdateStartEventArgs> ResourceUpdateStart
        {
            add
            {
                m_ResourceUpdateStartEventHandler += value;
            }
            remove
            {
                m_ResourceUpdateStartEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源更新改变事件。
        /// </summary>
        public event EventHandler<ResourceUpdateChangedEventArgs> ResourceUpdateChanged
        {
            add
            {
                m_ResourceUpdateChangedEventHandler += value;
            }
            remove
            {
                m_ResourceUpdateChangedEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源更新成功事件。
        /// </summary>
        public event EventHandler<ResourceUpdateSuccessEventArgs> ResourceUpdateSuccess
        {
            add
            {
                m_ResourceUpdateSuccessEventHandler += value;
            }
            remove
            {
                m_ResourceUpdateSuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源更新失败事件。
        /// </summary>
        public event EventHandler<ResourceUpdateFailureEventArgs> ResourceUpdateFailure
        {
            add
            {
                m_ResourceUpdateFailureEventHandler += value;
            }
            remove
            {
                m_ResourceUpdateFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// 资源更新全部完成事件。
        /// </summary>
        public event EventHandler<ResourceUpdateAllCompleteEventArgs> ResourceUpdateAllComplete
        {
            add
            {
                m_ResourceUpdateAllCompleteEventHandler += value;
            }
            remove
            {
                m_ResourceUpdateAllCompleteEventHandler -= value;
            }
        }
        #endregion

        #region 完全是凑字数的“LoadAsset”，“LoadScene”，“UnloadScene”，“LoadBinary”,"LoadBinaryFromFileSystem","LoadBinarySegmentFromFileSystem"的多个垃圾重载方法
        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        public void LoadAsset(string assetName, LoadAssetCallbacks loadAssetCallbacks)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, null, Constant.DefaultPriority, loadAssetCallbacks, null);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        public void LoadAsset(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, assetType, Constant.DefaultPriority, loadAssetCallbacks, null);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        public void LoadAsset(string assetName, int priority, LoadAssetCallbacks loadAssetCallbacks)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, null, priority, loadAssetCallbacks, null);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadAsset(string assetName, LoadAssetCallbacks loadAssetCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, null, Constant.DefaultPriority, loadAssetCallbacks, userData);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        public void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, assetType, priority, loadAssetCallbacks, null);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadAsset(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, assetType, Constant.DefaultPriority, loadAssetCallbacks, userData);
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="assetName">要加载资源的名称。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadAsset(string assetName, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            m_ResourceLoader.LoadAsset(assetName, null, priority, loadAssetCallbacks, userData);
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="sceneAssetName">要加载场景资源的名称。</param>
        /// <param name="loadSceneCallbacks">加载场景回调函数集。</param>
        public void LoadScene(string sceneAssetName, LoadSceneCallbacks loadSceneCallbacks)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (loadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Load scene callbacks is invalid.");
            }

            m_ResourceLoader.LoadScene(sceneAssetName, Constant.DefaultPriority, loadSceneCallbacks, null);
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="sceneAssetName">要加载场景资源的名称。</param>
        /// <param name="priority">加载场景资源的优先级。</param>
        /// <param name="loadSceneCallbacks">加载场景回调函数集。</param>
        public void LoadScene(string sceneAssetName, int priority, LoadSceneCallbacks loadSceneCallbacks)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (loadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Load scene callbacks is invalid.");
            }

            m_ResourceLoader.LoadScene(sceneAssetName, priority, loadSceneCallbacks, null);
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="sceneAssetName">要加载场景资源的名称。</param>
        /// <param name="loadSceneCallbacks">加载场景回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void LoadScene(string sceneAssetName, LoadSceneCallbacks loadSceneCallbacks, object userData)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (loadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Load scene callbacks is invalid.");
            }

            m_ResourceLoader.LoadScene(sceneAssetName, Constant.DefaultPriority, loadSceneCallbacks, userData);
        }

        /// <summary>
        /// 异步卸载场景。
        /// </summary>
        /// <param name="sceneAssetName">要卸载场景资源的名称。</param>
        /// <param name="unloadSceneCallbacks">卸载场景回调函数集。</param>
        public void UnloadScene(string sceneAssetName, UnloadSceneCallbacks unloadSceneCallbacks)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                throw new GameFrameworkException("Scene asset name is invalid.");
            }

            if (unloadSceneCallbacks == null)
            {
                throw new GameFrameworkException("Unload scene callbacks is invalid.");
            }

            m_ResourceLoader.UnloadScene(sceneAssetName, unloadSceneCallbacks, null);
        }

        /// <summary>
        /// 异步加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <param name="loadBinaryCallbacks">加载二进制资源回调函数集。</param>
        public void LoadBinary(string binaryAssetName, LoadBinaryCallbacks loadBinaryCallbacks)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (loadBinaryCallbacks == null)
            {
                throw new GameFrameworkException("Load binary callbacks is invalid.");
            }

            m_ResourceLoader.LoadBinary(binaryAssetName, loadBinaryCallbacks, null);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <returns>存储加载二进制资源的二进制流。</returns>
        public byte[] LoadBinaryFromFileSystem(string binaryAssetName)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            return m_ResourceLoader.LoadBinaryFromFileSystem(binaryAssetName);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源的二进制流。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinaryFromFileSystem(string binaryAssetName, byte[] buffer)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinaryFromFileSystem(binaryAssetName, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源。
        /// </summary>
        /// <param name="binaryAssetName">要加载二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源的二进制流。</param>
        /// <param name="startIndex">存储加载二进制资源的二进制流的起始位置。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinaryFromFileSystem(string binaryAssetName, byte[] buffer, int startIndex)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinaryFromFileSystem(binaryAssetName, buffer, startIndex, buffer.Length - startIndex);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="length">要加载片段的长度。</param>
        /// <returns>存储加载二进制资源片段内容的二进制流。</returns>
        public byte[] LoadBinarySegmentFromFileSystem(string binaryAssetName, int length)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, 0, length);
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
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, offset, length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinarySegmentFromFileSystem(string binaryAssetName, byte[] buffer)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, 0, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
        /// <param name="length">要加载片段的长度。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinarySegmentFromFileSystem(string binaryAssetName, byte[] buffer, int length)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, 0, buffer, 0, length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
        /// <param name="startIndex">存储加载二进制资源片段内容的二进制流的起始位置。</param>
        /// <param name="length">要加载片段的长度。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinarySegmentFromFileSystem(string binaryAssetName, byte[] buffer, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, 0, buffer, startIndex, length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="offset">要加载片段的偏移。</param>
        /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinarySegmentFromFileSystem(string binaryAssetName, int offset, byte[] buffer)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, offset, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 从文件系统中加载二进制资源的片段。
        /// </summary>
        /// <param name="binaryAssetName">要加载片段的二进制资源的名称。</param>
        /// <param name="offset">要加载片段的偏移。</param>
        /// <param name="buffer">存储加载二进制资源片段内容的二进制流。</param>
        /// <param name="length">要加载片段的长度。</param>
        /// <returns>实际加载了多少字节。</returns>
        public int LoadBinarySegmentFromFileSystem(string binaryAssetName, int offset, byte[] buffer, int length)
        {
            if (string.IsNullOrEmpty(binaryAssetName))
            {
                throw new GameFrameworkException("Binary asset name is invalid.");
            }

            if (buffer == null)
            {
                throw new GameFrameworkException("Buffer is invalid.");
            }

            return m_ResourceLoader.LoadBinarySegmentFromFileSystem(binaryAssetName, offset, buffer, 0, length);
        }

        #endregion

        #region 其他垃圾方法
        /// <summary>
        /// 停止更新资源。
        /// </summary>
        public void StopUpdateResources()
        {
            if (m_ResourceMode == ResourceMode.Unspecified)
            {
                throw new GameFrameworkException("You must set resource mode first.");
            }

            if (m_ResourceMode != ResourceMode.Updatable && m_ResourceMode != ResourceMode.UpdatableWhilePlaying)
            {
                throw new GameFrameworkException("You can not use StopUpdateResources without updatable resource mode.");
            }

            if (m_ResourceUpdater == null)
            {
                throw new GameFrameworkException("You can not use StopUpdateResources at this time.");
            }

            m_ResourceUpdater.StopUpdateResources();
            m_UpdateResourcesCompleteCallback = null;
        }

        /// <summary>
        /// 获取所有加载资源任务的信息。
        /// </summary>
        /// <returns>所有加载资源任务的信息。</returns>
        public TaskInfo[] GetAllLoadAssetInfos()
        {
            return m_ResourceLoader.GetAllLoadAssetInfos();
        }

        /// <summary>
        /// 获取所有加载资源任务的信息。
        /// </summary>
        /// <param name="results">所有加载资源任务的信息。</param>
        public void GetAllLoadAssetInfos(List<TaskInfo> results)
        {
            m_ResourceLoader.GetAllLoadAssetInfos(results);
        }

        /// <summary>
        /// 获取默认资源组。
        /// </summary>
        /// <returns>默认资源组。</returns>
        public IResourceGroup GetResourceGroup()
        {
            return GetResourceGroup(string.Empty);
        }

        /// <summary>
        /// 获取所有资源组。
        /// </summary>
        /// <param name="results">所有资源组。</param>
        public void GetAllResourceGroups(List<IResourceGroup> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<string, ResourceGroup> resourceGroup in m_ResourceGroups)
            {
                results.Add(resourceGroup.Value);
            }
        }

        /// <summary>
        /// 获取资源组集合。
        /// </summary>
        /// <param name="resourceGroupNames">要获取的资源组名称的集合。</param>
        /// <returns>要获取的资源组集合。</returns>
        public IResourceGroupCollection GetResourceGroupCollection(params string[] resourceGroupNames)
        {
            if (resourceGroupNames == null || resourceGroupNames.Length < 1)
            {
                throw new GameFrameworkException("Resource group names is invalid.");
            }

            ResourceGroup[] resourceGroups = new ResourceGroup[resourceGroupNames.Length];
            for (int i = 0; i < resourceGroupNames.Length; i++)
            {
                if (string.IsNullOrEmpty(resourceGroupNames[i]))
                {
                    throw new GameFrameworkException("Resource group name is invalid.");
                }

                resourceGroups[i] = (ResourceGroup)GetResourceGroup(resourceGroupNames[i]);
                if (resourceGroups[i] == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Resource group '{0}' is not exist.", resourceGroupNames[i]));
                }
            }

            return new ResourceGroupCollection(resourceGroups, m_ResourceInfos);
        }

        /// <summary>
        /// 获取资源组集合。
        /// </summary>
        /// <param name="resourceGroupNames">要获取的资源组名称的集合。</param>
        /// <returns>要获取的资源组集合。</returns>
        public IResourceGroupCollection GetResourceGroupCollection(List<string> resourceGroupNames)
        {
            if (resourceGroupNames == null || resourceGroupNames.Count < 1)
            {
                throw new GameFrameworkException("Resource group names is invalid.");
            }

            ResourceGroup[] resourceGroups = new ResourceGroup[resourceGroupNames.Count];
            for (int i = 0; i < resourceGroupNames.Count; i++)
            {
                if (string.IsNullOrEmpty(resourceGroupNames[i]))
                {
                    throw new GameFrameworkException("Resource group name is invalid.");
                }

                resourceGroups[i] = (ResourceGroup)GetResourceGroup(resourceGroupNames[i]);
                if (resourceGroups[i] == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Resource group '{0}' is not exist.", resourceGroupNames[i]));
                }
            }

            return new ResourceGroupCollection(resourceGroups, m_ResourceInfos);
        }

        #endregion

    }
}
