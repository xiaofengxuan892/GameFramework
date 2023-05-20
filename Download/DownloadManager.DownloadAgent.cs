//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.IO;

namespace GameFramework.Download
{
    internal sealed partial class DownloadManager : GameFrameworkModule, IDownloadManager
    {
        /// <summary>
        /// 下载代理。
        /// </summary>
        private sealed class DownloadAgent : ITaskAgent<DownloadTask>, IDisposable
        {
            private readonly IDownloadAgentHelper m_Helper;  //负责实际的下载逻辑
            private DownloadTask m_Task;  //本TaskAgent当前负责的”TaskBase对象“
            private FileStream m_FileStream; //写入本地文件的数据流

            private long m_StartLength;  //本次下载开始时，该文件“当前已经下载完成存在本地的数据大小”(因为支持“断点下载”，所以大概是上一次下载完成的部分数据)
            private long m_DownloadedLength;  //“本次一共下载的总字节大小”(该数值在”下载过程中 —— 累计更新“，和”下载完成后 —— 直接赋值“都会刷新该数值)

            //距离上次下载进度更新后持续的时间(若在限定时间内进度一直没有更新，则代表”本次下载超时“)
            //注意：在”OnDownloadAgentHelperUpdateBytes/OnDownloadAgentHelperUpdateLength“或”OnDownloadAgentHelperComplete“委托中均需要重置此数值
            //    并且该数值只有在”当前为DownloadTaskStatus.Doing状态“时才会”累计数值“
            //PS：理论上在”Start“方法和”OnDownloadAgentHelperError回调“中均需要重置该数值
            private float m_WaitTime;

            private int m_WaitFlushSize;   //在存储到指定字节大小后，则将这些缓存的数据使用”fileStream.Flush“存入本地文件介质中
            private long m_SavedLength;  //目标下载文件”当前已经存储到本地文件的数据大小“

            private bool m_Disposed;  //代表本类型对象当前是否销毁过，以避免GC在回收该对象时重复执行其析构函数

            //通知各个”调用Download模块功能“的业务逻辑时用到的委托
            public GameFrameworkAction<DownloadAgent> DownloadAgentStart;
            public GameFrameworkAction<DownloadAgent, int> DownloadAgentUpdate;
            public GameFrameworkAction<DownloadAgent, long> DownloadAgentSuccess;
            public GameFrameworkAction<DownloadAgent, string> DownloadAgentFailure;

            #region 核心方法1：“DownloadAgent”下载任务的驱动核心
            /// <summary>
            /// 开始处理下载任务。
            /// </summary>
            /// <param name="task">要处理的下载任务。</param>
            /// <returns>开始处理任务的状态。</returns>
            public StartTaskStatus Start(DownloadTask task)
            {
                if (task == null)
                {
                    throw new GameFrameworkException("Task is invalid.");
                }

                m_Task = task;

                m_Task.Status = DownloadTaskStatus.Doing;
                //下载过程中的临时文件：在原路径末尾加“.download”后缀即可
                string downloadFile = Utility.Text.Format("{0}.download", m_Task.DownloadPath);

                try
                {
                    //为了支持”断点下载“，这里保留上一次下载的文件，在旧文件的基础上继续下载(不可直接删除旧文件，否则”断点下载“功能无法支持)
                    if (File.Exists(downloadFile))
                    {
                        m_FileStream = File.OpenWrite(downloadFile);
                        //将“stream.Position”定位到“该fileStream的末尾”：从stream末尾开始“0”个offset偏移，因此即代表在原stream的末尾开始写入新的数据
                        m_FileStream.Seek(0L, SeekOrigin.End);
                        //m_SavedLenth 代表当前已经保存到磁盘的字节大小
                        m_StartLength = m_SavedLength = m_FileStream.Length;
                        m_DownloadedLength = 0L;  //本次下载大小
                    }
                    else
                    {
                        //检测本地路径的文件夹是否创建，如果没有，则创建的“文件夹路径”
                        string directory = Path.GetDirectoryName(m_Task.DownloadPath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        //在上述文件夹下创建新的文件
                        m_FileStream = new FileStream(downloadFile, FileMode.Create, FileAccess.Write);
                        m_StartLength = m_SavedLength = m_DownloadedLength = 0L;
                    }

                    //通知外部：下载开始
                    if (DownloadAgentStart != null)
                    {
                        DownloadAgentStart(this);
                    }

                    //这里“Download”的两个重载方法应该可以合并，现在这样太繁琐了
                    if (m_StartLength > 0L)
                    {
                        m_Helper.Download(m_Task.DownloadUri, m_StartLength, m_Task.UserData);
                    }
                    else
                    {
                        m_Helper.Download(m_Task.DownloadUri, m_Task.UserData);
                    }

                    return StartTaskStatus.CanResume;
                }
                catch (Exception exception)
                {
                    DownloadAgentHelperErrorEventArgs downloadAgentHelperErrorEventArgs = DownloadAgentHelperErrorEventArgs.Create(false, exception.ToString());
                    OnDownloadAgentHelperError(this, downloadAgentHelperErrorEventArgs);
                    ReferencePool.Release(downloadAgentHelperErrorEventArgs);
                    return StartTaskStatus.UnknownError;
                }
            }

            /// <summary>
            /// 针对“下载模块”的“TaskAgent”，需要其进度没有更新的“持续时间”
            /// 注意：这里的“m_WaitTime”代表的是“距离上次下载的进度更新后的时间”，如果“限定时间内，进度依然没有更新”，则代表“当前下载出现超时情况”
            /// PS：这个“m_WaitTime”需要更换命名，强调是“距离上次进度更新后的持续时间”，而非“整个任务的下载持续时间”
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                if (m_Task.Status == DownloadTaskStatus.Doing)
                {
                    m_WaitTime += realElapseSeconds;
                    if (m_WaitTime >= m_Task.Timeout)
                    {
                        DownloadAgentHelperErrorEventArgs downloadAgentHelperErrorEventArgs = DownloadAgentHelperErrorEventArgs.Create(false, "Timeout");
                        OnDownloadAgentHelperError(this, downloadAgentHelperErrorEventArgs);
                        ReferencePool.Release(downloadAgentHelperErrorEventArgs);
                    }
                }
            }

