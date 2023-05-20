//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.UI
{
    internal sealed partial class UIManager : GameFrameworkModule, IUIManager
    {
        private sealed partial class UIGroup : IUIGroup
        {
            /// <summary>
            /// 界面组界面信息。
            /// </summary>
            private sealed class UIFormInfo : IReference
            {
                private IUIForm m_UIForm;
                private bool m_Paused;
                private bool m_Covered;

                public UIFormInfo()
                {
                    m_UIForm = null;
                    m_Paused = false;
                    m_Covered = false;
                }

                public static UIFormInfo Create(IUIForm uiForm)
                {
                    if (uiForm == null)
                    {
                        throw new GameFrameworkException("UI form is invalid.");
                    }

                    UIFormInfo uiFormInfo = ReferencePool.Acquire<UIFormInfo>();
                    uiFormInfo.m_UIForm = uiForm;
                    //第一阶段：从理论上讲，并不建议每次创建UIFormInfo对象时，都将”Paused“和”Covered“参数都设置为”true“。
                    //        只有在该UIFormInfo所对应的UIInstanceObject是从”对象池“中取出来的复用实例时，才需要如此设置;
                    //        如果该UIForm在对象池中并没有直接可用的实例，则其必然已经被销毁了。此时会重新创建出新的UIInstanceObject对象，这种情况不应该设置”Paused“和”Covered“为”true“
                    //        所以在调用”UIFormInfo.Create“方法时，应该由外部传递过来该”UIInstanceObject“对象是直接从对象池中复用的，如果不是，则这里”m_Paused”和“m_Covered”应该保持为“初始值false”
                    //        只有在其是“对象池中复用的”，才可以将”m_Paused”和“m_Covered”设置为true
                    //第二阶段：经过再次仔细思考后，极容易出现这种情况：在“OnOpen”中向服务器发送协议请求数据，然后在“OnOpen”执行完毕后需要刷新“UIGroup下所有的UIForm”
                    //        此时由于“m_Covered”和“m_Paused”均为true，而这个最新打开的界面是一定要展示出来的，所以会再次执行该UIForm的“OnResume”和“OnReveal”方法
                    //        问题的关键在于：通常如果某个界面被遮挡，如果其恢复显示状态，则为了保证此时界面上展示的数据为最新的，一定会向服务器发送协议请求最新数据
                    //        这样的话就会出现在打开UIForm时，先在OnOpen中发送一次协议，后来在刷新UIGroup中的UIForm时导致其OnReveal又向服务器发送一次协议
                    //        并且两次发送协议的间隔时间极短。而这是绝不允许的，属于“UI模块的系统漏洞”
                    //        因此基于这样的原因，无论其是否“复用对象池中的实例”，在创建“UIFormInfo对象”时，“m_Paused”和“m_Covered”参数都应该设置为“false”
                    uiFormInfo.m_Paused = false;
                    uiFormInfo.m_Covered = false;
                    return uiFormInfo;
                }

                public void Clear()
                {
                    m_UIForm = null;
                    m_Paused = false;
                    m_Covered = false;
                }

                #region 属性
                public IUIForm UIForm
                {
                    get
                    {
                        return m_UIForm;
                    }
                }

                public bool Paused
                {
                    get
                    {
                        return m_Paused;
                    }
                    set
                    {
                        m_Paused = value;
                    }
                }

                public bool Covered
                {
                    get
                    {
                        return m_Covered;
                    }
                    set
                    {
                        m_Covered = value;
                    }
                }

                #endregion
            }
        }
    }
}
