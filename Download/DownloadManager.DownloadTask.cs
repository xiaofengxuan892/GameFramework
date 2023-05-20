//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Download
{
    internal sealed partial class DownloadManager : GameFrameworkModule, IDownloadManager
    {
        /// <summary>
        /// 下载任务。
        /// </summary>
        private sealed class DownloadTask : TaskBase
        {
            //”s_Serial“是专门用来为”每个任务池中的所有任务对象“计数用的，因此其为”static“修饰，
            //而”TaskBase中的m_SerialId“则是每个TaskBase对象持有的，代表该TaskBase对象的编号
            //PS：从这样的实际作用来看，该参数似乎不应该放在这里，而应该放在”每个继承TaskPool的扩展子类“中
            private static int s_Serial = 0;

            private DownloadTaskStatus m_Status;
            private string m_DownloadPath;  //下载之后存储到本地的路径
            private string m_DownloadUri;
            private int m_FlushSize;
            private float m_Timeout;

            /// <summary>
            /// 初始化下载任务的新实例。
            /// </summary>
            public DownloadTask()
            {
                m_Status = DownloadTaskStatus.Todo;
                m_DownloadPath = null;
                m_DownloadUri = null;
                m_FlushSize = 0;
                m_Timeout = 0f;
            }

            /// <summary>
            /// 创建下载任务。
            /// </summary>
            /// <param name="downloadPath">下载后存放路径。</param>
            /// <param name="downloadUri">原始下载地址。</param>
            /// <param name="tag">下载任务的标签。</param>
            /// <param name="priority">下载任务的优先级。</param>
            /// <param name="flushSize">将缓冲区写入磁盘的临界大小。</param>
            /// <param name="timeout">下载超时时长，以秒为单位。</param>
            /// <param name="userData">用户自定义数据。</param>
            /// <returns>创建的下载任务。</returns>
            public static DownloadTask Create(string downloadPath, string downloadUri, string tag, int priority, int flushSize, float timeout, object userData)
            {
                DownloadTask downloadTask = ReferencePool.Acquire<DownloadTask>();
                downloadTask.Initialize(++s_Serial, tag, priority, userData);
                downloadTask.m_DownloadPath = downloadPath;
                downloadTask.m_DownloadUri = downloadUri;
                downloadTask.m_FlushSize = flushSize;
                downloadTask.m_Timeout = timeout;
                return downloadTask;
            }

            /// <summary>
            /// 清理下载任务。
            /// </summary>
            public override void Clear()
            {
                base.Clear();
                m_Status = DownloadTaskStatus.Todo;
                m_DownloadPath = null;
                m_DownloadUri = null;
                m_FlushSize = 0;
                m_Timeout = 0f;
            }

            #region 属性
            /// <summary>
            /// 获取或设置下载任务的状态。
            /// </summary>
            public DownloadTaskStatus Status
            {
                get
                {
                    return m_Status;
                }
                set
                {
                    m_Status = value;
                }
            }

            /// <summary>
            /// 获取下载后存放路径。
            /// </summary>
            public string DownloadPath
            {
                get
                {
                    return m_DownloadPath;
                }
            }

            /// <summary>
            /// 获取原始下载地址。
            /// </summary>
            public string DownloadUri
            {
                get
                {
                    return m_DownloadUri;
                }
            }

            /// <summary>
            /// 获取将缓冲区写入磁盘的临界大小。
            /// </summary>
            public int FlushSize
            {
                get
                {
                    return m_FlushSize;
                }
            }

            /// <summary>
            /// 获取下载超时时长，以秒为单位。
            /// </summary>
            public float Timeout
            {
                get
                {
                    return m_Timeout;
                }
            }

            /// <summary>
            /// 获取下载任务的描述。
            /// </summary>
            public override string Description
            {
                get
                {
                    return m_DownloadPath;
                }
            }

            #endregion
        }
    }
}