            #endregion

            #region 核心方法2：下载文件过程中的各个回调(每个回调中监听的事件均是由“Download模块”内部触发，然后该回调监听到该事件后使用“相应的委托”通知其他用到“本Download模块”业务逻辑的外部该消息)
            //从理论上讲：以下两个回调其实可以合并，但如果要细分需求：
            //OnDownloadAgentHelperUpdateBytes主要是需要将“下载到的bytes数据写入fileStream中”，并存储到“本地文件介质”
            //OnDownloadAgentHelperUpdateLength则主要是通知外部该事件
            //从这个角度来讲：DownloadAgentHelperUpdateLengthEventArgs事件存在的意义并不大，其可以用“DownloadAgentHelperUpdateBytesEventArgs”来代替
            //
            //问：“DownloadAgentHelperUpdateBytesEventArgs”和“DownloadAgentUpdate”的区别是什么？
            //答：DownloadAgentHelperUpdateBytesEventArgs事件是由“Download模块”内部发生，只有在使用“UnityWebRequest.Get”下载远程文件“有进度更新”时才触发
            //   而“DownloadAgentUpdate”则是“通知外部该消息的委托”
            private void OnDownloadAgentHelperUpdateBytes(object sender, DownloadAgentHelperUpdateBytesEventArgs e)
            {
                //只要“下载过程中”有“进度更新”，则需要重置“当前的等待时间”(PS：这傻逼的命名一定要改，太不合适了)
                m_WaitTime = 0f;
                try
                {
                    //这里的“e.Offset”默认为0，代表从“e.GetBytes”的字节数组中“offset偏移开始”，一共“e.Length”个字节的数据，写入“m_FileStream”中
                    m_FileStream.Write(e.GetBytes(), e.Offset, e.Length);
                    m_WaitFlushSize += e.Length;   //“m_WaitFlushSize”代表“等待存储到本地文件中的字节总大小”

                    //问题：为什么此时就需要累计“已经存储起来的字节大小”, 而不等待“FileStream.Flush”方法执行之后再累计？
                    //解答：从理论上讲，是只有在”将字节数据实际存储到本地文件介质中“时才会刷新该数值。但如果最后阶段传递过来的数据量较少，始终没有达到“m_Task.FlushSize”的要求，
                    //    则该数值不会被累计。此时则会出现“明明已经下载完，并且存储到本地文件介质中”，但“m_SavedLength数值却没有递增“的情况
                    //PS：在最后阶段，如果缓存区中的数据总量无法满足”m_Task.FlushSize“要求，此时调用”FileStream.Close“方法仍然会将”缓存区的所有数据“自动存入”文件介质中“
                    //   并不会导致”缓存区中数据丢失“的情况。这应该是”FileStream类型“本身具备的”内部机制“。
                    m_SavedLength += e.Length;

                    if (m_WaitFlushSize >= m_Task.FlushSize)
                    {
                        //将内存中的“m_FileStream”的数据写入“本地文件介质”中。从理论上讲：只有在此时才能更新“m_SavedLength”的数值
                        m_FileStream.Flush();
                        m_WaitFlushSize = 0;  //重置该参数，方便下次统计
                    }
                }
                catch (Exception exception)
                {
                    DownloadAgentHelperErrorEventArgs downloadAgentHelperErrorEventArgs = DownloadAgentHelperErrorEventArgs.Create(false, exception.ToString());
                    OnDownloadAgentHelperError(this, downloadAgentHelperErrorEventArgs);
                    ReferencePool.Release(downloadAgentHelperErrorEventArgs);
                }
            }

