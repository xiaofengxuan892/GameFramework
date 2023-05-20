//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Download;
using GameFramework.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 资源更新器。
        /// 扩展：三个问题
        /// 1.是否支持断点下载？如果某Resource下载了一部分，但本地版本列表中没有记录，
        ///   那重新进入游戏时，使用"Download模块"的功能开启下载时是否会先把原来的文件删除掉，还是直接使用”FileMode.Write“权限直接覆盖原有文件
        /// 答：支持断点下载，即上次下载的临时文件会被保留下载，再次下载时会直接读取之前的“xx.download”文件，从原有fileStream的末尾继续写入数据
        ///    这样即使上次下载没有完成，本次也可以继续下载
        ///    但当前框架存在一些问题：即频繁点击“Play”按钮后出现部分文件“xx.download”没有删除，导致后续按照正常流程下载时依然无法下载该文件，游戏整个被卡住的情况
        ///    解决方式则是在“RetryCount”的次数限制过程中对该“xx.download”删除，以方便下次正常下载
        ///    经过实际测试，该办法安全有效，可以解决该问题
        ///
        /// 2.UpdatableWhilePlaying模式在本框架内如何使用？真的是”支持“，而非强迫所有的Resources都使用该模式？
        ///   针对游戏运行非必要的资源是否可以使用该模式，并且保证不会卡住游戏流程？
        /// 答：经过实际测试可知，”UpdatableWhilePlaying“仅仅只是代表当前ResourceMode支持”边玩边下载“，
        ///    并不是说”设置了该ResourceMode后所有的Resources都必须在实际用到时才下载“
        ///    而是：当设置了该ResourceMode后，如果有Resource在初始下载时失败了，如果支持”边玩边下载“，那么在游戏中实际用到该Resource时就可以再次尝试下载
        ///    这应该才是”UpdatableWhilePlaying“模式真正的作用
        ///    本框架当前的demo没有体现”UpdatableWhilePlaying“作用的地方
        ///    但是可以使用”UpdatableWhilePlaying“的Resource必须是”不会影响游戏正常流程的Resource“。
        ///    如：某些活动只有在玩家达到指定等级后才会开启，那么这些Resource就没有必要强制要求玩家在进入游戏”热更新“的界面一次性把所有Resource都下载下来
        ///    可以设置玩家在”热更新界面“只下载”进入游戏必须要具备的资源“，其他非必要资源可以等到玩家实际用到该功能时才开始下载
        ///    这样的好处在于”节省玩家进入游戏的成本“，可以更快的体验游戏，增加活跃度
        ///    当然也可以一次性下载，但是如果这些非必要资源在”热更新界面“下载失败了，那么也不要让其卡住流程，可以直接进入游戏，在实际用到的时候再次尝试下载即可
        ///    因为有的时候可能资源过多，或者玩家网络不好，不要因为这些”非必要资源“卡住玩家流程
        ///
        /// 3.”资源包“应该如何使用？？是否真的可以替代”persistentDataPath“中的Resources下载？？那与”streamingAssetPaths“有何区别？？
        /// 答：“资源包”仅是“Full”文件夹下的所有资源的另一种形式，作用其实是一样的，只是将各个零散的Resource文件集成到一个文件中，这个文件就是“资源包”
        ///    并且“资源包”加入了“版本区分”的特性，因此也被称为“增量包”
        ///    “资源包”就是将所有需要Update的资源达成一个“统一的文件”。在“热更新”时不需要一个个下载那些AB文件，只需要通过比对之后下载目标版本的“资源包文件”即可
        ///    之后从该“资源包”中读取数据应用到目标Update资源即可
        ///    这样的好处在于：避免下载零散文件，上传到资源服务器时也只需要上传一个“资源包文件”即可，简化过程
        ///    但两者的效果其实是一样的。所以本框架当前并没有使用“资源包”也是完全可以正常运行的。只是此时需要从“资源服务器”上一个个下载目标Resources
        ///    同时“资源包”中还加入了“增量更新”的机制：即比较之前打出来的各个“internal Resource Version”的“GameFrameworkVersion.dat”文件来确定
        ///    从“资源版本1”到“资源版本2”有哪些AB发生了变化，然后只把这些“改变”的AB文件放入“资源包”中，这也就是所谓的“增量包”
        ///    但如果“当前最新版本”已经是“8”，而有玩家从安装游戏后一直就没有更新资源，此时“玩家本地版本号为0”。在这种情况下打的资源包就是“全量包”
        ///    即没有重新打“安装包”，仅仅只是更新了资源版本，那么会从打一个包含所有“GameFrameworkVersion.dat”中要求的Resources的“全量包”
        ///    整个游戏过程中“internalResourceVersion”是一直递增的，其代表的是“资源的版本”，与“游戏版本GameVersion”，如“1.0.0”是不同的。
        ///    并且不论游戏版本是否改变，只要“重新打了资源”，该“资源版本internalResourceVersion”数值就需要递增
        ///总结：1.“资源包”与“普通零散的AB文件”其实作用是一样的，只是前者只需要下载一个“总的文件”，而后者需要一个个下载需要Update的AB文件
        ///    并且“资源包”更加紧凑，保密性更好(因为都包含在一个文件中，且为二进制格式)
        ///    但“资源包”下载完成后需要应用(将目标AB的数据从资源包中读取出来，然后写入配置的fileSystem中)，这个过程其实和一个个下载AB是一样的
        ///    但“资源包”增加了“读取”的过程，并且在任何一个AB应用完成后需要从“资源包”中删除该AB的数据(否则资源包在应用到最后会占据两倍的存储空间)
        ///    并且在所有AB都应用完毕后必须要将“资源包”本身删除，不然会在“读写区”占用两倍的存储空间
        ///    虽然本框架当前没有写应用“资源包”的逻辑，但“资源包”必然只能用于“热更新模式”，仅仅只是将所有需要下载的AB打进一个“统一的文件”而已。
        ///    功能效果与当前并没有任何区别
        ///    2.从本质上来讲：“资源包”和“当前使用的零散更新的AB文件”仅仅只是“热更新”的不同路径，两者都能达到同一目的，只是资源的存在形式不同，
        ///    前者是“集中在一个文件”中 —— 根据包含AB的情况分为“增量包”和“全量包”
        ///    后者则是零散的文件，需要一个个单独下载
        ///    本框架demo中其实不应该把两者写在一个脚本中。因为这两个路径应该是独立的，如果使用了“资源包”，其实就完全不需要“下载零散的AB文件”了
        ///    因为“资源包”中必然已经包含了所有需要Update的AB数据，直接下载“完整的资源包文件”即可，完全不需要再单独另外下载“一个个的AB文件”
        ///    如果“有AB连资源包中都没有”，那么一定是编辑器中“ResourceBuilder”或“ResourcePackBuilder”打包时就已经出错了
        ///    绝对不是“这里加载机制”的问题
        /// 实现“资源包”逻辑的方式：
        /// 1.读写区本地版本文件增加“当前内部资源版本号”参数：用于确定从“资源服务器”上下载对应版本的“资源包”，同时只有在“应用资源包”中所有AB正确无误后才会更新“本地内部资源版本号”
        /// 2.下载到“本地读写区”的“资源包”同样需要增加“版本文件”：因为每成功应用一个AB，则需要从“资源包”中删除该AB的数据，同时重写该资源包的“版本文件”
        /// </summary>
        private sealed partial class ResourceUpdater
        {
            private const int CachedHashBytesLength = 4;
            private const int CachedBytesLength = 0x1000;

            private readonly ResourceManager m_ResourceManager;
            //始终代表当前剩余需要更新的Resource信息，如果该集合数量为0，则说明当前所有资源都已更新完毕
            private readonly Dictionary<ResourceName, UpdateInfo> m_UpdateCandidateInfo;

            //“资源包”应用相关参数
            //经过“ResourcePackVersionList”和“m_UpdateCandidateInfo”筛选出来的可以用于“应用”的资源信息“ApplyInfo”集合
            private readonly Queue<ApplyInfo> m_ApplyWaitingInfo;
            private string m_ApplyingResourcePackPath;
            private FileStream m_ApplyingResourcePackStream;

            //下载资源的两种情况：
            //情况一：按照”ResourceGroup“更新本Group下所有需要Updatable的资源，并存储在本集合中
            //      每次更换ResourceGroup时都需要将其清空
            private ResourceGroup m_UpdatingResourceGroup;
            private readonly List<UpdateInfo> m_UpdateWaitingInfo;
            //用于从指定的”ResourceGroup“中获取到其中的所有”ResourceName“
            private readonly List<ResourceName> m_CachedResourceNames;
            //情况二：
            //当在使用“LoadAsset、LoadScene”方法加载对象，如果该对象所在的Resource尚未下载完成，则需要“updatableWhilePlaying”，
            //此时会将其放入本集合中
            private readonly HashSet<UpdateInfo> m_UpdateWaitingInfoWhilePlaying;

            //下载Resource并校验其hahscode等相关的参数
            private IDownloadManager m_DownloadManager;
            private int m_UpdateRetryCount;
            private readonly byte[] m_CachedHashBytes;
            private readonly byte[] m_CachedBytes;
            private bool m_CheckResourcesComplete;
            //代表”ApplyResource“或”DownloadResource“的过程中是否有错误
            private bool m_FailureFlag;

            //重写本地版本文件的相关参数
            //这两个傻逼的参数主要用于判定当“更新指定字节”的资源时会重新生成“本地版本列表”
            //其中第一个参数“m_GenerateReadWriteVersionListLength”字节数限制，
            //第二个参数“m_CurrentGenerateReadWriteVersionListLength”则代表当前已经累计了多少个字节，
            //当满足指定字节的数量限制后则会重写本地版本文件，并重置该参数
            //这两个参数的傻逼命名也是让人无语，如果不是看执行逻辑，这他妈的谁能明白
            private int m_GenerateReadWriteVersionListLength;
            private int m_CurrentGenerateReadWriteVersionListLength;
            private string m_ReadWriteVersionListFileName;
            private string m_ReadWriteVersionListTempFileName;
            //重写”本地版本文件“时需要用到的参数
            private readonly SortedDictionary<string, List<int>> m_CachedFileSystemsForGenerateReadWriteVersionList;

            public GameFrameworkAction<string, int, long> ResourceApplyStart;
            public GameFrameworkAction<ResourceName, string, string, int, int> ResourceApplySuccess;
            public GameFrameworkAction<ResourceName, string, string> ResourceApplyFailure;
            public GameFrameworkAction<string, bool> ResourceApplyComplete;

            public GameFrameworkAction<ResourceName, string, string, int, int, int> ResourceUpdateStart;
            public GameFrameworkAction<ResourceName, string, string, int, int> ResourceUpdateChanged;
            public GameFrameworkAction<ResourceName, string, string, int, int> ResourceUpdateSuccess;
            public GameFrameworkAction<ResourceName, string, int, int, string> ResourceUpdateFailure;
            public GameFrameworkAction<ResourceGroup, bool> ResourceUpdateComplete;
            public GameFrameworkAction ResourceUpdateAllComplete;

            /// <summary>
            /// 初始化资源更新器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public ResourceUpdater(ResourceManager resourceManager) {
                m_ResourceManager = resourceManager;
                m_ApplyWaitingInfo = new Queue<ApplyInfo>();
                m_UpdateWaitingInfo = new List<UpdateInfo>();
                m_UpdateWaitingInfoWhilePlaying = new HashSet<UpdateInfo>();
                m_UpdateCandidateInfo = new Dictionary<ResourceName, UpdateInfo>();
                m_CachedFileSystemsForGenerateReadWriteVersionList =
                    new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
                m_CachedResourceNames = new List<ResourceName>();
                m_CachedHashBytes = new byte[CachedHashBytesLength];
                m_CachedBytes = new byte[CachedBytesLength];
                m_DownloadManager = null;
                m_CheckResourcesComplete = false;
                m_ApplyingResourcePackPath = null;
                m_ApplyingResourcePackStream = null;
                m_UpdatingResourceGroup = null;
                m_GenerateReadWriteVersionListLength = 0;
                m_CurrentGenerateReadWriteVersionListLength = 0;
                m_UpdateRetryCount = 3;
                m_FailureFlag = false;
                //这里主要是为“本地版本文件”使用，命名中应该增加“Local”关键字
                m_ReadWriteVersionListFileName = Utility.Path.GetRegularPath(
                    Path.Combine(m_ResourceManager.m_ReadWritePath, LocalVersionListFileName));
                m_ReadWriteVersionListTempFileName = Utility.Text.Format("{0}.{1}",
                    m_ReadWriteVersionListFileName, TempExtension);

                ResourceApplyStart = null;
                ResourceApplySuccess = null;
                ResourceApplyFailure = null;
                ResourceApplyComplete = null;
                ResourceUpdateStart = null;
                ResourceUpdateChanged = null;
                ResourceUpdateSuccess = null;
                ResourceUpdateFailure = null;
                ResourceUpdateComplete = null;
                ResourceUpdateAllComplete = null;
            }

            /// <summary>
            /// 资源更新器轮询。
            /// PS：该轮询逻辑的执行完全由外界触发，只有在”设置了资源包路径“或者”指定更新的ResourceGroup“后才会触发实质的轮询逻辑，
            ///     并不是直接执行的
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds) {
                //如果当前有可以直接使用的“资源包”，则执行“ApplyResource”将其分别应用
                if (m_ApplyingResourcePackStream != null) {
                    //“m_ApplyWaitingInfo”集合中的元素必然全部包含在“m_UpdateCandidateInfo”中
                    //只有在“m_UpdateCandidateInfo”集合中已经包含的“目标Resource”，才能从“资源包”中获取使用
                    while (m_ApplyWaitingInfo.Count > 0) {
                        ApplyInfo applyInfo = m_ApplyWaitingInfo.Dequeue();
                        //由于这里使用的是”Dequeue“，因此从”m_ApplyWaitingInfo“中遍历时会自动减少集合中的”Count“
                        if (ApplyResource(applyInfo)) {
                            //如果应用资源成功，则直接返回 —— 这说明每一帧中最多只应用一个Resource
                            //如果资源应用失败，则继续下一个Resource的应用，直到有一个成功为止。
                            //并且应用失败的资源不会再次进行应用，其依然存在于”m_UpdateCandidateInfo“集合中
                            return;
                        }
                    }
                    //至此，所有”资源包“中可以直接使用的资源都已应用完毕，此时”m_UpdateCandidateInfo“集合中留存的Resource信息
                    //则只能通过”Download“的方式获取

                    Array.Clear(m_CachedBytes, 0, CachedBytesLength);
                    //由于ResourcePack中所有Resource都已处理完毕，因此这里清除相关数据
                    string resourcePackPath = m_ApplyingResourcePackPath;
                    m_ApplyingResourcePackPath = null;   // 引用类型对象string也需要置为null
                    m_ApplyingResourcePackStream.Dispose();
                    m_ApplyingResourcePackStream = null;
                    if (ResourceApplyComplete != null) {
                        //从现有逻辑来看，”ResourceApply“和”ResourceUpdate“分别都会有代表其过程是否顺利的状态参数
                        //但是两者不可同时进行，即如果当前有“ResourcePack”正在“Apply”，则必然无法“ResourceUpdate”
                        //因此两者共用一个“表示执行过程是否顺利”的标志位“m_FailureFlag”
                        ResourceApplyComplete(resourcePackPath, !m_FailureFlag);
                    }

                    //正常情况下“资源包”中包含所有需要更新的Resources，所以通常在“应用资源包”结束后确实是“m_UpdateCandidateInfo.Count <= 0”
                    //所以这里其实没有“多余”
                    if (m_UpdateCandidateInfo.Count <= 0 && ResourceUpdateAllComplete != null) {
                        ResourceUpdateAllComplete();
                    }

                    //该语句代表只要有“资源包正在应用”，则必然不会执行后续逻辑
                    return;
                }

                //这里按照”ResourceGroup“来分别下载各个”资源分组“中的Resource，并将其放入”m_UpdateWaitingInfo“集合中
                //从此逻辑可知：是否开启资源的下载必须由外部启动，指定需要更新的ResourceGroup(之后才会将其放入m_UpdateWaitingInfo集合中)
                if (m_UpdateWaitingInfo.Count > 0) {
                    int freeCount = m_DownloadManager.FreeAgentCount - m_DownloadManager.WaitingTaskCount;
                    if (freeCount > 0) {
                        for (int i = 0, count = 0; i < m_UpdateWaitingInfo.Count && count < freeCount; i++) {
                            //“开启下载任务”并没有强制“每帧只能下载一个”，因为“DownloadResource”仅仅只是将“downloadTask”添加到“任务池”中
                            //并没有比较复杂的执行逻辑，所以这里没有“强制每帧只能开启下载一个Resource”
                            //但“应用资源包”中由于每个Resource有比较复杂的“读取数据然后校验数据”的逻辑，因此“强制每帧只能执行一个Resource的应用”
                            if (DownloadResource(m_UpdateWaitingInfo[i])) {
                                count++;
                            }
                        }
                    }

                    return;
                }
            }

            /// <summary>
            /// 关闭并清理资源更新器。
            /// </summary>
            public void Shutdown() {
                if (m_DownloadManager != null) {
                    m_DownloadManager.DownloadStart -= OnDownloadStart;
                    m_DownloadManager.DownloadUpdate -= OnDownloadUpdate;
                    m_DownloadManager.DownloadSuccess -= OnDownloadSuccess;
                    m_DownloadManager.DownloadFailure -= OnDownloadFailure;
                }

                m_UpdateWaitingInfo.Clear();
                m_UpdateCandidateInfo.Clear();
                m_CachedFileSystemsForGenerateReadWriteVersionList.Clear();
            }

            #region 提供给“ResourceChecker”用于向“m_UpdateCandidateInfo”中添加“所有待更新的资源”和“更新操作判断条件m_CheckResourceComplete“
            /// <summary>
            /// 增加资源更新。
            /// PS：调用时机在“ResourceChecker”中比较“服务器和本地版本列表”后筛选出需要更新的Resource，
            ///    因此该方法的调用时机其实在“ResourceChecker”中
            /// </summary>
            /// <param name="resourceName">资源名称。</param>
            /// <param name="fileSystemName">资源所在的文件系统名称。</param>
            /// <param name="loadType">资源加载方式。</param>
            /// <param name="length">资源大小。</param>
            /// <param name="hashCode">资源哈希值。</param>
            /// <param name="compressedLength">压缩后大小。</param>
            /// <param name="compressedHashCode">压缩后哈希值。</param>
            /// <param name="resourcePath">资源下载完成后的本地存储路径。</param>
            public void AddResourceUpdate(ResourceName resourceName, string fileSystemName, LoadType loadType,
                int length, int hashCode, int compressedLength, int compressedHashCode, string resourcePath) {
                m_UpdateCandidateInfo.Add(resourceName, new UpdateInfo(resourceName, fileSystemName, loadType,
                    length, hashCode, compressedLength, compressedHashCode, resourcePath));
            }

            /// <summary>
            /// 检查资源完成。
            /// PS：提供“资源是否检测完毕”的标志判定，本脚本中所有重要操作都必须在“CheckResourceComplete”之后才可执行
            /// </summary>
            /// <param name="needGenerateReadWriteVersionList">是否需要生成读写区版本资源列表。</param>
            public void CheckResourceComplete(bool needGenerateReadWriteVersionList) {
                m_CheckResourcesComplete = true;
                //如果“moveCount > 0“，说明有AB的fileSystem配置发生了变动，同样会导致”LocalVersionList“中的”m_FileSystems“变量发生变化，
                //所以这种情况也需要重写”本地版本文件“
                if (needGenerateReadWriteVersionList) {
                    GenerateReadWriteVersionList();
                }
            }
            #endregion

            #region 应用”资源包“中的”资源“，该资源无需下载，可直接从资源包中获取，因此在应用成功后会移除”m_UpdateCandidateInfo“集合中该Resource
            /// <summary>
            /// 应用指定资源包的资源。
            /// </summary>
            /// <param name="resourcePackPath">要应用的资源包路径。</param>
            public void ApplyResources(string resourcePackPath) {
                if (!m_CheckResourcesComplete) {
                    throw new GameFrameworkException("You must check resources complete first.");
                }

                if (m_ApplyingResourcePackStream != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource pack '{0}' being applied.", m_ApplyingResourcePackPath));
                }

                //如果当前有ResourceGroup正在更新，则不能执行该方法
                //PS：这里源码的“提示内容”也说明：资源包与“普通的AB零散文件”，两者代表的是两种不同的模式，作用是等效的，但两者通常不会同时执行
                if (m_UpdatingResourceGroup != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource group '{0}' being updated.", m_UpdatingResourceGroup.Name));
                }

                //资源包的应用难道不是在“热更新界面“就执行的吗？为什么还要考虑”进入游戏后的边玩边下载模式下“的”资源包应用“？其实应该是没有多大意义的
                if (m_UpdateWaitingInfoWhilePlaying.Count > 0) {
                    throw new GameFrameworkException("There are already some resources being updated while playing.");
                }

                try {
                    long length = 0L;
                    ResourcePackVersionList versionList = default(ResourcePackVersionList);
                    using (FileStream fileStream = new FileStream(resourcePackPath, FileMode.Open, FileAccess.Read)) {
                        length = fileStream.Length;
                        versionList = m_ResourceManager.m_ResourcePackVersionListSerializer.Deserialize(fileStream);
                    }

                    if (!versionList.IsValid) {
                        throw new GameFrameworkException("Deserialize resource pack version list failure.");
                    }

                    //“资源数据在资源包中的起始偏移位置Offset” + “资源数据的总大小Length” = “fileStream对象的总大小”
                    //从这说明：资源包中不只包含各个信息总表，还包含“真实的AB数据”
                    if (versionList.Offset + versionList.Length != length) {
                        throw new GameFrameworkException("Resource pack length is invalid.");
                    }

                    Debug.LogFormat("resourcePackPath: {0}", resourcePackPath);

                    m_ApplyingResourcePackPath = resourcePackPath;
                    //由于前面使用“using”语句创建的“fileStream”对象仅仅只是为了获取到“length”和“versionList”对象，因此该逻辑执行完后
                    //“fileStream”已经被置为null了，所以这里需要重新创建新的“fileStream”对象
                    m_ApplyingResourcePackStream = new FileStream(resourcePackPath, FileMode.Open, FileAccess.Read);
                    //这里的“versionList.Offset”应该是指在“版本列表中”真正Resource信息开始的地方
                    m_ApplyingResourcePackStream.Position = versionList.Offset;
                    m_FailureFlag = false;

                    long totalLength = 0L;
                    ResourcePackVersionList.Resource[] resources = versionList.GetResources();
                    foreach (ResourcePackVersionList.Resource resource in resources) {
                        ResourceName resourceName =
                            new ResourceName(resource.Name, resource.Variant, resource.Extension);
                        UpdateInfo updateInfo = null;
                        //”ResourcePack“中已经包含的”Resource“则无需再下载，故从“m_UpdateCandidateInfo”集合中查询
                        if (!m_UpdateCandidateInfo.TryGetValue(resourceName, out updateInfo)) {
                            continue;
                        }

                        //从后续逻辑来看，这里无论使用“HashCode”或“CompressedHashCode”都是可以的，这里只是大致做一个验证，
                        //因此只要大致信息一致即可，后续在“应用每个单独的Resource”时会对所有的“HashCode/CompressedHashCode/Length/CompressedLength”都验证一遍
                        if (updateInfo.LoadType == (LoadType) resource.LoadType &&
                            updateInfo.Length == resource.Length && updateInfo.HashCode == resource.HashCode) {
                            totalLength += resource.Length;
                            //加入“等待应用的集合中”
                            m_ApplyWaitingInfo.Enqueue(new ApplyInfo(resourceName, updateInfo.FileSystemName,
                                (LoadType) resource.LoadType, resource.Offset, resource.Length, resource.HashCode,
                                resource.CompressedLength, resource.CompressedHashCode, updateInfo.ResourcePath));
                        }
                    }

                    //“ResourcePack”中包含哪些目标Resource，直接使用“资源包”中的Resource
                    if (ResourceApplyStart != null) {
                        ResourceApplyStart(m_ApplyingResourcePackPath, m_ApplyWaitingInfo.Count, totalLength);
                    }
                }
                catch (Exception exception) {
                    //注意：如果正常应用该“资源包”，其“Dispose”以及置空“null”操作在“Update”方法中
                    if (m_ApplyingResourcePackStream != null) {
                        m_ApplyingResourcePackStream.Dispose();  //这里其实用“Close”方法也可以
                        m_ApplyingResourcePackStream = null;
                    }

                    throw new GameFrameworkException(
                        Utility.Text.Format("Apply resources '{0}' with exception '{1}'.", resourcePackPath, exception),
                        exception);
                }
            }

            //针对于那些在“m_UpdateCandidateInfo”集合中，并且“ResourcePack”中已经包含的“Resource”，
            //即这些资源无需再次从网络上下载，直接使用“ResourcePack”中的相应Resource即可
            private bool ApplyResource(ApplyInfo applyInfo) {
                //记录在读取”ResourcePack“中具体某个Resource之前其初始位置(ResourcePack中除了Resource信息外可能还包含其他一些必要信息)
                //该position可能代表的是ResourcePack中实际Resource数据的起始位置
                //后续需要读取其中某个Resource的信息时，则根据applyInfo.Offset即可定位到目标Resource数据的起始位置
                //所以在每次读取Resource信息前需要将该位置存储起来，当读取完毕后需要将”m_ApplyingResourcePackStream.Position“归位
                long position = m_ApplyingResourcePackStream.Position;
                try {
                    //这里指的是“ResourcePack”中已经包含的“Resource”的信息。检测该资源包中的Resource是否是“压缩”的
                    bool compressed = applyInfo.Length != applyInfo.CompressedLength ||
                                      applyInfo.HashCode != applyInfo.CompressedHashCode;

                    int bytesRead = 0;  //局部变量，后面会用到
                    //本applyInfo的Resource的总的字节数组大小
                    int bytesLeft = applyInfo.CompressedLength;
                    //这里的“applyInfo.ResourcePath”指的是将“资源包”中的“目标Resource”放置在“本地读写区”中的路径
                    string directory = Path.GetDirectoryName(applyInfo.ResourcePath);
                    if (!Directory.Exists(directory)) {
                        Directory.CreateDirectory(directory);
                    }

                    //"applyInfo.Offset"指该“Resource”在资源包中的“偏移量”
                    m_ApplyingResourcePackStream.Position += applyInfo.Offset;
                    //注意：这里的“FileStream”是使用“FileMode.Create”，因此这也证明会根据“applyInfo.ResourcePath”在本地读写区创建文件
                    using (FileStream fileStream =
                           new FileStream(applyInfo.ResourcePath, FileMode.Create, FileAccess.ReadWrite)) {
                        //每次只从“m_ApplyingResourcePackStream”中读取到“CachedBytesLength”字节的数据存储到“m_CachedBytes“数组中
                        while ((bytesRead = m_ApplyingResourcePackStream.Read(m_CachedBytes, 0,
                                   bytesLeft < CachedBytesLength ? bytesLeft : CachedBytesLength)) > 0) {
                            //更新剩余的字节数量
                            bytesLeft -= bytesRead;
                            //把”m_CachedBytes“中的数据写入fileStream中
                            fileStream.Write(m_CachedBytes, 0, bytesRead);
                        }
                        //至此已经将”ResourcePack“中具体某个Resource的信息全部读取完毕，并保存到”applyInfo.ResourcePath“的本地路径

                        if (compressed) {
                            //每次在获取hashCode前都需要将该fileStream.Position置为0，后续非压缩的文件也是这样的
                            fileStream.Position = 0L;
                            //针对压缩文件的HashCode直接使用”Verified.GetCrc32“方法即可获取
                            int hashCode = Utility.Verifier.GetCrc32(fileStream);
                            if (hashCode != applyInfo.CompressedHashCode) {
                                if (ResourceApplyFailure != null) {
                                    string errorMessage = Utility.Text.Format(
                                        "Resource compressed hash code error, need '{0}', applied '{1}'.",
                                        applyInfo.CompressedHashCode, hashCode);
                                    ResourceApplyFailure(applyInfo.ResourceName, m_ApplyingResourcePackPath,
                                        errorMessage);
                                }

                                //若数据不匹配，则代表”Apply“该Resource失败
                                m_FailureFlag = true;
                                return false;
                            }

                            fileStream.Position = 0L;
                            m_ResourceManager.PrepareCachedStream();
                            if (!Utility.Compression.Decompress(fileStream, m_ResourceManager.m_CachedStream)) {
                                if (ResourceApplyFailure != null) {
                                    string errorMessage = Utility.Text.Format("Unable to decompress resource '{0}'.",
                                        applyInfo.ResourcePath);
                                    ResourceApplyFailure(applyInfo.ResourceName, m_ApplyingResourcePackPath,
                                        errorMessage);
                                }

                                m_FailureFlag = true;
                                return false;
                            }

                            //将解压出来并暂存在”m_CachedStream“中的数据写入原来的fileStream中
                            fileStream.Position = 0L;
                            fileStream.SetLength(0L);
                            fileStream.Write(m_ResourceManager.m_CachedStream.GetBuffer(), 0,
                                (int) m_ResourceManager.m_CachedStream.Length);
                        }
                        else {
                            int hashCode = 0;
                            fileStream.Position = 0L;
                            //从”内存“或”二进制“文件中加载的资源其HashCode计算方式
                            if (applyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt ||
                                applyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt
                                || applyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt ||
                                applyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt) {
                                Utility.Converter.GetBytes(applyInfo.HashCode, m_CachedHashBytes);
                                if (applyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt ||
                                    applyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt) {
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes,
                                        Utility.Encryption.QuickEncryptLength);
                                }
                                else if (applyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt ||
                                         applyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt) {
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes,
                                        applyInfo.Length);
                                }

                                Array.Clear(m_CachedHashBytes, 0, CachedHashBytesLength);
                            }
                            else {
                                //普通”非压缩文件“的hashCode的计算方式
                                hashCode = Utility.Verifier.GetCrc32(fileStream);
                            }

                            if (hashCode != applyInfo.HashCode) {
                                if (ResourceApplyFailure != null) {
                                    string errorMessage = Utility.Text.Format(
                                        "Resource hash code error, need '{0}', applied '{1}'.", applyInfo.HashCode,
                                        hashCode);
                                    ResourceApplyFailure(applyInfo.ResourceName, m_ApplyingResourcePackPath,
                                        errorMessage);
                                }

                                m_FailureFlag = true;
                                return false;
                            }
                        }
                    }
                    //至此，从ResourcePack中读取指定Resource，匹配成功并保存在本地读写区中的过程已完成
                    //现在可以直接在读写区查找到该Resource数据了

                    if (applyInfo.UseFileSystem) {
                        IFileSystem fileSystem = m_ResourceManager.GetFileSystem(applyInfo.FileSystemName, false);
                        //写入”目标FileSystem“中
                        bool retVal = fileSystem.WriteFile(applyInfo.ResourceName.FullName, applyInfo.ResourcePath);
                        if (File.Exists(applyInfo.ResourcePath)) {
                            File.Delete(applyInfo.ResourcePath);
                        }

                        if (!retVal) {
                            if (ResourceApplyFailure != null) {
                                string errorMessage = Utility.Text.Format(
                                    "Unable to write resource '{0}' to file system '{1}'.", applyInfo.ResourcePath,
                                    applyInfo.FileSystemName);
                                ResourceApplyFailure(applyInfo.ResourceName, m_ApplyingResourcePackPath, errorMessage);
                            }

                            m_FailureFlag = true;
                            return false;
                        }
                    }
                    //至此，将读写区的Resource写入指定的fileSytem的操作执行完毕

                    //这个其实不应该放在这里执行
                    string downloadingResource = Utility.Text.Format("{0}.download", applyInfo.ResourcePath);
                    if (File.Exists(downloadingResource)) {
                        File.Delete(downloadingResource);
                    }

                    //从”m_UpdateCandidateInfo“集合中移除该Resource信息
                    m_UpdateCandidateInfo.Remove(applyInfo.ResourceName);
                    m_ResourceManager.m_ResourceInfos[applyInfo.ResourceName].MarkReady();
                    //由于部分资源存在”读写区“，部分资源存在”只读区“，因此这里把”位于读写区的Resource“记录下来
                    m_ResourceManager.m_ReadWriteResourceInfos.Add(applyInfo.ResourceName,
                        new ReadWriteResourceInfo(applyInfo.FileSystemName, applyInfo.LoadType, applyInfo.Length,
                            applyInfo.HashCode));
                    //从”资源包“中应用”已有资源“成功
                    if (ResourceApplySuccess != null) {
                        ResourceApplySuccess(applyInfo.ResourceName, applyInfo.ResourcePath, m_ApplyingResourcePackPath,
                            applyInfo.Length, applyInfo.CompressedLength);
                    }

                    //注意：这里始终使用的都是”压缩后的大小“
                    m_CurrentGenerateReadWriteVersionListLength += applyInfo.CompressedLength;
                    //”m_ApplyWaitingInfo“代表根据”ResourcePack“和”m_UpdateCandidateInfo“两者筛选出来可以直接应用的Resource集合
                    //当”m_ApplyWaitingInfo“集合中所有Resource都应用完毕后，则重写读写区版本文件
                    if (m_ApplyWaitingInfo.Count <= 0 || m_CurrentGenerateReadWriteVersionListLength >=
                        m_GenerateReadWriteVersionListLength) {
                        GenerateReadWriteVersionList();
                        return true;
                    }

                    return false;
                }
                catch (Exception exception) {
                    if (ResourceApplyFailure != null) {
                        ResourceApplyFailure(applyInfo.ResourceName, m_ApplyingResourcePackPath, exception.ToString());
                    }

                    m_FailureFlag = true;
                    return false;
                }
                finally {
                    //从ResourcePack读取玩某个Resource后则将其Postion归位，以便下次调用该方法应用其他Resource
                    //注意：在”try...catch..finally“中无论是否有返回值，都会在最终”return“前执行”finally“中的语句
                    m_ApplyingResourcePackStream.Position = position;
                }
            }
            #endregion

            #region 按照ResourceGroup依次下载目标Resource(有”string.Empty“的默认分组可直接下载m_UpdateCandidateInfo中所有Resource)
            /// <summary>
            /// 更新指定资源组的资源。
            /// PS：这命名不是应该为”UpdateResourceByResourceGroup“吗？
            /// </summary>
            /// <param name="resourceGroup">要更新的资源组。</param>
            public void UpdateResources(ResourceGroup resourceGroup) {
                if (m_DownloadManager == null) {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                if (!m_CheckResourcesComplete) {
                    throw new GameFrameworkException("You must check resources complete first.");
                }

                //当前正在应用资源包，
                if (m_ApplyingResourcePackStream != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource pack '{0}' being applied.",
                        m_ApplyingResourcePackPath));
                }

                if (m_UpdatingResourceGroup != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource group '{0}' being updated.",
                        m_UpdatingResourceGroup.Name));
                }

                //这里默认的“resourceGroup.Name”是“string.Empty”
                if (string.IsNullOrEmpty(resourceGroup.Name)) {
                    //1.以”string.Empty“为name的ResourceGroup下包含”所有满足变体需求的Resources“，
                    //  但是这里并没有直接使用该ResourceGroup下的”m_ResourceNames“变量，
                    //  而是使用”ResourceChecker.cs“在其执行过程中将所有”CheckStatus.Update“状态的Resource加入到”m_UpdateCandidateInfo“集合
                    // 所以这里直接使用”m_UpdateCandidateInfo“集合即可(该集合包含所有需要Update的Resource信息)
                    //2.使用”string.Empty“分组的原因：
                    //  在正式流程中为了方便一次性下载所有需要Update的Resource，因此创建”string.Empty“分组
                    //  而不用通过遍历”ResourceManager.m_ResourceGroups“集合分别下载每个ResourceGroup下需要”Update“的Resource
                    //  这样比较简便。这也是”string.Empty“分组的其中一个作用
                    foreach (KeyValuePair<ResourceName, UpdateInfo> updateInfo in m_UpdateCandidateInfo) {
                        m_UpdateWaitingInfo.Add(updateInfo.Value);
                    }
                }
                else {
                    //从每个“ResourceGroup”中筛选出“ResourceChecker”中处理过后加入到“m_UpdateCandidateInfo”中的Resource
                    resourceGroup.InternalGetResourceNames(m_CachedResourceNames);
                    foreach (ResourceName resourceName in m_CachedResourceNames) {
                        UpdateInfo updateInfo = null;
                        if (!m_UpdateCandidateInfo.TryGetValue(resourceName, out updateInfo)) {
                            continue;
                        }

                        m_UpdateWaitingInfo.Add(updateInfo);
                    }

                    m_CachedResourceNames.Clear();
                }

                m_UpdatingResourceGroup = resourceGroup;
                //代表当前”m_UpdatingResourceGroup“的下载情况是否有失败的
                //每次开启更新某个ResourceGroup时都需要将该参数重置为初始状态
                m_FailureFlag = false;

                //注意：1.在将“目标Resource”加入到“m_UpdateWaitingInfo”集合中后，“m_UpdateCandidateInfo”集合中依然保留该Resource的信息
                //      并没有移除，只有在该资源下载成功并验证hashcode，length等参数无误后才会从集合中删除
                //    2.加入”m_UpdateWaitingInfo“集合中后由”Update轮询“开始下载每个Resource
            }

            /// <summary>
            /// 更新指定资源(主要在LoadAsset/LoadScene时发现该Asset所属的Resource还没有"Ready"时使用，
            /// 因此这里会介入“m_UpdateWaitingInfoWhilePlaying”集合)
            /// 并且其会直接调用“DownloadResource”，而并不是等待“Update”中的执行
            /// PS：从后续逻辑可知，所有调用该方法的地方都是”目标资源尚未Ready“的情况(因为该资源允许在”Playing“的过程中加载)
            ///    如果是这样的逻辑，这个名字不是应该为”UpdateResourceWhilePlaying“吗！！！！！！！
            /// </summary>
            /// <param name="resourceName">要更新的资源名称。</param>
            public void UpdateResource(ResourceName resourceName) {
                if (m_DownloadManager == null) {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                if (!m_CheckResourcesComplete) {
                    throw new GameFrameworkException("You must check resources complete first.");
                }

                if (m_ApplyingResourcePackStream != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource pack '{0}' being applied.",
                        m_ApplyingResourcePackPath));
                }

                UpdateInfo updateInfo = null;
                //如果”m_UpdateWhilePlaying“集合中并未存在该”updateInfo“时才会触发”DownloadResource“
                //避免短时间内频繁触发下载同一Resource的情况
                //如果在”LoadScene/LoadAsset“前该资源已在”m_UpdateWhilePlaying“集合中，则无需处理，
                //等待该Resource加载完成即可
                //只有在该资源”DownloadFailure/DownloadSuccess“后才会从”m_UpdateWhilePlaying“集合中移除该元素
                if (m_UpdateCandidateInfo.TryGetValue(resourceName, out updateInfo) &&
                    m_UpdateWaitingInfoWhilePlaying.Add(updateInfo)) {
                    DownloadResource(updateInfo);
                }
            }
            #endregion

            #region DownloadResource: 根据updateInfo信息开始从服务器下载资源
            //下载Resource包含两种情况：1.根据ResourceGroup中缺少的Resource加入“m_UpdateWaitingInfo”后等待“Update”轮询
            // 2.调用“LoadAsset/LoadScene”时由于该资源所在的Resource尚未下载导致需要在“UpdatableWhilePlaying”时下载
            //   此时会将其加入“m_UpdateWhilePlaying”集合
            private bool DownloadResource(UpdateInfo updateInfo) {
                if (updateInfo.Downloading) {
                    return false;
                }

                //设置标志位，避免重复下载
                updateInfo.Downloading = true;
                //这里存在漏洞：不应该直接使用“DefaultExtension”，而应该使用“updateInfo.ResourceName.Extension”
                //这里只要出现“非*.dat后缀的AB文件”则会报错
                string resourceFullNameWithCrc32 = updateInfo.ResourceName.Variant != null
                    ? Utility.Text.Format("{0}.{1}.{2:x8}.{3}", updateInfo.ResourceName.Name,
                        updateInfo.ResourceName.Variant, updateInfo.HashCode, updateInfo.ResourceName.Extension)
                    : Utility.Text.Format("{0}.{1:x8}.{2}", updateInfo.ResourceName.Name,
                        updateInfo.HashCode, updateInfo.ResourceName.Extension);
                m_DownloadManager.AddDownload(updateInfo.ResourcePath,
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_UpdatePrefixUri,
                        resourceFullNameWithCrc32)),
                    updateInfo);
                return true;
            }

            private void OnDownloadStart(object sender, DownloadStartEventArgs e) {
                UpdateInfo updateInfo = e.UserData as UpdateInfo;
                if (updateInfo == null) {
                    return;
                }

                if (m_DownloadManager == null) {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                //通常不会出现这种情况，在下载开始时文件已经非常大了
                if (e.CurrentLength > int.MaxValue) {
                    throw new GameFrameworkException(Utility.Text.Format("File '{0}' is too large.",
                        e.DownloadPath));
                }

                //执行外部监听的委托“ResourceUpdateStart”(如果外部有监听的话)
                if (ResourceUpdateStart != null) {
                    ResourceUpdateStart(updateInfo.ResourceName, e.DownloadPath, e.DownloadUri, (int) e.CurrentLength,
                        updateInfo.CompressedLength, updateInfo.RetryCount);
                }
            }

            private void OnDownloadUpdate(object sender, DownloadUpdateEventArgs e) {
                UpdateInfo updateInfo = e.UserData as UpdateInfo;
                if (updateInfo == null) {
                    return;
                }

                if (m_DownloadManager == null) {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                //前提：所有放在服务器上的资源其实都是压缩过的，因此对应的必然是“updateInfo.CompressedLength”，
                //    而使用“DownloadManager”下载同一资源时必然“e.CurrentLength”只可能"<="updateInfo.CompressedLength
                //    所以如果出现“e.CurrentLength > updateInfo.CompressedLength”，则说明资源下载错了
                //    此时需要删除已经下载的资源，并执行“OnDownloadFailure”委托
                if (e.CurrentLength > updateInfo.CompressedLength) {
                    m_DownloadManager.RemoveDownload(e.SerialId);
                    //下载过程中的文件带“*.download”后缀，而下载完成后则直接使用”e.DownloadPath“，并没有”.download“后缀了
                    string downloadFile = Utility.Text.Format("{0}.download", e.DownloadPath);
                    if (File.Exists(downloadFile)) {
                        File.Delete(downloadFile);
                    }
                    string errorMessage =
                        Utility.Text.Format(
                            "When download update, downloaded length is larger than compressed length, need '{0}', downloaded '{1}'.",
                            updateInfo.CompressedLength, e.CurrentLength);
                    DownloadFailureEventArgs downloadFailureEventArgs =
                        DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage,
                            e.UserData);
                    OnDownloadFailure(this, downloadFailureEventArgs);
                    ReferencePool.Release(downloadFailureEventArgs);
                    return;
                }

                //如果没有出现以上异常，则直接执行“ResourceUpdateChanged”委托即可(如果外界有监听的话)
                if (ResourceUpdateChanged != null) {
                    ResourceUpdateChanged(updateInfo.ResourceName, e.DownloadPath, e.DownloadUri, (int) e.CurrentLength,
                        updateInfo.CompressedLength);
                }
            }

            private void OnDownloadSuccess(object sender, DownloadSuccessEventArgs e) {
                UpdateInfo updateInfo = e.UserData as UpdateInfo;
                if (updateInfo == null) {
                    return;
                }

                try {
                    //这里的“e.DownloadPath”是指该资源下载到本地的存储路径，而不是其“downloadUrl”
                    //PS：这里直接使用“using”语句所以后面都不需要专门对“fileStream”对象执行“close或dispose”方法
                    using (FileStream fileStream =
                           new FileStream(e.DownloadPath, FileMode.Open, FileAccess.ReadWrite)) {
                        //判定放在服务器上的该资源是否是“压缩后的资源”
                        bool compressed = updateInfo.Length != updateInfo.CompressedLength ||
                                          updateInfo.HashCode != updateInfo.CompressedHashCode;

                        //首先可以肯定的是：从服务器上下载的资源必定是跟“CompressedLength”比较，而之后判断该资源是否“压缩过”，则需要
                        //比较updateInfo的“Length”和“CompressedLength”
                        int length = (int) fileStream.Length;
                        if (length != updateInfo.CompressedLength) {
                            //说明文件下载错误
                            fileStream.Close();
                            string errorMessage = Utility.Text.Format(
                                "Resource compressed length error, need '{0}', downloaded '{1}'.",
                                updateInfo.CompressedLength, length);
                            //这一部分的通用逻辑是否可以提取出来，只把“errorMessage”预留给外部即可
                            DownloadFailureEventArgs downloadFailureEventArgs =
                                DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage,
                                    e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);

                            //扩展：在这种情况下其实还应该删除该AB文件
                            return;
                        }

                        //如果压缩过，则说明下载完成的文件还需要将其解压才行
                        if (compressed) {
                            fileStream.Position = 0L;  //该语句“fileStream.Position = 0L”可能与后续的“GetCrc32”计算hashcode时使用到
                            int hashCode = Utility.Verifier.GetCrc32(fileStream);
                            if (hashCode != updateInfo.CompressedHashCode) {
                                fileStream.Close();
                                string errorMessage = Utility.Text.Format(
                                    "Resource compressed hash code error, need '{0}', downloaded '{1}'.",
                                    updateInfo.CompressedHashCode, hashCode);
                                DownloadFailureEventArgs downloadFailureEventArgs =
                                    DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri,
                                        errorMessage, e.UserData);
                                OnDownloadFailure(this, downloadFailureEventArgs);
                                ReferencePool.Release(downloadFailureEventArgs);
                                return;
                            }

                            //开始解压
                            fileStream.Position = 0L;
                            m_ResourceManager.PrepareCachedStream();
                            if (!Utility.Compression.Decompress(fileStream, m_ResourceManager.m_CachedStream)) {
                                //解压失败
                                fileStream.Close();
                                string errorMessage = Utility.Text.Format("Unable to decompress resource '{0}'.",
                                    e.DownloadPath);
                                DownloadFailureEventArgs downloadFailureEventArgs =
                                    DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri,
                                        errorMessage, e.UserData);
                                OnDownloadFailure(this, downloadFailureEventArgs);
                                ReferencePool.Release(downloadFailureEventArgs);
                                return;
                            }

                            //解压成功，并将数据存储到“m_ResourceManager.m_CachedStream”中
                            int uncompressedLength = (int) m_ResourceManager.m_CachedStream.Length;
                            //注意：解压成功后的文件只需要比较length，并没有比较hashcode
                            if (uncompressedLength != updateInfo.Length) {
                                //如果解压缩后“字节数组”的长度不一致，说明文件错误
                                fileStream.Close();
                                string errorMessage = Utility.Text.Format(
                                    "Resource length error, need '{0}', downloaded '{1}'.", updateInfo.Length,
                                    uncompressedLength);
                                DownloadFailureEventArgs downloadFailureEventArgs =
                                    DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri,
                                        errorMessage, e.UserData);
                                OnDownloadFailure(this, downloadFailureEventArgs);
                                ReferencePool.Release(downloadFailureEventArgs);
                                return;
                            }

                            //由于真正真确的数据存储在“m_ResourceManager.m_CachedStream”中，因此这里将“fileStream”清空
                            //然后将“m_CachedStream”中的数据写入进“fileStream”中(该fileStream具有“Write”权限)
                            fileStream.Position = 0L;
                            fileStream.SetLength(0L);
                            fileStream.Write(m_ResourceManager.m_CachedStream.GetBuffer(), 0, uncompressedLength);
                        }
                        else {
                            //没有压缩的文件其”length“与”CompressedLength“相同，由于上述已经比较过，因此这里只需要比较hashcode即可
                            //如果从服务器上下载的该资源没有压缩
                            int hashCode = 0;
                            fileStream.Position = 0L;
                            //从上下逻辑来看：普通文件或者压缩文件的HashCode计算可以直接使用“该文件的fileSystem”即可得到
                            //但是如果其为“LoadFromMemory/LoadFromBinary”，则需要使用比较复杂的hashcode计算方式
                            if (updateInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt ||
                                updateInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt
                                || updateInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt ||
                                updateInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt) {
                                Utility.Converter.GetBytes(updateInfo.HashCode, m_CachedHashBytes);
                                if (updateInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt ||
                                    updateInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt) {
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes,
                                        Utility.Encryption.QuickEncryptLength);
                                }
                                else if (updateInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt ||
                                         updateInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt) {
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes, length);
                                }

                                Array.Clear(m_CachedHashBytes, 0, CachedHashBytesLength);
                            }
                            else {
                                //普通文件的hashCode直接使用该方式即可获取
                                hashCode = Utility.Verifier.GetCrc32(fileStream);
                            }

                            if (hashCode != updateInfo.HashCode) {
                                fileStream.Close();
                                string errorMessage = Utility.Text.Format(
                                    "Resource hash code error, need '{0}', downloaded '{1}'.", updateInfo.HashCode,
                                    hashCode);
                                DownloadFailureEventArgs downloadFailureEventArgs =
                                    DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri,
                                        errorMessage, e.UserData);
                                OnDownloadFailure(this, downloadFailureEventArgs);
                                ReferencePool.Release(downloadFailureEventArgs);
                                return;
                            }
                        }
                    }
                    //至此文件下载成功，并且文件解压也成功(未压缩的文件不需要解压)，解压后的“本地存储路径”与原有的“下载成功路径e.DownloadPath”相同

                    //如果该“AssetBundle”使用了“文件系统”，则需要将其包含进“目标文件系统”中，同时删除“下载完成后已经保存到本地的AssetBundle资源”
                    //如“Entities.dat”，“Materials.dat”等文件(对应ResourceEditor界面中设置的各个AssetBundle)
                    if (updateInfo.UseFileSystem) {
                        //“storageInReadOnly”指该fileSystem处于“StreamingAssetPath”中，本“热更新”模块中涉及到“StreamingAssetPath”
                        //的资源会删除“persistentDataPath”中的资源，而只保留“StreamingAssetPath”中的那份，并且不会加入“updateInfo”集合中
                        IFileSystem fileSystem = m_ResourceManager.GetFileSystem(updateInfo.FileSystemName, false);
                        bool retVal = fileSystem.WriteFile(updateInfo.ResourceName.FullName, updateInfo.ResourcePath);
                        //由于已经将该“AssetBundle”文件成功下载到本地，因此在写入“目标fileSystem”中后，需要将其删除
                        //但这里不是应该在”retVal“为true后才删除吗？
                        if (File.Exists(updateInfo.ResourcePath)) {
                            File.Delete(updateInfo.ResourcePath);
                        }

                        //写入”目标fileSystem“失败
                        if (!retVal) {
                            string errorMessage = Utility.Text.Format("Write resource to file system '{0}' error.",
                                fileSystem.FullPath);
                            DownloadFailureEventArgs downloadFailureEventArgs =
                                DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage,
                                    e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);
                            return;
                        }
                    }
                    //至此，该Resource完全处理完毕

                    //这里的逻辑存在问题：”m_UpdateCandidateInfo“集合中该Resource必然需要移除，
                    //但”m_UpdateWaitingInfo“和”m_UpdateWhilePlaying“则不一定，因为触发”DownloadResource“的情况有两种
                    //这里无法判定属于哪一种。
                    //所以在移除该Resource前，应该先检测下是否存在于”m_UpdateWaitingInfo“或”m_UpdateWhilePlaying“集合中
                    m_UpdateCandidateInfo.Remove(updateInfo.ResourceName);
                    m_UpdateWaitingInfo.Remove(updateInfo);
                    m_UpdateWaitingInfoWhilePlaying.Remove(updateInfo);
                    //下载完成后，放入集合中
                    //”m_ResourceManager.m_ResourceInfos“集合代表游戏中所有用到的Resource信息(如果限定了变体版本，则只有对应变体的Resource)
                    m_ResourceManager.m_ResourceInfos[updateInfo.ResourceName].MarkReady();
                    //”m_ReadWriteResourceInfos“代表”读写区“包含的Resource信息
                    m_ResourceManager.m_ReadWriteResourceInfos.Add(updateInfo.ResourceName,
                        new ReadWriteResourceInfo(updateInfo.FileSystemName, updateInfo.LoadType, updateInfo.Length,
                            updateInfo.HashCode));

                    if (ResourceUpdateSuccess != null) {
                        ResourceUpdateSuccess(updateInfo.ResourceName, e.DownloadPath, e.DownloadUri, updateInfo.Length,
                            updateInfo.CompressedLength);
                    }

                    //更新当前已下载完成的资源的大小(注意：这里使用的是”CompressedLength“)，以便触发”重写本地版本文件“的逻辑
                    m_CurrentGenerateReadWriteVersionListLength += updateInfo.CompressedLength;
                    //第二个触发条件其实用一句话限制了两种情况：
                    //按”ResourceGroup“更新本ResourceGroup下所有资源完毕时，以及”m_UpdateWhilePlaying“完毕时都会触发重写
                    if (m_UpdateCandidateInfo.Count <= 0
                        || m_UpdateWaitingInfo.Count + m_UpdateWaitingInfoWhilePlaying.Count <= 0
                        || m_CurrentGenerateReadWriteVersionListLength >= m_GenerateReadWriteVersionListLength) {
                        GenerateReadWriteVersionList();
                    }

                    //如果当前”正在更新的ResourceGroup“中所有”Resource“都已更新完毕(通过”m_UpdateWaitingInfo“集合来判定)
                    //则重置”m_UpdatingResourceGroup“为null，并触发”之前ResourceGroup“的”更新结束的委托“
                    if (m_UpdatingResourceGroup != null && m_UpdateWaitingInfo.Count <= 0) {
                        //虽然都是引用类型对象，但这里并没有改变引用对象指向的地址中的内容，所以这里不影响
                        ResourceGroup updatingResourceGroup = m_UpdatingResourceGroup;
                        m_UpdatingResourceGroup = null;
                        if (ResourceUpdateComplete != null) {
                            ResourceUpdateComplete(updatingResourceGroup, !m_FailureFlag);
                        }
                    }

                    //”m_UpdateCandidateInfo“集合代表的是所有”需要更新的Resource“
                    if (m_UpdateCandidateInfo.Count <= 0 && ResourceUpdateAllComplete != null) {
                        ResourceUpdateAllComplete();
                    }
                }
                catch (Exception exception) {
                    string errorMessage = Utility.Text.Format("Update resource '{0}' with error message '{1}'.",
                        e.DownloadPath, exception);
                    DownloadFailureEventArgs downloadFailureEventArgs =
                        DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage,
                            e.UserData);
                    OnDownloadFailure(this, downloadFailureEventArgs);
                    ReferencePool.Release(downloadFailureEventArgs);
                }
            }

            private void OnDownloadFailure(object sender, DownloadFailureEventArgs e) {
                UpdateInfo updateInfo = e.UserData as UpdateInfo;
                if (updateInfo == null) {
                    return;
                }

                //注意：这里删除的是“正式文件”，而不是“xx.download”后缀的文件
                //PS：1.为了支持“断点下载”的功能，下载失败时“xx.download”文件通常是不会直接删除的(除非能够明确“下载的数据是错误的”，此时才可以删除“xx.download”文件)
                //   但是“xx.download”本身是存在风险的(有的时候会因为“xx.download”文件导致一直下载失败，卡住游戏流程，此时直接删除“xx.download”文件可以解决问题)
                //   2.因为除了“Download模块”自身的逻辑会调用本方法外，其他的“OnDownloadStart/Update/Success”的内部逻辑也会调用本方法
                //   而在这些情况是可能已经有“正式下载完成的AB文件”的(如OnDownloadSuccess中该AB文件的Length/HashCode/CompressedLength/HashCode校验未通过时也会执行本方法)
                //   所以在这些情况下同样需要将“已经被验证为错误的AB正式文件删除掉”，所以“本句代码”是有存在的必要的
                if (File.Exists(e.DownloadPath)) {
                    File.Delete(e.DownloadPath);
                }

                if (ResourceUpdateFailure != null) {
                    ResourceUpdateFailure(updateInfo.ResourceName, e.DownloadUri, updateInfo.RetryCount,
                        m_UpdateRetryCount, e.ErrorMessage);
                }

                string temporaryDownloadFilePath = Utility.Text.Format("{0}.download", e.DownloadPath);
                //尚有“retryCount”则继续尝试下载
                if (updateInfo.RetryCount < m_UpdateRetryCount) {
                    updateInfo.Downloading = false;
                    updateInfo.RetryCount++;

                    //修复框架原有存在的问题：
                    //复现方式：在“热更新”过程中频繁点击“Play”按钮导致“下载过程”异常中断。由于“xx.download”通常并没有被删除(即使下载失败的时候)
                    //        导致后续没有再点击Play按钮时依然无法下载成功，一直卡在该流程。但只要删除该“xx.download”文件或清除缓存(在手机上)
                    //        马上即可正常下载，并进入游戏
                    //解决办法：由于“下载失败”的原因有很多种，并且即使“TimeOut”导致下载失败也会卡住流程。
                    //        如果将“TimeOut”导致的“下载失败”加入“需要删除xx.download”的情况其实不合适
                    //        因此直接在“Download模块”处理“下载失败时xx.download”文件的删除，其实不合适
                    //        所以，将“下载失败时删除xx.download”的逻辑放在“需要用到删除功能的地方”处理
                    //        而且并非每次“文件下载失败都需要删除xx.download临时文件”，所以放在“超出RetryCount次数“时才会”删除xx.download”文件
                    //PS：为了保证体验，在尝试了两次均失败后则删除“本地原有的xx.download文件”重新下载，如果这样尝试依然失败，则本次停止该Resource的下载
                    if (updateInfo.RetryCount >= 2) {
                        if (File.Exists(temporaryDownloadFilePath)) {
                            File.Delete(temporaryDownloadFilePath);
                        }
                    }

                    //下载资源有两种情况：1.从m_UpdateWaitingInfo集合中等待“Update轮询”来执行下载，只要该资源尚未下载成功，则必然仍在
                    // m_UpdateWaitingInfo集合中。所以针对这种情况无需处理，只用把该“updateInfo”的“RetryCount”和“Downloading”状态修改下即可
                    //2.从“LoadAsset/LoadScene”中发现该Resource未下载从而导致的“DownloadResource”。
                    //  此时并不会将该Resource加入“m_UpdateWaitingInfo”集合中
                    //  所以需要检测“m_UpdateWhilePlaying”集合来确定是否需要下载该资源
                    if (m_UpdateWaitingInfoWhilePlaying.Contains(updateInfo)) {
                        DownloadResource(updateInfo);
                    }
                }
                else {
                    //若该资源”retryCount“超出最大限制，则设置”m_FailureFlag“为true
                    m_FailureFlag = true;
                    updateInfo.Downloading = false;
                    updateInfo.RetryCount = 0;
                    //这里的逻辑存在问题：因为无法确定该UpdateInfo触发下载的情况属于哪种，所以至少应该检测下
                    //“m_UpdateWaitingInfo”，“m_UpdateWhilePlaying”集合中是否存在
                    //只有其存在时才能从集合中删除
                    m_UpdateWaitingInfo.Remove(updateInfo);
                    m_UpdateWaitingInfoWhilePlaying.Remove(updateInfo);

                    //如果超出RetryCount次数则删除原有下载的“xx.download”文件以方便后续再次下载(因为存在“xx.download文件卡住下载流程”的情况)
                    if (File.Exists(temporaryDownloadFilePath)) {
                        File.Delete(temporaryDownloadFilePath);
                    }
                }
            }
            #endregion

            #region 属性相关
            /// <summary>
            /// 获取或设置每更新多少字节的资源，重新生成一次本地版本资源列表。
            /// </summary>
            public int GenerateReadWriteVersionListLength {
                get { return m_GenerateReadWriteVersionListLength; }
                set { m_GenerateReadWriteVersionListLength = value; }
            }

            /// <summary>
            /// 获取正在应用的资源包路径。
            /// </summary>
            public string ApplyingResourcePackPath {
                get { return m_ApplyingResourcePackPath; }
            }

            /// <summary>
            /// 获取等待应用资源数量。
            /// </summary>
            public int ApplyWaitingCount {
                get { return m_ApplyWaitingInfo.Count; }
            }

            /// <summary>
            /// 获取或设置资源更新重试次数。
            /// PS：这里应该是每个资源在下载失败后都会重新尝试的次数限制，超过该次数则下载失败
            /// </summary>
            public int UpdateRetryCount {
                get { return m_UpdateRetryCount; }
                set { m_UpdateRetryCount = value; }
            }

            /// <summary>
            /// 获取正在更新的资源组。
            /// </summary>
            public IResourceGroup UpdatingResourceGroup {
                get { return m_UpdatingResourceGroup; }
            }

            /// <summary>
            /// 获取等待更新资源数量。
            /// PS：按照“ResourceGroup”更新Resource，此为本ResourceGroup下所有剩余需要更新的资源数量
            /// </summary>
            public int UpdateWaitingCount {
                get { return m_UpdateWaitingInfo.Count; }
            }

            /// <summary>
            /// 获取使用时下载的等待更新资源数量。
            /// </summary>
            public int UpdateWaitingWhilePlayingCount {
                get { return m_UpdateWaitingInfoWhilePlaying.Count; }
            }

            /// <summary>
            /// 获取候选更新资源数量。
            /// </summary>
            public int UpdateCandidateCount {
                get { return m_UpdateCandidateInfo.Count; }
            }
            #endregion

            #region 工具方法
            /// <summary>
            /// 设置下载管理器。
            /// </summary>
            /// <param name="downloadManager">下载管理器。</param>
            public void SetDownloadManager(IDownloadManager downloadManager) {
                if (downloadManager == null) {
                    throw new GameFrameworkException("Download manager is invalid.");
                }

                m_DownloadManager = downloadManager;
                m_DownloadManager.DownloadStart += OnDownloadStart;
                m_DownloadManager.DownloadUpdate += OnDownloadUpdate;
                m_DownloadManager.DownloadSuccess += OnDownloadSuccess;
                m_DownloadManager.DownloadFailure += OnDownloadFailure;
            }

            /// <summary>
            /// 停止更新资源。
            /// PS：对于”资源包中的资源应用“无法停止，只专用于”停止使用指定ResourceGroup更新资源”的方式
            /// </summary>
            public void StopUpdateResources() {
                if (m_DownloadManager == null) {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                if (!m_CheckResourcesComplete) {
                    throw new GameFrameworkException("You must check resources complete first.");
                }

                //如果当前有”资源包“正在”应用“，则无法停止
                if (m_ApplyingResourcePackStream != null) {
                    throw new GameFrameworkException(Utility.Text.Format(
                        "There is already a resource pack '{0}' being applied.", m_ApplyingResourcePackPath));
                }

                if (m_UpdatingResourceGroup == null) {
                    throw new GameFrameworkException("There is no resource group being updated.");
                }

                m_UpdateWaitingInfo.Clear();
                m_UpdatingResourceGroup = null;
            }


            //每次更新“指定字节数量”的资源时都会重新创建“本地版本文件GameFrameworkList.dat”的临时文件，即在该文件后添加“.tmp”后缀
            //增加“临时文件逻辑”的原因在于：如果在生成新的“本地版本文件”的过程中出现异常，则原来的“正确的本地版本文件”依然可以继续使用
            //不会因为该“生成逻辑”导致其他流程卡住 —— 相当于是一种保险措施
            //如果生成“新本地版本文件”的过程中一切执行正常，则最后更换“本地原有的旧版本文件”即可
            //
            //触发”重写版本文件“的情况：
            //1.所有需要下载的资源都已准备完成”m_UpdateCandidateInfo.Count <= 0“
            //2.资源包中所有需要应用的资源都已处理完成”m_ApplyWaitingInfo.Count < = 0“
            //3.按照”ResourceGroup“更新Resource时，该ResourceGroup所有待更新的Resource都已更新完毕”m_UpdateWaitingInfo.Count <= 0“
            //4.使用”LoadScene/LoadAsset“添加到”m_UpdateWhilePlaying“集合中的Resource更新完成
            //5.当下载的资源大小满足”m_GenerateReadWriteVersionListLength“限制时
            //PS：从该方法的调用还看出，ResourceUpdater和”ResourceChecker“一样依然作用于”读写区“
            private void GenerateReadWriteVersionList() {
                FileStream fileStream = null;
                try {
                    //！！！！注意：这里使用的是“FileMode.Create”，所以每次调用都会重新生成文件，不论本地当前是否已经有该文件 ！！！！！
                    fileStream = new FileStream(m_ReadWriteVersionListTempFileName, FileMode.Create, FileAccess.Write);
                    LocalVersionList.Resource[] resources = m_ResourceManager.m_ReadWriteResourceInfos.Count > 0
                        ? new LocalVersionList.Resource[m_ResourceManager.m_ReadWriteResourceInfos.Count]
                        : null;
                    if (resources != null) {
                        int index = 0;
                        foreach (KeyValuePair<ResourceName, ReadWriteResourceInfo> i in m_ResourceManager
                                     .m_ReadWriteResourceInfos) {
                            ResourceName resourceName = i.Key;
                            ReadWriteResourceInfo resourceInfo = i.Value;
                            resources[index] = new LocalVersionList.Resource(resourceName.Name, resourceName.Variant,
                                resourceName.Extension, (byte) resourceInfo.LoadType, resourceInfo.Length,
                                resourceInfo.HashCode);
                            if (resourceInfo.UseFileSystem) {
                                List<int> resourceIndexes = null;
                                if (!m_CachedFileSystemsForGenerateReadWriteVersionList.TryGetValue(
                                        resourceInfo.FileSystemName, out resourceIndexes)) {
                                    resourceIndexes = new List<int>();
                                    m_CachedFileSystemsForGenerateReadWriteVersionList.Add(resourceInfo.FileSystemName,
                                        resourceIndexes);
                                }

                                resourceIndexes.Add(index);
                            }

                            index++;
                        }
                    }

                    LocalVersionList.FileSystem[] fileSystems =
                        m_CachedFileSystemsForGenerateReadWriteVersionList.Count > 0
                            ? new LocalVersionList.FileSystem[m_CachedFileSystemsForGenerateReadWriteVersionList.Count]
                            : null;
                    if (fileSystems != null) {
                        int index = 0;
                        foreach (KeyValuePair<string, List<int>> i in
                                 m_CachedFileSystemsForGenerateReadWriteVersionList) {
                            fileSystems[index++] = new LocalVersionList.FileSystem(i.Key, i.Value.ToArray());
                            i.Value.Clear();
                        }
                    }

                    LocalVersionList versionList = new LocalVersionList(resources, fileSystems);
                    if (!m_ResourceManager.m_ReadWriteVersionListSerializer.Serialize(fileStream, versionList)) {
                        throw new GameFrameworkException("Serialize read-write version list failure.");
                    }

                    if (fileStream != null) {
                        fileStream.Dispose();
                        fileStream = null;
                    }
                }
                catch (Exception exception) {
                    if (fileStream != null) {
                        fileStream.Dispose();
                        fileStream = null;
                    }

                    //这里是异常处理时执行，会删除已经创建的“临时文件”
                    if (File.Exists(m_ReadWriteVersionListTempFileName)) {
                        File.Delete(m_ReadWriteVersionListTempFileName);
                    }

                    throw new GameFrameworkException(
                        Utility.Text.Format("Generate read-write version list exception '{0}'.", exception), exception);
                }

                if (File.Exists(m_ReadWriteVersionListFileName)) {
                    File.Delete(m_ReadWriteVersionListFileName);
                }

                File.Move(m_ReadWriteVersionListTempFileName, m_ReadWriteVersionListFileName);
                m_CurrentGenerateReadWriteVersionListLength = 0;
            }
            #endregion
        }
    }
}