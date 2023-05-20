//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.WebRequest
{
    internal sealed partial class WebRequestManager : GameFrameworkModule, IWebRequestManager
    {
        /// <summary>
        /// Web 请求代理。
        /// </summary>
        private sealed class WebRequestAgent : ITaskAgent<WebRequestTask>
        {
            private WebRequestTask m_Task;
            private readonly IWebRequestAgentHelper m_Helper;

            //代表在使用”UnityWebRequest.Get/Post“向服务器发送消息后等待的时间，如果超出时间限制则代表超时
            //注意：这里与”Download模块“中的”计算超时时间的方式“不同。由于这里在发送消息给服务器后，很快就会收到消息，
            //并不会”像Download模块那样不断检测下载进度，然后在下载进度更新时不断重置m_WaitTime参数“
            //总之：”WebRequest模块“中的”超时“代表的是”在向服务器发出UnityWebRequest.Get/Post“后等待回复的时间
            //    而”Download模块“的”超时“是在”相邻两个下载进度更新之间“等待的时间，只有距离上次”下载进度更新“超出时间限制时才会被判定为”超时“
            private float m_WaitTime;

            //提供给”WebRequestManager“监听的委托
            public GameFrameworkAction<WebRequestAgent> WebRequestAgentStart;
            public GameFrameworkAction<WebRequestAgent, byte[]> WebRequestAgentSuccess;
            public GameFrameworkAction<WebRequestAgent, string> WebRequestAgentFailure;

            #region 框架固定方法
            /// <summary>
            /// 初始化 Web 请求代理的新实例。
            /// </summary>
            /// <param name="webRequestAgentHelper">Web 请求代理辅助器。</param>
            public WebRequestAgent(IWebRequestAgentHelper webRequestAgentHelper)
            {
                if (webRequestAgentHelper == null)
                {
                    throw new GameFrameworkException("Web request agent helper is invalid.");
                }

                m_Helper = webRequestAgentHelper;
                m_Task = null;  //”m_Task“的初始值为null
                m_WaitTime = 0f;

                WebRequestAgentStart = null;
                WebRequestAgentSuccess = null;
                WebRequestAgentFailure = null;
            }

            /// <summary>
            /// 初始化 Web 请求代理。
            /// </summary>
            public void Initialize()
            {
                m_Helper.WebRequestAgentHelperComplete += OnWebRequestAgentHelperComplete;
                m_Helper.WebRequestAgentHelperError += OnWebRequestAgentHelperError;
            }

            #endregion

            #region 核心方法1：尝试启动WebRequestTask，并在"Update"中监测“是否超时”
            /// <summary>
            /// 开始处理 Web 请求任务。
            /// </summary>
            /// <param name="task">要处理的 Web 请求任务。</param>
            /// <returns>开始处理任务的状态。</returns>
            public StartTaskStatus Start(WebRequestTask task)
            {
                if (task == null)
                {
                    throw new GameFrameworkException("Task is invalid.");
                }

                m_Task = task;
                m_Task.Status = WebRequestTaskStatus.Doing;

                if (WebRequestAgentStart != null)
                {
                    WebRequestAgentStart(this);
                }

                //以下的两种方法其实有严格的参数限制：
                //第二种方法需要这里传递的”postData“数据其本身是”string类型“，否则不能用第二种方法。从这个角度来看，第二种方法的参数其实需要改成”string类型“，而不能直接用”byte[]“
                //        获取在”WebRequestTask“中添加能够直接表明”m_PostData“参数代表的”真实类型“，这样也能直接进行转换
                //第一种方法则完全是瞎搞，胡乱融合"WWWForm“和”userData“数据，应该是要直接避免的
                byte[] postData = m_Task.GetPostData();
                if (postData == null)
                {
                    //这里的“m_Task.UserData”参数外部已经将其封装成了“WWWFormInfo”对象，但其实这样做是”极为不合适的“
                    m_Helper.Request(m_Task.WebRequestUri, m_Task.UserData);
                }
                else
                {
                    m_Helper.Request(m_Task.WebRequestUri, postData, m_Task.UserData);
                }

                m_WaitTime = 0f;
                return StartTaskStatus.CanResume;
            }

            /// <summary>
            /// Web 请求代理轮询。
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                //对当前正在执行的”WebRequestTask“进行”超时监听“
                if (m_Task.Status == WebRequestTaskStatus.Doing)
                {
                    m_WaitTime += realElapseSeconds;
                    if (m_WaitTime >= m_Task.Timeout)
                    {
                        WebRequestAgentHelperErrorEventArgs webRequestAgentHelperErrorEventArgs = WebRequestAgentHelperErrorEventArgs.Create("Timeout");
                        OnWebRequestAgentHelperError(this, webRequestAgentHelperErrorEventArgs);
                        ReferencePool.Release(webRequestAgentHelperErrorEventArgs);
                    }
                }
            }

            #endregion

            #region 核心方法2：向服务器发送“UnityWebRequest.Get/Post”后收到回应，此时触发回调：只有成功和失败两种情况的回调，没有“过程中的回调”
            private void OnWebRequestAgentHelperComplete(object sender, WebRequestAgentHelperCompleteEventArgs e)
            {
                m_Helper.Reset();
                m_Task.Status = WebRequestTaskStatus.Done;

                if (WebRequestAgentSuccess != null)
                {
                    WebRequestAgentSuccess(this, e.GetWebResponseBytes());
                }

                m_Task.Done = true;
            }

            private void OnWebRequestAgentHelperError(object sender, WebRequestAgentHelperErrorEventArgs e)
            {
                m_Helper.Reset();
                m_Task.Status = WebRequestTaskStatus.Error;

                if (WebRequestAgentFailure != null)
                {
                    WebRequestAgentFailure(this, e.ErrorMessage);
                }

                m_Task.Done = true;
            }

            #endregion

            #region 重置相关数据
            /// <summary>
            /// 关闭并清理 Web 请求代理。
            /// </summary>
            public void Shutdown()
            {
                //从本质上讲，虽然本脚本中没有“FileStream”或“UnityWebRequest”类型对象，但其用到的“TaskAgentHelper”中有，
                //而“m_Helper.Reset”中并没有区分是“执行任务结束后的正常重置”，还是“由于游戏关闭导致整体框架的Shutdown”
                //所以这里在调用“TaskAgent自身的Reset方法”时，应该添加“额外参数”来区分这两种情况：
                //如果是第一种，则调用“m_Helper.Reset()”；如果是第二种，则调用“m_Helper.Dispose()”
                //所以从严谨的角度讲：当前的这种写法是错误的。导致的结果就是“GC在回收m_Helper中的m_UnityWebRequest对象”时依然会调用其“析构函数”，增加GC负担，影响性能
                Reset();
                m_Helper.WebRequestAgentHelperComplete -= OnWebRequestAgentHelperComplete;
                m_Helper.WebRequestAgentHelperError -= OnWebRequestAgentHelperError;
            }

            /// <summary>
            /// 重置 Web 请求代理。
            /// </summary>
            public void Reset()
            {
                m_Helper.Reset();
                m_Task = null;
                m_WaitTime = 0f;
            }

            #endregion

            #region 属性
            /// <summary>
            /// 获取 Web 请求任务。
            /// </summary>
            public WebRequestTask Task
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

            #endregion
        }
    }
}