            private void OnDownloadAgentHelperUpdateLength(object sender, DownloadAgentHelperUpdateLengthEventArgs e)
            {
                //重要：只要收到本事件，则说明“加载进度有更新”，此时则需要重置本“持续等待时间”
                m_WaitTime = 0f;
                m_DownloadedLength += e.DeltaLength;
                if (DownloadAgentUpdate != null)
                {
                    DownloadAgentUpdate(this, e.DeltaLength);
                }
            }

            //注意：“DownloadAgentHelperCompleteEventArgs”中传递过来的“e.Length”代表“本次下载该文件”时“一共下载的总字节大小”(该文件可能之前下载了一部分，本次继续接着下载)
            private void OnDownloadAgentHelperComplete(object sender, DownloadAgentHelperCompleteEventArgs e)
            {
                //该“持续时间”依然需要重置
                m_WaitTime = 0f;
                //注意：这里是“直接赋值”，而非“OnDownloadAgentHelperUpdateLength”中的”累计更新“
                m_DownloadedLength = e.Length;
                //“m_SavedLength”代表“已经存到本地文件介质中的字节总大小”
                //PS：这里其实不应该这样写。“m_SavedLength - m_StartLength”代表“本次下载的总数量”，又因为之前“每次Update回调”时，“m_DownloadedLength”参数已经累计过
                //   因此这里应该将“已经累计过的m_DownloadedLength”与“事件中传递过来的e.Length”进行比较，若两者不等，则说明”下载异常“
                if (m_SavedLength != CurrentLength)
                {
                    throw new GameFrameworkException("Internal download error.");
                }

                m_Helper.Reset();
                //这里的”m_FileStream“是”xx.download“文件的fileStream
                //注意：这里有一个默认逻辑：由于下载过程中每次会通过“DownloadHandler.ReceiveData”将bytes数据传递过来，当满足“m_WaitFlushSize”限制时，将这些数据写入“本地文件”
                //     因此存在这样一种情况：
                //     如果最后一次调用“DownloadHandler.ReceiveData”方法传递过来的bytes数据依然无法满足“m_WaitFlushSize”时，这里的“fileStream.Close”方法应该会默认将数据再写入“本地文件介质”中
                m_FileStream.Close();
                m_FileStream = null;

                //如果该文件之前已经有，则删除原来的旧文件，因为本次是重新下载完成的新文件(有可能是同一命名的文件，但旧文件代表的是“上一个资源版本”的文件，所以本次更新后需要替换)
                if (File.Exists(m_Task.DownloadPath))
                {
                    File.Delete(m_Task.DownloadPath);
                }

                File.Move(Utility.Text.Format("{0}.download", m_Task.DownloadPath), m_Task.DownloadPath);

                m_Task.Status = DownloadTaskStatus.Done;

                //通知外部该消息
                if (DownloadAgentSuccess != null)
                {
                    DownloadAgentSuccess(this, e.Length);
                }

                m_Task.Done = true;
            }

            private void OnDownloadAgentHelperError(object sender, DownloadAgentHelperErrorEventArgs e)
            {
                m_Helper.Reset();
                if (m_FileStream != null)
                {
                    m_FileStream.Close();
                    m_FileStream = null;
                }

                //针对于”下载失败“的情况，由于导致”下载失败“的原因可能有多种，
                //实际上因为要支持”断点下载“，只有在确定”是因为下载的数据有误“时才需要删除已经下载的数据，其他情况默认是”不能删除已下载的数据“的
                //但是从现有机制以及实际情况来看(由于”TimeOut“导致的下载失败，此时无法删除”xx.download“文件，但也因此后续无法下载，导致卡住流程)
                //(出现上述情况时，只要删除下载失败的”xx.download“文件即可”再次正常下载，保证游戏流程不被卡住“)
                //所以，从”Download模块“本身的功能来讲，不适宜加入”判断下载失败时什么情况下可以删除xx.download文件“，实际上也无法判断
                //(因为由于”TimeOut“导致的下载失败，也会卡住游戏流程)
                //所以将”下载失败“后删除”xx.download“文件的逻辑交由用到的功能模块来处理(如下载同一个文件超出RetryCount后直接删除”xx.download“文件以方便后续重新下载)
                if (e.DeleteDownloading)
                {
                    File.Delete(Utility.Text.Format("{0}.download", m_Task.DownloadPath));
                }

                m_Task.Status = DownloadTaskStatus.Error;

                if (DownloadAgentFailure != null)
                {
                    DownloadAgentFailure(this, e.ErrorMessage);
                }

                m_Task.Done = true;
            }

