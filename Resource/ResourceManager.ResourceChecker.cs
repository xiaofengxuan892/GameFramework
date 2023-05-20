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
        /// <summary>
        /// 资源检查器。
        /// </summary>
        private sealed partial class ResourceChecker
        {
            private readonly ResourceManager m_ResourceManager;
            private readonly Dictionary<ResourceName, CheckInfo> m_CheckInfos;
            private string m_CurrentVariant;
            private bool m_IgnoreOtherVariant;

            private bool m_UpdatableVersionListReady;
            private bool m_ReadOnlyVersionListReady;
            private bool m_ReadWriteVersionListReady;

            public GameFrameworkAction<ResourceName, string, LoadType, int, int, int, int> ResourceNeedUpdate;
            public GameFrameworkAction<int, int, int, long, long> ResourceCheckComplete;

            /// <summary>
            /// 初始化资源检查器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public ResourceChecker(ResourceManager resourceManager)
            {
                m_ResourceManager = resourceManager;
                m_CheckInfos = new Dictionary<ResourceName, CheckInfo>();
                m_CurrentVariant = null;
                m_IgnoreOtherVariant = false;
                m_UpdatableVersionListReady = false;
                m_ReadOnlyVersionListReady = false;
                m_ReadWriteVersionListReady = false;

                ResourceNeedUpdate = null;
                ResourceCheckComplete = null;
            }

            /// <summary>
            /// 关闭并清理资源检查器。
            /// </summary>
            public void Shutdown()
            {
                m_CheckInfos.Clear();
            }

            /// <summary>
            /// 检查资源。
            /// </summary>
            /// <param name="currentVariant">当前使用的变体。</param>
            /// <param name="ignoreOtherVariant">是否忽略处理其它变体的资源，若不忽略，将会移除其它变体的资源。</param>
            public void CheckResources(string currentVariant, bool ignoreOtherVariant)
            {
                if (m_ResourceManager.m_ResourceHelper == null)
                {
                    throw new GameFrameworkException("Resource helper is invalid.");
                }

                if (string.IsNullOrEmpty(m_ResourceManager.m_ReadOnlyPath))
                {
                    throw new GameFrameworkException("Read-only path is invalid.");
                }

                if (string.IsNullOrEmpty(m_ResourceManager.m_ReadWritePath))
                {
                    throw new GameFrameworkException("Read-write path is invalid.");
                }

                m_CurrentVariant = currentVariant;
                m_IgnoreOtherVariant = ignoreOtherVariant;
                //加载”读写区“的”服务器版本文件列表GameFrameworkVersion.dat“
                //PS：这个文件是一定会有的，不论是第一次进游戏还是之后再进游戏，这个文件一定会下载到”读写区“并且是”解压完成的“
                m_ResourceManager.m_ResourceHelper.LoadBytes(
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_ReadWritePath, RemoteVersionListFileName)),
                    new LoadBytesCallbacks(OnLoadUpdatableVersionListSuccess, OnLoadUpdatableVersionListFailure), null);
                //加载”只读区StreamingAssets“中的”本地版本列表GameFrameworkList.dat“文件
                m_ResourceManager.m_ResourceHelper.LoadBytes(
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_ReadOnlyPath, LocalVersionListFileName)),
                    new LoadBytesCallbacks(OnLoadReadOnlyVersionListSuccess, OnLoadReadOnlyVersionListFailure), null);
                //加载”读写区“的”本地版本文件GameFrameworkList.dat“
                //PS：第一次进游戏时，”读写区“是没有这个文件的
                m_ResourceManager.m_ResourceHelper.LoadBytes(
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_ReadWritePath, LocalVersionListFileName)),
                    new LoadBytesCallbacks(OnLoadReadWriteVersionListSuccess, OnLoadReadWriteVersionListFailure), null);

                //扩展：这三个文件的加载都是同时执行的，并不是说在第一个”LoadBytes“的回调执行完毕后再执行第二个”LoadBytes“
                //     每个”LoadBytes“内部实际会调用”协程的yield“来切换CPU控制权限，因此这也导致以上三个”LoadBytes“其实是一起执行的
            }

            #region 设置AB的“服务器目标RemoteVersionInfo”，同时设置“ResourceManager”的“m_AssetInfos”(设置完毕)，“m_ResourceInfos/m_ReadWriteResourceInfos”(只赋容量大小，但集合内没有元素)
            //并更新“m_ResourceGroups”(包含所有分组，每个分组内包含其下所有ResourceNames；除此之外，还有一个“name为string.Empty”的resourceGroup，其内包含所有目标变体的Resource)
            private void OnLoadUpdatableVersionListSuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                //该参数初始时为”false“，代表在本流程中”服务器的版本列表文件“已经”解析完成了“
                if (m_UpdatableVersionListReady)
                {
                    throw new GameFrameworkException("Updatable version list has been parsed.");
                }

                MemoryStream memoryStream = null;
                try
                {
                    memoryStream = new MemoryStream(bytes, false);
                    UpdatableVersionList versionList = m_ResourceManager.m_UpdatableVersionListSerializer.Deserialize(memoryStream);
                    if (!versionList.IsValid)
                    {
                        throw new GameFrameworkException("Deserialize updatable version list failure.");
                    }

                    UpdatableVersionList.Asset[] assets = versionList.GetAssets();
                    UpdatableVersionList.Resource[] resources = versionList.GetResources();
                    UpdatableVersionList.FileSystem[] fileSystems = versionList.GetFileSystems();
                    UpdatableVersionList.ResourceGroup[] resourceGroups = versionList.GetResourceGroups();
                    //从服务器的“版本列表文件GameFrameworkVersion.dat”中获取到“目标版本信息”
                    m_ResourceManager.m_ApplicableGameVersion = versionList.ApplicableGameVersion;
                    m_ResourceManager.m_InternalResourceVersion = versionList.InternalResourceVersion;
                    m_ResourceManager.m_AssetInfos = new Dictionary<string, AssetInfo>(assets.Length, StringComparer.Ordinal);
                    m_ResourceManager.m_ResourceInfos = new Dictionary<ResourceName, ResourceInfo>(resources.Length, new ResourceNameComparer());
                    m_ResourceManager.m_ReadWriteResourceInfos = new SortedDictionary<ResourceName, ReadWriteResourceInfo>(new ResourceNameComparer());

                    //******* 重要：所有Resource的Index都是在该分组中通过字母排序后得到 *************
                    //注意：这里是创建一个默认分组(其实该分组的name不要用“String.Empty”，而应该用“All”或“Default”才合适)
                    ResourceGroup defaultResourceGroup = m_ResourceManager.GetOrAddResourceGroup(string.Empty);

                    //一个“fileSytem”中可能包含多个AssetBundle，其与“ResourceGroup”中的“AssetBundle”，两者对于AB的分类不同
                    //前者是处于“AB的文件存储”的角度，后者则处于“AB的资源性质的角度”
                    //PS：虽然从某种程度上讲，其实可以将“fileSytem”和“ResourceGroup”合并，现在这样其实“ResourceGroup”的作用有些减弱
                    foreach (UpdatableVersionList.FileSystem fileSystem in fileSystems)
                    {
                        //这些“Index”其实是已经排序过的(但是我感觉应该把“index”应该也加入到“Resource”类型的必备参数中)
                        int[] resourceIndexes = fileSystem.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            UpdatableVersionList.Resource resource = resources[resourceIndex];
                            //所有“非目标变体版本”的资源都不会加入“待使用的集合”中
                            if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                            {
                                continue;
                            }

                            //这里的方法命名以及执行结构有点混乱：
                            //主要逻辑是为该”ResourceName“对应的”CheckInfo“设置其”fileSystemName“
                            //PS：其实有点像“ResourceVerify”中针对“LocalVersionList”的处理有点类似，这里是转换成“Resource”的角度
                            //    来设置其对应的FileSytem。之前获取到的都是某个“fileSytem”中包含哪些“Resource”
                            SetCachedFileSystemName(new ResourceName(resource.Name, resource.Variant, resource.Extension), fileSystem.Name);
                        }
                    }

                    //虽然在匹配”服务器上的版本文件列表“时只提取”目标变体版本“的”Resource“信息，
                    //但是在匹配”本地读写区和本地只读区的版本文件列表“时，却把所有”Resource“的信息都包含进”m_CheckInfos“集合中了，
                    //因此后续对于”m_CheckInfos“集合中”非目标变体版本的Resource“时，增加了“ignoreOtherVariant”参数
                    foreach (UpdatableVersionList.Resource resource in resources)
                    {
                        //由于会有一个“defaultResourceGroup”，因此这里只包含“目标变体”的Resource，非目标变体的则不需要
                        if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                        {
                            continue;
                        }

                        ResourceName resourceName = new ResourceName(resource.Name, resource.Variant, resource.Extension);
                        int[] assetIndexes = resource.GetAssetIndexes();
                        foreach (int assetIndex in assetIndexes)
                        {
                            //这个“asset”的命名有点扯淡，原本就只是一个局部变量，完全不用起这么易混淆的名字，
                            //很容易与“包含所有对象信息的集合Assets”搞混淆
                            UpdatableVersionList.Asset asset = assets[assetIndex];
                            int[] dependencyAssetIndexes = asset.GetDependencyAssetIndexes();
                            int index = 0;
                            string[] dependencyAssetNames = new string[dependencyAssetIndexes.Length];
                            foreach (int dependencyAssetIndex in dependencyAssetIndexes)
                            {
                                dependencyAssetNames[index++] = assets[dependencyAssetIndex].Name;
                            }

                            //为“ResourceManager”中的“m_AssetInfos”集合赋值
                            m_ResourceManager.m_AssetInfos.Add(asset.Name, new AssetInfo(asset.Name,
                                resourceName, dependencyAssetNames));
                        }

                        //本质逻辑是设置该“Resource”所对应的“CheckInfo”中的“RemoteVersionInfo”参数
                        //PS：在设置“RemoteVesionInfo”时其实不应该“区分目标变体”，
                        //   只要“服务器版本文件”中包含的“Resource”就应该为其设置“RemoteVersionInfo”，否则会导致“切换SD/HD”后重新下载目标变体的AB文件
                        //   但“defaultResourceGroup”是需要区分目标变体的，因为后续“ProcedureUpdateResource”时会下载其中包含的所有Resources
                        SetVersionInfo(resourceName, (LoadType)resource.LoadType, resource.Length,
                            resource.HashCode, resource.CompressedLength, resource.CompressedHashCode);
                        //将该“AssetBundle”的信息加入“默认的ResourceGroup”中(该分组包含所有的Resources)
                        defaultResourceGroup.AddResource(resourceName, resource.Length, resource.CompressedLength);
                    }

                    //这些“resourceGroups”中可能包含有”非目标变体版本“的”AssetBundle“，而”ResoueceManager“中的”m_ResoueceGroup“
                    //会把所有的”AssetBundle“都放进去
                    foreach (UpdatableVersionList.ResourceGroup resourceGroup in resourceGroups)
                    {
                        //为“ResourceManager”中的“m_ResourceGroups”集合赋值
                        ResourceGroup group = m_ResourceManager.GetOrAddResourceGroup(resourceGroup.Name);
                        int[] resourceIndexes = resourceGroup.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            UpdatableVersionList.Resource resource = resources[resourceIndex];
                            //“非目标变体版本”的AssetBundle不处理
                            if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                            {
                                continue;
                            }

                            //更新该group内包含的“所有resources”
                            group.AddResource(new ResourceName(resource.Name, resource.Variant, resource.Extension),
                                resource.Length, resource.CompressedLength);
                        }
                    }

                    //代表将“服务器上的版本列表文件”解析成功，将其拆分出了“m_AssetInfos”, "m_Resources"等所有的信息
                    m_UpdatableVersionListReady = true;
                    RefreshCheckInfoStatus();
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Parse updatable version list exception '{0}'.", exception), exception);
                }
                finally
                {
                    if (memoryStream != null)
                    {
                        memoryStream.Dispose();
                        memoryStream = null;
                    }
                }
            }

            private void OnLoadUpdatableVersionListFailure(string fileUri, string errorMessage, object userData)
            {
                //由于在“热更新模式”下，任何情况都应该有“服务器上的GameFrameworkVersion.dat”文件，因此这里即使加载失败，
                //也没有将“m_UpdatableVersionListReady”置为“true”的语句
                throw new GameFrameworkException(Utility.Text.Format("Updatable version list '{0}' is invalid, error message is '{1}'.", fileUri, string.IsNullOrEmpty(errorMessage) ? "<Empty>" : errorMessage));
            }
            #endregion

            #region 设置AB的“只读区LocalVersionInfo”
            private void OnLoadReadOnlyVersionListSuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                if (m_ReadOnlyVersionListReady)
                {
                    throw new GameFrameworkException("Read-only version list has been parsed.");
                }

                MemoryStream memoryStream = null;
                try
                {
                    memoryStream = new MemoryStream(bytes, false);
                    LocalVersionList versionList = m_ResourceManager.m_ReadOnlyVersionListSerializer.Deserialize(memoryStream);
                    if (!versionList.IsValid)
                    {
                        throw new GameFrameworkException("Deserialize read-only version list failure.");
                    }

                    LocalVersionList.Resource[] resources = versionList.GetResources();
                    LocalVersionList.FileSystem[] fileSystems = versionList.GetFileSystems();

                    foreach (LocalVersionList.FileSystem fileSystem in fileSystems)
                    {
                        //这里的“index”编号是对应“resources”数组中的编号
                        int[] resourceIndexes = fileSystem.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            LocalVersionList.Resource resource = resources[resourceIndex];
                            //”ResourceVerify“中确保的是”读写区的LocalVersionList“的正确性，但是对于”只读区“却并没有校验，
                            //这里如果设置的是同一个”CheckInfo“的”fileSytemName“该如何？
                            SetCachedFileSystemName(new ResourceName(resource.Name, resource.Variant, resource.Extension),
                                fileSystem.Name);
                        }
                    }

                    foreach (LocalVersionList.Resource resource in resources)
                    {
                        //如果“只读区”中有”GameFrameworkList.dat“文件，那么就设置该”ResourceName“对应的”CheckInfo“的”m_ReadOnlyInfo“
                        SetReadOnlyInfo(new ResourceName(resource.Name, resource.Variant, resource.Extension),
                            (LoadType)resource.LoadType, resource.Length, resource.HashCode);
                    }

                    //加载”只读区“的”版本列表文件“结束
                    m_ReadOnlyVersionListReady = true;
                    RefreshCheckInfoStatus();
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Parse read-only version list exception '{0}'.", exception), exception);
                }
                finally
                {
                    if (memoryStream != null)
                    {
                        memoryStream.Dispose();
                        memoryStream = null;
                    }
                }
            }

            private void OnLoadReadOnlyVersionListFailure(string fileUri, string errorMessage, object userData)
            {
                if (m_ReadOnlyVersionListReady)
                {
                    throw new GameFrameworkException("Read-only version list has been parsed.");
                }

                //由于在“热更新模式”下，只读区不一定会具有“本地版本列表”文件，因此即使“加载该文件失败”，
                //这里也需要将“m_ReadOnlyVersionListReady”置为true
                m_ReadOnlyVersionListReady = true;
                RefreshCheckInfoStatus();
            }
            #endregion

            #region 设置AB的“读写区LocalVersionInfo”
            private void OnLoadReadWriteVersionListSuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                if (m_ReadWriteVersionListReady)
                {
                    throw new GameFrameworkException("Read-write version list has been parsed.");
                }

                MemoryStream memoryStream = null;
                try
                {
                    memoryStream = new MemoryStream(bytes, false);
                    LocalVersionList versionList = m_ResourceManager.m_ReadWriteVersionListSerializer.Deserialize(memoryStream);
                    if (!versionList.IsValid)
                    {
                        throw new GameFrameworkException("Deserialize read-write version list failure.");
                    }

                    LocalVersionList.Resource[] resources = versionList.GetResources();
                    LocalVersionList.FileSystem[] fileSystems = versionList.GetFileSystems();

                    foreach (LocalVersionList.FileSystem fileSystem in fileSystems)
                    {
                        int[] resourceIndexes = fileSystem.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            LocalVersionList.Resource resource = resources[resourceIndex];
                            //虽然本质上只有在一个文件读取完后才会执行另一个文件的“读取回调”，所以相同的CheckInfo共用一个“m_CachedFileSystemName”变量也可以
                            //但其实还是有点不安全
                            //所以这里是否可以用一个“局部变量”来存储“ResourceName”与“fileSystemName”之间的关系
                            //(通过“引用池系统”来管理该对象的“Acquire”和“Unspawn”，这样三个文件在读取时都可以使用该变量，也挺好)
                            //如果是这样的话，那“CheckInfo”中的“m_CachedFileSystemName”就没必要了
                            //注意：每个RemoteVersionInfo, LocalVersionInfo中都包含“m_FileSystemName”参数，所以这里的“m_CachedFileSystemName”只是“局部变量”而已
                            SetCachedFileSystemName(new ResourceName(resource.Name, resource.Variant, resource.Extension),
                                fileSystem.Name);
                        }
                    }

                    foreach (LocalVersionList.Resource resource in resources)
                    {
                        SetReadWriteInfo(new ResourceName(resource.Name, resource.Variant, resource.Extension), (LoadType)resource.LoadType, resource.Length, resource.HashCode);
                    }

                    m_ReadWriteVersionListReady = true;
                    RefreshCheckInfoStatus();
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Parse read-write version list exception '{0}'.", exception), exception);
                }
                finally
                {
                    if (memoryStream != null)
                    {
                        memoryStream.Dispose();
                        memoryStream = null;
                    }
                }
            }

            private void OnLoadReadWriteVersionListFailure(string fileUri, string errorMessage, object userData)
            {
                if (m_ReadWriteVersionListReady)
                {
                    throw new GameFrameworkException("Read-write version list has been parsed.");
                }

                //部分情况下(如”热更新模式“下第一次进入游戏时，”读写区“此时并没有”GameFrameworkList.dat“文件)
                m_ReadWriteVersionListReady = true;
                RefreshCheckInfoStatus();
            }
            #endregion

            private void RefreshCheckInfoStatus()
            {
                //以上三个版本文件，任一个没有读取完都不会执行本方法。如此即解决了“读取三个版本文件”的先后问题
                //PS: 因为本质上三个文件读取后的逻辑没有任何相通或影响的，所以完全不用在意先后顺序，只要三个文件都读取完了即可
                if (!m_UpdatableVersionListReady || !m_ReadOnlyVersionListReady || !m_ReadWriteVersionListReady)
                {
                    return;
                }

                int movedCount = 0;
                int removedCount = 0;
                int updateCount = 0;
                long updateTotalLength = 0L;
                long updateTotalCompressedLength = 0L;
                //”服务器版本列表中目标变体版本的Resource“和”本地读写区和只读区所有Resource“的”CheckInfo“的集合 —— m_CheckInfos
                foreach (KeyValuePair<ResourceName, CheckInfo> checkInfo in m_CheckInfos)
                {
                    CheckInfo ci = checkInfo.Value;
                    ci.RefreshStatus(m_CurrentVariant, m_IgnoreOtherVariant);
                    //只有该“Resource”已经存在于“只读区”并且“length/hashCode”等所有数据都“正确无误”时才能得到该“CheckStatus”
                    if (ci.Status == CheckInfo.CheckStatus.StorageInReadOnly)
                    {
                        //将“只读区正确无误”的资源放入“m_ResourceInfos”中
                        m_ResourceManager.m_ResourceInfos.Add(ci.ResourceName,
                            new ResourceInfo(ci.ResourceName, ci.FileSystemName, ci.LoadType, ci.Length, ci.HashCode,
                                ci.CompressedLength, true, true));
                    }
                    else if (ci.Status == CheckInfo.CheckStatus.StorageInReadWrite)
                    {
                        if (ci.NeedMoveToDisk || ci.NeedMoveToFileSystem)
                        {
                            movedCount++;
                            string resourceFullName = ci.ResourceName.FullName; //包含“变体版本”和“扩展名”的全名
                            string resourcePath = Utility.Path.GetRegularPath(
                                Path.Combine(m_ResourceManager.m_ReadWritePath, resourceFullName));
                            if (ci.NeedMoveToDisk)
                            {
                                //说明该“Resource”现在放在“上一个FileSystem”中，所以需要先从“之前的FileSystem”中释放出来
                                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(ci.ReadWriteFileSystemName, false);
                                //并存放在“resourcePath”路径下
                                if (!fileSystem.SaveAsFile(resourceFullName, resourcePath))
                                {
                                    throw new GameFrameworkException(Utility.Text.Format("Save as file '{0}' to '{1}' from file system '{2}' error.", resourceFullName, resourcePath, fileSystem.FullPath));
                                }

                                //从“原来的fileSystem”中删除该“resource”
                                fileSystem.DeleteFile(resourceFullName);
                            }

                            //如果该“resource”有新的“fileSystem”，则需要“写入”新的“fileSystem”中
                            if (ci.NeedMoveToFileSystem)
                            {
                                IFileSystem fileSystem = m_ResourceManager.GetFileSystem(ci.FileSystemName, false);
                                //写入新的“fileSystem”中
                                if (!fileSystem.WriteFile(resourceFullName, resourcePath))
                                {
                                    throw new GameFrameworkException(Utility.Text.Format("Write resource '{0}' to file system '{1}' error.", resourceFullName, fileSystem.FullPath));
                                }

                                //在写入“目标fileSystem”后需要删除“本地Disk”中该文件
                                if (File.Exists(resourcePath))
                                {
                                    File.Delete(resourcePath);
                                }
                            }
                        }

                        //将处理完毕后的“Resource”放入“m_ResourceInfos”集合中
                        m_ResourceManager.m_ResourceInfos.Add(ci.ResourceName,
                            new ResourceInfo(ci.ResourceName, ci.FileSystemName, ci.LoadType, ci.Length,
                                ci.HashCode, ci.CompressedLength, false, true));
                        //“m_ReadWriteResourceInfos”集合中存储“本地读写区”中的资源
                        m_ResourceManager.m_ReadWriteResourceInfos.Add(ci.ResourceName,
                            new ReadWriteResourceInfo(ci.FileSystemName, ci.LoadType, ci.Length, ci.HashCode));
                    }
                    else if (ci.Status == CheckInfo.CheckStatus.Update)
                    {
                        //将需要更新的Resource信息也放入“m_ResourceInfos”集合中
                        //(在“ResourceInfo”类型的参数中通过“m_Ready”来表示该资源是否已“正确无误”)
                        m_ResourceManager.m_ResourceInfos.Add(ci.ResourceName,
                            new ResourceInfo(ci.ResourceName, ci.FileSystemName, ci.LoadType, ci.Length,
                                ci.HashCode, ci.CompressedLength, false, false));
                        updateCount++;
                        updateTotalLength += ci.Length;
                        updateTotalCompressedLength += ci.CompressedLength;
                        if (ResourceNeedUpdate != null)
                        {
                            ResourceNeedUpdate(ci.ResourceName, ci.FileSystemName, ci.LoadType, ci.Length,
                                ci.HashCode, ci.CompressedLength, ci.CompressedHashCode);
                        }
                    }
                    else if (ci.Status == CheckInfo.CheckStatus.Unavailable || ci.Status == CheckInfo.CheckStatus.Disuse)
                    {
                        // Do nothing.
                    }
                    else
                    {
                        throw new GameFrameworkException(Utility.Text.Format("Check resources '{0}' error with unknown status.", ci.ResourceName.FullName));
                    }

                    //“NeedRemove”参数代表的是从“读写区”中删除该资源
                    //PS：由于“Resource”在“读写区”存储时有可能被放入“配置的FileSystem”中，所以需要分成两种情况分别删除该Resource
                    if (ci.NeedRemove)
                    {
                        removedCount++;
                        if (ci.ReadWriteUseFileSystem)
                        {
                            //从配置的“fileSystem”中删除该Resource
                            IFileSystem fileSystem = m_ResourceManager.GetFileSystem(ci.ReadWriteFileSystemName,
                                false);
                            fileSystem.DeleteFile(ci.ResourceName.FullName);
                        }
                        else
                        {
                            //从本地的常规路径中删除该Resource
                            string resourcePath = Utility.Path.GetRegularPath(
                                Path.Combine(m_ResourceManager.m_ReadWritePath, ci.ResourceName.FullName));
                            if (File.Exists(resourcePath))
                            {
                                File.Delete(resourcePath);
                            }
                        }
                    }
                }

                //“movedCount”代表该“Resource”本身的数据是正确的，但需要改变其所属的FileSystem：
                //            可能是从fileSystem中释放出来，也可能是从常规路径中被写入”目标fileSystem“中
                //”removedCount“代表从”读写区“中移除该Resource：可能是从fileSystem中，也可能是从常规路径中直接移除
                if (movedCount > 0 || removedCount > 0)
                {
                    //清除其中Resource数量为空的”fileSystem“：这里的清除”fileSystem“会将该fileSystem所在的”物理存储文件“也一并删除
                    RemoveEmptyFileSystems();
                    //删除空文件夹(如果该目录本身为空，那么该目录自身也会被删除)
                    Utility.Path.RemoveEmptyDirectory(m_ResourceManager.m_ReadWritePath);
                }

                if (ResourceCheckComplete != null)
                {
                    //”updateCount“包含的是”需要更新的资源数量“
                    //PS：感觉前面的”movedCount“和”removedCount“其实并不需要，可以省略
                    ResourceCheckComplete(movedCount, removedCount, updateCount, updateTotalLength, updateTotalCompressedLength);
                }
            }

            #region 工具方法
            //若该文件系统内没有文件则删除本fileSystem(使用“FileSystem模块”中的方法删除“目标fileSystem”),
            //同时更新“ResourceManager.m_ReadWriteFileSystem”参数(该参数代表本地读写区包含的所有“fileSystemNames”的集合 —— 只需要记录name即可，不需要包含存储实际的fileSystem对象)
            private void RemoveEmptyFileSystems()
            {
                List<string> removedFileSystemNames = null;
                foreach (KeyValuePair<string, IFileSystem> fileSystem in m_ResourceManager.m_ReadWriteFileSystems)
                {
                    if (fileSystem.Value.FileCount <= 0)
                    {
                        //这一句判断，直接在声明”removedFileSystem“时将其初始化不是更好吗？
                        if (removedFileSystemNames == null)
                        {
                            removedFileSystemNames = new List<string>();
                        }

                        //从“文件系统管理器”的集合中移除该fileSystem对象，并删除其”物理存在的文件“
                        m_ResourceManager.m_FileSystemManager.DestroyFileSystem(fileSystem.Value, true);
                        removedFileSystemNames.Add(fileSystem.Key);
                    }
                }

                //由于是字典集合，因此不方便在”遍历的同时改变集合中的元素“。所以这里先将需要删除的对象都保存起来，之后删除即可
                if (removedFileSystemNames != null)
                {
                    //这里仅仅只是移除“字典集合m_ReadWriteFileSystems”中的元素而已，并不是“删除物理存在的文件”，
                    //该文件早在“FileSystem模块”中就已经删除了
                    foreach (string removedFileSystemName in removedFileSystemNames)
                    {
                        m_ResourceManager.m_ReadWriteFileSystems.Remove(removedFileSystemName);
                    }
                }
            }

            //这些方法名称也是够随意，明明是处理“CheckInfo”的信息，而方法名中却不体现
            private void SetCachedFileSystemName(ResourceName resourceName, string fileSystemName)
            {
                GetOrAddCheckInfo(resourceName).SetCachedFileSystemName(fileSystemName);
            }

            private void SetVersionInfo(ResourceName resourceName, LoadType loadType, int length, int hashCode, int compressedLength, int compressedHashCode)
            {
                GetOrAddCheckInfo(resourceName).SetVersionInfo(loadType, length, hashCode, compressedLength, compressedHashCode);
            }

            private void SetReadOnlyInfo(ResourceName resourceName, LoadType loadType, int length, int hashCode)
            {
                GetOrAddCheckInfo(resourceName).SetReadOnlyInfo(loadType, length, hashCode);
            }

            private void SetReadWriteInfo(ResourceName resourceName, LoadType loadType, int length, int hashCode)
            {
                GetOrAddCheckInfo(resourceName).SetReadWriteInfo(loadType, length, hashCode);
            }

            private CheckInfo GetOrAddCheckInfo(ResourceName resourceName)
            {
                CheckInfo checkInfo = null;
                if (m_CheckInfos.TryGetValue(resourceName, out checkInfo))
                {
                    return checkInfo;
                }

                checkInfo = new CheckInfo(resourceName);
                m_CheckInfos.Add(checkInfo.ResourceName, checkInfo);

                return checkInfo;
            }
            #endregion
        }
    }
}
