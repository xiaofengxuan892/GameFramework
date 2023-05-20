//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Download
{
    /// <summary>
    /// 下载成功事件。
    /// </summary>
    public sealed class DownloadSuccessEventArgs : GameFrameworkEventArgs
    {
        /// <summary>
        /// 初始化下载成功事件的新实例。
        /// </summary>
        public DownloadSuccessEventArgs()
        {
            SerialId = 0;
            DownloadPath = null;
            DownloadUri = null;
            CurrentLength = 0L;
            //任何需要“Download”的地方都可以将某些“自定义数据”在使用“DownloadManager.AddDownload”方法时传递过来，
            //然后当“下载完成“后，通过该事件”DownloadSuccessEventArgs“中的”userdata“传递过来
            //并以此来鉴别是否正确。
            //这也是”userdata“中的其中一种作用
            UserData = null;
        }

        /// <summary>
        /// 获取下载任务的序列编号。
        /// </summary>
        public int SerialId
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取下载后存放路径。
        /// </summary>
        public string DownloadPath
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取下载地址。
        /// </summary>
        public string DownloadUri
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取当前大小。
        /// </summary>
        public long CurrentLength
        {
            get;
            private set;
        }

        /// <summary>
        /// 获取用户自定义数据。
        /// </summary>
        public object UserData
        {
            get;
            private set;
        }

        /// <summary>
        /// 创建下载成功事件。
        /// </summary>
        /// <param name="serialId">下载任务的序列编号。</param>
        /// <param name="downloadPath">下载后存放路径。</param>
        /// <param name="downloadUri">下载地址。</param>
        /// <param name="currentLength">当前大小。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>创建的下载成功事件。</returns>
        public static DownloadSuccessEventArgs Create(int serialId, string downloadPath, string downloadUri, long currentLength, object userData)
        {
            DownloadSuccessEventArgs downloadSuccessEventArgs = ReferencePool.Acquire<DownloadSuccessEventArgs>();
            downloadSuccessEventArgs.SerialId = serialId;
            downloadSuccessEventArgs.DownloadPath = downloadPath;
            downloadSuccessEventArgs.DownloadUri = downloadUri;
            downloadSuccessEventArgs.CurrentLength = currentLength;
            downloadSuccessEventArgs.UserData = userData;
            return downloadSuccessEventArgs;
        }

        /// <summary>
        /// 清理下载成功事件。
        /// </summary>
        public override void Clear()
        {
            SerialId = 0;
            DownloadPath = null;
            DownloadUri = null;
            CurrentLength = 0L;
            UserData = null;
        }
    }
}