            #endregion

            #region 框架固定方法
            /// <summary>
            /// 构造方法
            /// </summary>
            /// <param name="downloadAgentHelper">下载代理辅助器。</param>
            public DownloadAgent(IDownloadAgentHelper downloadAgentHelper)
            {
                if (downloadAgentHelper == null)
                {
                    throw new GameFrameworkException("Download agent helper is invalid.");
                }

                m_Helper = downloadAgentHelper;
                m_Task = null;
                m_FileStream = null;
                m_WaitFlushSize = 0;
                m_WaitTime = 0f;
                m_StartLength = 0L;
                m_DownloadedLength = 0L;
                m_SavedLength = 0L;
                m_Disposed = false;

                DownloadAgentStart = null;
                DownloadAgentUpdate = null;
                DownloadAgentSuccess = null;
                DownloadAgentFailure = null;
            }

            /// <summary>
            /// 初始化该下载代理
            /// PS：其实这里的逻辑完全可以放在”构造方法“中执行，但本方法专门提供给”各个业务逻辑中继承TaskAgent的扩展子类“，用于处理其”各个TaskPool中的TaskAgent独有的逻辑“
            /// </summary>
            public void Initialize()
            {
                m_Helper.DownloadAgentHelperUpdateBytes += OnDownloadAgentHelperUpdateBytes;
                m_Helper.DownloadAgentHelperUpdateLength += OnDownloadAgentHelperUpdateLength;
                m_Helper.DownloadAgentHelperComplete += OnDownloadAgentHelperComplete;
                m_Helper.DownloadAgentHelperError += OnDownloadAgentHelperError;
            }

            /// <summary>
            /// 关闭并清理下载代理。
            /// </summary>
            public void Shutdown()
            {
                Dispose();

                m_Helper.DownloadAgentHelperUpdateBytes -= OnDownloadAgentHelperUpdateBytes;
                m_Helper.DownloadAgentHelperUpdateLength -= OnDownloadAgentHelperUpdateLength;
                m_Helper.DownloadAgentHelperComplete -= OnDownloadAgentHelperComplete;
                m_Helper.DownloadAgentHelperError -= OnDownloadAgentHelperError;
            }

            #endregion

            #region 重置代理即清除数据相关的操作：针对“FileStream/UnityWebRequest”等类型的对象，需调用“GC.SuppressFinalize(this)”等方法释放内存占用
            /// <summary>
            /// 重置下载代理。
            /// PS：重置与“Dispose”的区别在于：这里只是将该对象的参数进行重置，该对象占用的内存资源并没有被释放，后续还可能再次使用该对象
            /// </summary>
            public void Reset()
            {
                m_Helper.Reset();

                if (m_FileStream != null)
                {
                    m_FileStream.Close();
                    m_FileStream = null;
                }

                m_Task = null;
                m_WaitFlushSize = 0;
                m_WaitTime = 0f;
                m_StartLength = 0L;
                m_DownloadedLength = 0L;
                m_SavedLength = 0L;
            }

            /// <summary>
            /// 真正提供给外部手动调用释放资源的“Dispose”方法。该方法是“实现IDisposable接口”中的方法
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// 该方法只能内部使用，不可被外部调用
            /// </summary>
            /// <param name="disposing">释放资源标记。</param>
            private void Dispose(bool disposing)
            {
                if (m_Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    if (m_FileStream != null)
                    {
                        m_FileStream.Dispose();
                        m_FileStream = null;
                    }
                }

                m_Disposed = true;
            }

            #endregion

            #region 属性
            /// <summary>
            /// 获取下载任务。
            /// </summary>
            public DownloadTask Task
            {
                get
                {
                    return m_Task;
                }
            }

            /// <summary>
            /// 获取已经等待时间。
            /// </summary>
            public float WaitTime
            {
                get
                {
                    return m_WaitTime;
                }
            }

            /// <summary>
            /// 获取开始下载时已经存在的大小。
            /// PS：因为支持”断点下载“，因此初始下载时，可能该文件已经有”之前下载好的数据“
            /// </summary>
            public long StartLength
            {
                get
                {
                    return m_StartLength;
                }
            }

            /// <summary>
            /// 获取本次已经下载的大小。
            /// </summary>
            public long DownloadedLength
            {
                get
                {
                    return m_DownloadedLength;
                }
            }

            /// <summary>
            /// 获取当前一共已经下载好的大小
            /// </summary>
            public long CurrentLength
            {
                get
                {
                    return m_StartLength + m_DownloadedLength;
                }
            }

            /// <summary>
            /// 获取已经存盘的大小。
            /// </summary>
            public long SavedLength
            {
                get
                {
                    return m_SavedLength;
                }
            }

            #endregion
        }
    }
}
