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
        private sealed partial class ResourceChecker
        {
            /// <summary>
            /// 资源检查信息。
            /// PS：实话来讲，这个作者实在是拆分的太细了，细到有些多余的程度了，这么简单的一个功能居然拆分成了4个脚本
            ///     该“CheckInfo”的作用在于：专用于“ResourceCheck”中用到的，代表每一个AssetBundle的检查信息
            ///     包含当前处于的检查状态CheckStatus(初始创建时都为“CheckStatus.Unknown”)
            ///     以及该资源在服务器上的“目标版本RemoteVersion”和本地已经保存过的“LocalVersion”
            /// </summary>
            private sealed partial class CheckInfo
            {
                private readonly ResourceName m_ResourceName;
                private CheckStatus m_Status;
                private bool m_NeedRemove;
                private bool m_NeedMoveToDisk;
                private bool m_NeedMoveToFileSystem;
                //这三个傻逼的命名是不是都应该在“名字”里相应的加上“Remote、Local、Local”
                //这个框架的作者在参数和方法命名上是真的有点随意啊！！！！
                private RemoteVersionInfo m_VersionInfo;
                private LocalVersionInfo m_ReadOnlyInfo;
                private LocalVersionInfo m_ReadWriteInfo;
                private string m_CachedFileSystemName;

                /// <summary>
                /// 初始化资源检查信息的新实例。
                /// </summary>
                /// <param name="resourceName">资源名称。</param>
                public CheckInfo(ResourceName resourceName)
                {
                    m_ResourceName = resourceName;
                    m_Status = CheckStatus.Unknown;
                    m_NeedRemove = false;
                    m_NeedMoveToDisk = false;
                    m_NeedMoveToFileSystem = false;
                    m_VersionInfo = default(RemoteVersionInfo);
                    m_ReadOnlyInfo = default(LocalVersionInfo);
                    m_ReadWriteInfo = default(LocalVersionInfo);
                    m_CachedFileSystemName = null;
                }

                /// <summary>
                /// 刷新资源信息状态。
                /// </summary>
                /// <param name="currentVariant">当前变体。</param>
                /// <param name="ignoreOtherVariant">是否忽略处理其它变体的资源，若不忽略则移除。</param>
                public void RefreshStatus(string currentVariant, bool ignoreOtherVariant)
                {
                    //若该AssetBundle的远程RemoteVersionInfo不存在，则需要删除其在本地读写区的“AssetBundle”信息
                    if (!m_VersionInfo.Exist)
                    {
                        m_Status = CheckStatus.Disuse;
                        //检测“本地读写区”中是否有该资源，如果有则在“本地读写区”中删除该资源
                        m_NeedRemove = m_ReadWriteInfo.Exist;
                        return;
                    }

                    if (m_ResourceName.Variant == null || m_ResourceName.Variant == currentVariant)
                    {
                        //如果“只读区StreamingAssets”中保存该AssetBundle，则需要从“本地读写区”中删除该资源
                        //PS：并不需要在”读写区PerisistentDataPath“和”StreamingAssetsPath“中同时保存同一AssetBundle
                        //    也可以理解为”只读区“中保存的该AssetBundle资源已经包含在”最终包“中所以不需要在”读写区“中再下载一份
                        //扩展：本条件的作用在于：排除”streamingAssetPath“中已包含的并且当前仍然可用的AssetBundle
                        if (m_ReadOnlyInfo.Exist
                            && m_ReadOnlyInfo.FileSystemName == m_VersionInfo.FileSystemName
                            && m_ReadOnlyInfo.LoadType == m_VersionInfo.LoadType
                            && m_ReadOnlyInfo.Length == m_VersionInfo.Length
                            && m_ReadOnlyInfo.HashCode == m_VersionInfo.HashCode)
                        {
                            //由于“只读区”的内容不能“改变”，因此以上的条件中会包含“FileSystemName”一致的条件
                            m_Status = CheckStatus.StorageInReadOnly;
                            m_NeedRemove = m_ReadWriteInfo.Exist;
                        }
                        //若本地读写区的AssetBundle的内容与”服务器上要求的信息“一致
                        else if (m_ReadWriteInfo.Exist
                                 && m_ReadWriteInfo.LoadType == m_VersionInfo.LoadType
                                 && m_ReadWriteInfo.Length == m_VersionInfo.Length
                                 && m_ReadWriteInfo.HashCode == m_VersionInfo.HashCode)
                        {
                            //由于三个文件的加载回调中都会设置某一”ResourceName“的”CheckInfo“的”fileSytemName“，
                            //因此必然存在同一”CheckInfo“的”fileSystem“被反复写入的情况
                            bool differentFileSystem = m_ReadWriteInfo.FileSystemName != m_VersionInfo.FileSystemName;
                            m_Status = CheckStatus.StorageInReadWrite;
                            //注意：由于本框架中使用了“FileSystem”，因此每个Resource在下载完毕后会根据其“m_FileSystem”设定
                            //使用“FileSytem模块”中的"Write"方法将其写入“目标FileSystem”中
                            //因此在“磁盘的Explorer.exe”中其实是无法直接看到该“Resource”的，
                            //所以如果“本地版本文件中的FileSystem与服务器上的目标fileSystem”不同时，则需要将“本地已经整合进FileSystem”
                            //中的“Resource”释放出来，重新放入“Explorer.exe”，即这里的“Disk”中
                            //这样可以直接在“Windows资源管理器”中看到该“Resource”
                            //以便再次将其“Write”进“服务器标注的正确的FileSyste”中
                            m_NeedMoveToDisk = m_ReadWriteInfo.UseFileSystem && differentFileSystem;
                            //这里始终以”服务器上的RemoteVersionInfo“为准，如果”本地的VersionInfo“与”服务器不一致“，
                            //则需要将该”AssetBundle”移动到“RemoteVersionInfo”上标识的“fileSystem”中
                            m_NeedMoveToFileSystem = m_VersionInfo.UseFileSystem && differentFileSystem;

                            /* 因为有的Resource并没有配置FileSystem，所以在设置“m_NeedMoveToDisk/FileSystem”时
                               需要检测其“UseFileSystem”参数 */
                        }
                        else
                        {
                            //当”只读区“和”读写区“都不存在正确的资源信息但”服务器要求的m_VersionInfo“存在时，则说明需要更新了
                            m_Status = CheckStatus.Update;
                            //由于”只读区StreamingAssetPath“中的资源不具备”写入权限“，因此无法删除”只读区“中的资源
                            //因此当”只读区“和”读写区“的资源都不正确时，则只能删除”读写区“的资源
                            m_NeedRemove = m_ReadWriteInfo.Exist;
                        }
                    }
                    else
                    {
                        //当该资源的变体版本与目标版本不一致时，则标记该资源”当前不可用“
                        m_Status = CheckStatus.Unavailable;
                        //如果不忽略其他变体版本的资源，则只要该”变体版本“与”目标变体版本“不一致，则将其删除
                        //PS：由于”只读区StreamingAssetPath“不具有”写入“权限，因此只能删除”读写区“的资源
                        m_NeedRemove = !ignoreOtherVariant && m_ReadWriteInfo.Exist;
                    }
                }

                #region 设置该AB“服务器要求的版本”，“本地读写区已经存在的版本”，“本地只读区已经存在的版本”
                /// <summary>
                /// 临时缓存资源所在的文件系统名称。
                /// </summary>
                /// <param name="fileSystemName">资源所在的文件系统名称。</param>
                public void SetCachedFileSystemName(string fileSystemName)
                {
                    m_CachedFileSystemName = fileSystemName;
                }

                /// <summary>
                /// 设置资源在版本中的信息。
                /// PS：这傻逼的方法命名不应该是“SetRemoteVersionInfo”吗？！！！！
                /// </summary>
                /// <param name="loadType">资源加载方式。</param>
                /// <param name="length">资源大小。</param>
                /// <param name="hashCode">资源哈希值。</param>
                /// <param name="compressedLength">压缩后大小。</param>
                /// <param name="compressedHashCode">压缩后哈希值。</param>
                public void SetVersionInfo(LoadType loadType, int length, int hashCode, int compressedLength, int compressedHashCode)
                {
                    if (m_VersionInfo.Exist)
                    {
                        throw new GameFrameworkException(Utility.Text.Format("You must set version info of '{0}' only once.", m_ResourceName.FullName));
                    }

                    m_VersionInfo = new RemoteVersionInfo(m_CachedFileSystemName, loadType, length, hashCode, compressedLength, compressedHashCode);
                    m_CachedFileSystemName = null;
                }

                /// <summary>
                /// 设置资源在只读区中的信息。
                /// </summary>
                /// <param name="loadType">资源加载方式。</param>
                /// <param name="length">资源大小。</param>
                /// <param name="hashCode">资源哈希值。</param>
                public void SetReadOnlyInfo(LoadType loadType, int length, int hashCode)
                {
                    if (m_ReadOnlyInfo.Exist)
                    {
                        throw new GameFrameworkException(Utility.Text.Format("You must set read-only info of '{0}' only once.", m_ResourceName.FullName));
                    }

                    m_ReadOnlyInfo = new LocalVersionInfo(m_CachedFileSystemName, loadType, length, hashCode);
                    m_CachedFileSystemName = null;
                }

                /// <summary>
                /// 设置资源在读写区中的信息。
                /// </summary>
                /// <param name="loadType">资源加载方式。</param>
                /// <param name="length">资源大小。</param>
                /// <param name="hashCode">资源哈希值。</param>
                public void SetReadWriteInfo(LoadType loadType, int length, int hashCode)
                {
                    if (m_ReadWriteInfo.Exist)
                    {
                        throw new GameFrameworkException(Utility.Text.Format("You must set read-write info of '{0}' only once.", m_ResourceName.FullName));
                    }

                    m_ReadWriteInfo = new LocalVersionInfo(m_CachedFileSystemName, loadType, length, hashCode);
                    m_CachedFileSystemName = null;
                }
                #endregion

                #region 属性
                /// <summary>
                /// 获取资源名称。
                /// </summary>
                public ResourceName ResourceName
                {
                    get
                    {
                        return m_ResourceName;
                    }
                }

                /// <summary>
                /// 获取资源检查状态。
                /// </summary>
                public CheckStatus Status
                {
                    get
                    {
                        return m_Status;
                    }
                }

                /// <summary>
                /// 获取是否需要移除读写区的资源。
                /// PS：这傻逼的命名不应该是“m_NeedRemoveInReadWriteArea”吗？？！！！！
                /// </summary>
                public bool NeedRemove
                {
                    get
                    {
                        return m_NeedRemove;
                    }
                }

                /// <summary>
                /// 获取是否需要将读写区的资源移动到磁盘。
                /// PS：有可能该AB配置的“FileSystem”发生改变，因此需要将其从原来的“fileSystem”数据中转移出来，重新放到“肉眼可见的磁盘”中
                ///     如果该AB又配置了新的“fileSystem”，则需要将其再次写入“新的fileSystem”中
                /// </summary>
                public bool NeedMoveToDisk
                {
                    get
                    {
                        return m_NeedMoveToDisk;
                    }
                }

                /// <summary>
                /// 获取是否需要将读写区的资源移动到文件系统。
                /// </summary>
                public bool NeedMoveToFileSystem
                {
                    get
                    {
                        return m_NeedMoveToFileSystem;
                    }
                }

                /// <summary>
                /// 获取资源所在的文件系统名称。
                /// </summary>
                public string FileSystemName
                {
                    get
                    {
                        return m_VersionInfo.FileSystemName;
                    }
                }

                /// <summary>
                /// 获取资源是否使用文件系统。
                /// PS：已下载到本地读写区的资源是否使用文件系统
                ///     这些信息远程的RemoteVersionInfo不是有嘛
                /// </summary>
                public bool ReadWriteUseFileSystem
                {
                    get
                    {
                        return m_ReadWriteInfo.UseFileSystem;
                    }
                }

                /// <summary>
                /// 获取读写资源所在的文件系统名称。
                /// </summary>
                public string ReadWriteFileSystemName
                {
                    get
                    {
                        return m_ReadWriteInfo.FileSystemName;
                    }
                }

                /// <summary>
                /// 获取资源加载方式。
                /// PS：资源的加载方式，RemoteVersionInfo也有啊，这是为什么又要搞一次？？？
                /// </summary>
                public LoadType LoadType
                {
                    get
                    {
                        return m_VersionInfo.LoadType;
                    }
                }

                /// <summary>
                /// 获取资源大小。
                /// </summary>
                public int Length
                {
                    get
                    {
                        return m_VersionInfo.Length;
                    }
                }

                /// <summary>
                /// 获取资源哈希值。
                /// </summary>
                public int HashCode
                {
                    get
                    {
                        return m_VersionInfo.HashCode;
                    }
                }

                /// <summary>
                /// 获取压缩后大小。
                /// </summary>
                public int CompressedLength
                {
                    get
                    {
                        return m_VersionInfo.CompressedLength;
                    }
                }

                /// <summary>
                /// 获取压缩后哈希值。
                /// </summary>
                public int CompressedHashCode
                {
                    get
                    {
                        return m_VersionInfo.CompressedHashCode;
                    }
                }
                #endregion
            }
        }
    }
}
