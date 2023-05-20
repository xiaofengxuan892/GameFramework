//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Download;
using System;
using System.IO;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 版本资源列表处理器。
        /// PS：“检测是否是最新的internalResourceVersion”时是在“读写区”内检查？？为何不在“只读区”？？
        /// </summary>
        private sealed class VersionListProcessor
        {
            private readonly ResourceManager m_ResourceManager;
            private IDownloadManager m_DownloadManager;
            private int m_VersionListLength;
            private int m_VersionListHashCode;
            private int m_VersionListCompressedLength;
            private int m_VersionListCompressedHashCode;

            public GameFrameworkAction<string, string> VersionListUpdateSuccess;
            public GameFrameworkAction<string, string> VersionListUpdateFailure;

            /// <summary>
            /// 初始化版本资源列表处理器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public VersionListProcessor(ResourceManager resourceManager)
            {
                m_ResourceManager = resourceManager;
                m_DownloadManager = null;
                m_VersionListLength = 0;
                m_VersionListHashCode = 0;
                m_VersionListCompressedLength = 0;
                m_VersionListCompressedHashCode = 0;

                VersionListUpdateSuccess = null;
                VersionListUpdateFailure = null;
            }

            /// <summary>
            /// 关闭并清理版本资源列表处理器。
            /// </summary>
            public void Shutdown()
            {
                if (m_DownloadManager != null)
                {
                    m_DownloadManager.DownloadSuccess -= OnDownloadSuccess;
                    m_DownloadManager.DownloadFailure -= OnDownloadFailure;
                }
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

                m_DownloadManager = downloadManager;
                //添加“下载监听”
                m_DownloadManager.DownloadSuccess += OnDownloadSuccess;
                m_DownloadManager.DownloadFailure += OnDownloadFailure;
            }

            /// <summary>
            /// 检查版本资源列表。
            /// </summary>
            /// <param name="latestInternalResourceVersion">最新的内部资源版本号。</param>
            /// <returns>检查版本资源列表结果。</returns>
            public CheckVersionListResult CheckVersionList(int latestInternalResourceVersion)
            {
                //“读写区路径”为“persistentDataPath”或“temporaryCachedPath”
                if (string.IsNullOrEmpty(m_ResourceManager.m_ReadWritePath))
                {
                    throw new GameFrameworkException("Read-write path is invalid.");
                }

                //”热更新模式“会将最新的”Resource资源“都下载到”读写区PersistentDataPath“
                string versionListFileName = Utility.Path.GetRegularPath(Path.Combine(m_ResourceManager.m_ReadWritePath, RemoteVersionListFileName));
                if (!File.Exists(versionListFileName))
                {
                    return CheckVersionListResult.NeedUpdate;
                }

                //读取“本地读写区PersistentDataPath”中的“GameFramworkVersion.dat”文件，获取其中的“InternalResourceVersion”数值
                int internalResourceVersion = 0;
                FileStream fileStream = null;
                try
                {
                    fileStream = new FileStream(versionListFileName, FileMode.Open, FileAccess.Read);
                    object internalResourceVersionObject = null;
                    //由于“m_UpdatableVersionListSerializer”的“TryGetValueCallback”仅仅只是为指定的“key == internalResourceVersion”的专属方法
                    //所以在实际查找时并没有在“stream”中遍历比较“key”，而是直接根据“序列化数据时的顺序”来
                    //直接读取“指定Position”的value作为“目标InternalResourceVersion的值”
                    if (!m_ResourceManager.m_UpdatableVersionListSerializer.TryGetValue(fileStream, "InternalResourceVersion", out internalResourceVersionObject))
                    {
                        return CheckVersionListResult.NeedUpdate;
                    }

                    internalResourceVersion = (int)internalResourceVersionObject;
                }
                catch
                {
                    return CheckVersionListResult.NeedUpdate;
                }
                finally
                {
                    //释放该“fileStream”
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                        fileStream = null;
                    }
                }

                //如果读写区内“GameFrameworkVersion.dat”文件中存储的“InternalResourceVersion”与目标版本不同，
                //则代表需要下载最新的资源文件
                //注意：这里只要不等即需要下载，并且只要下载就必然是下载服务器上的文件
                //这里包含“本地版本号 > 目标版本号”的情况。此时会默认下载服务器上的资源，所以需要保证服务器上的资源为“目标版本号”的资源即可
                //如此可以实现“资源版本”的回退
                if (internalResourceVersion != latestInternalResourceVersion)
                {
                    return CheckVersionListResult.NeedUpdate;
                }

                //说明该“VersionList”已然是最新的，可以直接使用了
                return CheckVersionListResult.Updated;
            }

            /// <summary>
            /// 更新版本资源列表。
            /// </summary>
            /// <param name="versionListLength">版本资源列表大小。</param>
            /// <param name="versionListHashCode">版本资源列表哈希值。</param>
            /// <param name="versionListCompressedLength">版本资源列表压缩后大小。</param>
            /// <param name="versionListCompressedHashCode">版本资源列表压缩后哈希值。</param>
            public void UpdateVersionList(int versionListLength, int versionListHashCode, int versionListCompressedLength, int versionListCompressedHashCode)
            {
                if (m_DownloadManager == null)
                {
                    throw new GameFrameworkException("You must set download manager first.");
                }

                m_VersionListLength = versionListLength;
                m_VersionListHashCode = versionListHashCode;
                m_VersionListCompressedLength = versionListCompressedLength;
                m_VersionListCompressedHashCode = versionListCompressedHashCode;
                //这里的命名好奇怪：从逻辑上看是把远程服务器上的“GameFrameworkVersion.dat”文件下载到本地读写区存储起来，并且文件名不变
                //那为什么这里的变量命名是“localVersionList”？？？？？
                string localVersionListFilePath = Utility.Path.GetRegularPath(Path.Combine(m_ResourceManager.m_ReadWritePath, RemoteVersionListFileName));
                int dotPosition = RemoteVersionListFileName.LastIndexOf('.');
                //“x”代表“十六进制”， “8”代表“占位符共8个”，即转换成十六进制后共占位8个，不满足的用“0”补齐
                string latestVersionListFullNameWithCrc32 = Utility.Text.Format("{0}.{2:x8}.{1}",
                    RemoteVersionListFileName.Substring(0, dotPosition),
                    RemoteVersionListFileName.Substring(dotPosition + 1),
                    m_VersionListHashCode);
                //”localVersionListFilePath“是将”RemotePath“的文件下载下来之后存储到本地的地址
                m_DownloadManager.AddDownload(localVersionListFilePath,
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_UpdatePrefixUri, latestVersionListFullNameWithCrc32)),
                    this);
                //注意：这里传递的“userData”变量在“OnDownloadSuccess”中会原样传递过来，用于校对
                //由于该方法为public，并且是非static修饰，所以若要调用该方法则必然需要有“VersionListProcessor”类型的实例对象
                //此时“this”则指代的是该“实例对象”本身

                //扩展：为什么这里不用”UnityWebRequest.Get“获取远程服务器的版本文件数据，而要用”Download模块“要专门下载该文件呢？
                //解答：因为这个文件是需要存到本地的，不只是要”获取其中的数据“。该文件后续还有其他地方要使用到，
                //    所以使用”UnityWebRequest.Get“获取数据是无法满足需求的
            }

            private void OnDownloadSuccess(object sender, DownloadSuccessEventArgs e)
            {
                //原样传递过来的参数，以用于校对
                VersionListProcessor versionListProcessor = e.UserData as VersionListProcessor;
                if (versionListProcessor == null || versionListProcessor != this)
                {
                    return;
                }

                try
                {
                    //获取下载完成后存储到本地”读写区“的”GameFrameworkVersion.dat“文件
                    using (FileStream fileStream = new FileStream(e.DownloadPath, FileMode.Open, FileAccess.ReadWrite))
                    {
                        int length = (int)fileStream.Length; //文件的byte[]的大小
                        if (length != m_VersionListCompressedLength)  //该文件得到的是”压缩后的大小“
                        {
                            //虽然文件下载成功，但下载的文件并不是”目标文件“或者”文件下载中途错误，并不完整“
                            fileStream.Close();
                            string errorMessage = Utility.Text.Format("Latest version list compressed length error, need '{0}', downloaded '{1}'.",
                                m_VersionListCompressedLength, length);
                            DownloadFailureEventArgs downloadFailureEventArgs = DownloadFailureEventArgs.Create(e.SerialId,
                                e.DownloadPath, e.DownloadUri, errorMessage, e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);
                            return;
                        }

                        fileStream.Position = 0L;
                        int hashCode = Utility.Verifier.GetCrc32(fileStream);
                        //注意：从”存储到本地读写区的GameFrameworkVersion.dat“文件读取到的”fileStream“均为”压缩后的文件“
                        if (hashCode != m_VersionListCompressedHashCode)
                        {
                            fileStream.Close();
                            string errorMessage = Utility.Text.Format("Latest version list compressed hash code error, need '{0}', downloaded '{1}'.", m_VersionListCompressedHashCode, hashCode);
                            DownloadFailureEventArgs downloadFailureEventArgs = DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage, e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);
                            return;
                        }

                        fileStream.Position = 0L;
                        //在”m_ResouceManager“中创建”可用的CachedStream“用于存储”解压后fileStream“
                        //其实可以直接在此方法中创建新的”MemoryStream“用于存储”解压后的fileStream“，
                        //但这里使用”m_ResourceManager“中”全局变量cachedStream“也是可以省却一部分内存消耗的
                        m_ResourceManager.PrepareCachedStream();
                        if (!Utility.Compression.Decompress(fileStream, m_ResourceManager.m_CachedStream))
                        {
                            //解压失败
                            fileStream.Close();
                            string errorMessage = Utility.Text.Format("Unable to decompress latest version list '{0}'.", e.DownloadPath);
                            DownloadFailureEventArgs downloadFailureEventArgs = DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage, e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);
                            return;
                        }

                        //解压成功后，则会存储在”m_ResourceManager.m_CachedStream“中
                        int uncompressedLength = (int)m_ResourceManager.m_CachedStream.Length;
                        //这里难道不应该再检测一下”m_CachedStream“的”hashcode“吗？还是说对于”公有变量“计算出来的”hashcode“必然不同
                        if (uncompressedLength != m_VersionListLength)
                        {
                            fileStream.Close();
                            string errorMessage = Utility.Text.Format("Latest version list length error, need '{0}', downloaded '{1}'.", m_VersionListLength, uncompressedLength);
                            DownloadFailureEventArgs downloadFailureEventArgs = DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage, e.UserData);
                            OnDownloadFailure(this, downloadFailureEventArgs);
                            ReferencePool.Release(downloadFailureEventArgs);
                            return;
                        }

                        //文件正确，此时”复用读取压缩文件时产生的fileStream“：直接设置”stream.Position“以及”stream.Length“即可重置该stream
                        fileStream.Position = 0L;
                        fileStream.SetLength(0L);
                        //将”cacheStream“中”从0到uncompressedLength“的字节数据全部写入”被重置后的fileStream“中
                        fileStream.Write(m_ResourceManager.m_CachedStream.GetBuffer(), 0, uncompressedLength);
                        //由于该fileStream具有”FileAccess.ReadWrite“，因此直接写入新的byte数组后会保存在原有的fileStream中，
                        //同时该fileStream的”本地读写区保存路径“依然没有改变
                    }

                    //通知外部：”版本列表“更新成功
                    //PS：这里的回调逻辑搞得有点麻烦了，其实可以直接在“UpdateVersionList”方法中把“m_UpdateVersionListCallbacks”传递过来
                    //   现在的逻辑中间还要通过外部监控的“VersionListUpdateSuccess”来执行，有点多次一举
                    if (VersionListUpdateSuccess != null)
                    {
                        VersionListUpdateSuccess(e.DownloadPath, e.DownloadUri);
                    }
                }
                catch (Exception exception)
                {
                    string errorMessage = Utility.Text.Format("Update latest version list '{0}' with error message '{1}'.", e.DownloadPath, exception);
                    DownloadFailureEventArgs downloadFailureEventArgs = DownloadFailureEventArgs.Create(e.SerialId, e.DownloadPath, e.DownloadUri, errorMessage, e.UserData);
                    OnDownloadFailure(this, downloadFailureEventArgs);
                    ReferencePool.Release(downloadFailureEventArgs);
                }
            }

            private void OnDownloadFailure(object sender, DownloadFailureEventArgs e)
            {
                VersionListProcessor versionListProcessor = e.UserData as VersionListProcessor;
                if (versionListProcessor == null || versionListProcessor != this)
                {
                    return;
                }

                //由于下载失败，因此删除本地已经存储的文件
                if (File.Exists(e.DownloadPath))
                {
                    File.Delete(e.DownloadPath);
                }

                //通知外部“更新版本列表失败”
                if (VersionListUpdateFailure != null)
                {
                    VersionListUpdateFailure(e.DownloadUri, e.ErrorMessage);
                }
            }
        }
    }
}
