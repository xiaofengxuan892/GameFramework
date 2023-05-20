//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.Collections.Generic;

namespace GameFramework.UI
{
    internal sealed partial class UIManager : GameFrameworkModule, IUIManager
    {
        /// <summary>
        /// 界面组。
        /// </summary>
        private sealed partial class UIGroup : IUIGroup
        {
            private readonly string m_Name;
            private int m_Depth;
            private bool m_Pause;
            private readonly IUIGroupHelper m_UIGroupHelper;
            private readonly GameFrameworkLinkedList<UIFormInfo> m_UIFormInfos;
            private LinkedListNode<UIFormInfo> m_CachedNode;

            /// <summary>
            /// 初始化界面组的新实例。
            /// </summary>
            /// <param name="name">界面组名称。</param>
            /// <param name="depth">界面组深度。</param>
            /// <param name="uiGroupHelper">界面组辅助器。</param>
            public UIGroup(string name, int depth, IUIGroupHelper uiGroupHelper)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new GameFrameworkException("UI group name is invalid.");
                }

                if (uiGroupHelper == null)
                {
                    throw new GameFrameworkException("UI group helper is invalid.");
                }

                m_Name = name;
                m_Pause = false;
                m_UIGroupHelper = uiGroupHelper;
                m_UIFormInfos = new GameFrameworkLinkedList<UIFormInfo>();
                m_CachedNode = null;
                Depth = depth;
            }

            /// <summary>
            /// 获取界面组名称。
            /// </summary>
            public string Name
            {
                get
                {
                    return m_Name;
                }
            }

            /// <summary>
            /// 获取或设置界面组深度。
            /// </summary>
            public int Depth
            {
                get
                {
                    return m_Depth;
                }
                set
                {
                    if (m_Depth == value)
                    {
                        return;
                    }

                    m_Depth = value;
                    m_UIGroupHelper.SetDepth(m_Depth);
                    Refresh();
                }
            }

            /// <summary>
            /// 获取或设置界面组是否暂停。
            /// PS：当整个游戏暂停时，会调用该方法
            /// </summary>
            public bool Pause
            {
                get
                {
                    return m_Pause;
                }
                set
                {
                    if (m_Pause == value)
                    {
                        return;
                    }

                    m_Pause = value;
                    //当外部直接设置“整个游戏是否暂停”时，需要重新刷新本UIGroup下所有处于“m_UIFormInfos”集合中的元素
                    Refresh();
                }
            }

            /// <summary>
            /// 获取界面组中界面数量。
            /// </summary>
            public int UIFormCount
            {
                get
                {
                    return m_UIFormInfos.Count;
                }
            }

            /// <summary>
            /// 获取当前界面。
            /// </summary>
            public IUIForm CurrentUIForm
            {
                get
                {
                    return m_UIFormInfos.First != null ? m_UIFormInfos.First.Value.UIForm : null;
                }
            }

            /// <summary>
            /// 获取界面组辅助器。
            /// </summary>
            public IUIGroupHelper Helper
            {
                get
                {
                    return m_UIGroupHelper;
                }
            }

            /// <summary>
            /// 界面组轮询。
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                LinkedListNode<UIFormInfo> current = m_UIFormInfos.First;
                while (current != null)
                {
                    if (current.Value.Paused)
                    {
                        break;
                    }

                    m_CachedNode = current.Next;
                    current.Value.UIForm.OnUpdate(elapseSeconds, realElapseSeconds);
                    current = m_CachedNode;
                    m_CachedNode = null;
                }
            }

            /// <summary>
            /// 界面组中是否存在界面。
            /// </summary>
            /// <param name="serialId">界面序列编号。</param>
            /// <returns>界面组中是否存在界面。</returns>
            public bool HasUIForm(int serialId)
            {
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.SerialId == serialId)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// 界面组中是否存在界面。
            /// </summary>
            /// <param name="uiFormAssetName">界面资源名称。</param>
            /// <returns>界面组中是否存在界面。</returns>
            public bool HasUIForm(string uiFormAssetName)
            {
                if (string.IsNullOrEmpty(uiFormAssetName))
                {
                    throw new GameFrameworkException("UI form asset name is invalid.");
                }

                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.UIFormAssetName == uiFormAssetName)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// 从界面组中获取界面。
            /// </summary>
            /// <param name="serialId">界面序列编号。</param>
            /// <returns>要获取的界面。</returns>
            public IUIForm GetUIForm(int serialId)
            {
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.SerialId == serialId)
                    {
                        return uiFormInfo.UIForm;
                    }
                }

                return null;
            }

            /// <summary>
            /// 从界面组中获取界面。
            /// </summary>
            /// <param name="uiFormAssetName">界面资源名称。</param>
            /// <returns>要获取的界面。</returns>
            public IUIForm GetUIForm(string uiFormAssetName)
            {
                if (string.IsNullOrEmpty(uiFormAssetName))
                {
                    throw new GameFrameworkException("UI form asset name is invalid.");
                }

                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.UIFormAssetName == uiFormAssetName)
                    {
                        return uiFormInfo.UIForm;
                    }
                }

                return null;
            }

            /// <summary>
            /// 从界面组中获取界面。
            /// </summary>
            /// <param name="uiFormAssetName">界面资源名称。</param>
            /// <returns>要获取的界面。</returns>
            public IUIForm[] GetUIForms(string uiFormAssetName)
            {
                if (string.IsNullOrEmpty(uiFormAssetName))
                {
                    throw new GameFrameworkException("UI form asset name is invalid.");
                }

                List<IUIForm> results = new List<IUIForm>();
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.UIFormAssetName == uiFormAssetName)
                    {
                        results.Add(uiFormInfo.UIForm);
                    }
                }

                return results.ToArray();
            }

            /// <summary>
            /// 从界面组中获取界面。
            /// </summary>
            /// <param name="uiFormAssetName">界面资源名称。</param>
            /// <param name="results">要获取的界面。</param>
            public void GetUIForms(string uiFormAssetName, List<IUIForm> results)
            {
                if (string.IsNullOrEmpty(uiFormAssetName))
                {
                    throw new GameFrameworkException("UI form asset name is invalid.");
                }

                if (results == null)
                {
                    throw new GameFrameworkException("Results is invalid.");
                }

                results.Clear();
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.UIFormAssetName == uiFormAssetName)
                    {
                        results.Add(uiFormInfo.UIForm);
                    }
                }
            }

            /// <summary>
            /// 从界面组中获取所有界面。
            /// </summary>
            /// <returns>界面组中的所有界面。</returns>
            public IUIForm[] GetAllUIForms()
            {
                List<IUIForm> results = new List<IUIForm>();
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    results.Add(uiFormInfo.UIForm);
                }

                return results.ToArray();
            }

            /// <summary>
            /// 从界面组中获取所有界面。
            /// </summary>
            /// <param name="results">界面组中的所有界面。</param>
            public void GetAllUIForms(List<IUIForm> results)
            {
                if (results == null)
                {
                    throw new GameFrameworkException("Results is invalid.");
                }

                results.Clear();
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    results.Add(uiFormInfo.UIForm);
                }
            }

            /// <summary>
            /// 往界面组增加界面。
            /// </summary>
            /// <param name="uiForm">要增加的界面。</param>
            public void AddUIForm(IUIForm uiForm)
            {
                m_UIFormInfos.AddFirst(UIFormInfo.Create(uiForm));
            }

            /// <summary>
            /// 从界面组移除界面。
            /// </summary>
            /// <param name="uiForm">要移除的界面。</param>
            public void RemoveUIForm(IUIForm uiForm)
            {
                UIFormInfo uiFormInfo = GetUIFormInfo(uiForm);
                if (uiFormInfo == null)
                {
                    throw new GameFrameworkException(Utility.Text.Format("Can not find UI form info for serial id '{0}', UI form asset name is '{1}'.", uiForm.SerialId, uiForm.UIFormAssetName));
                }

                if (!uiFormInfo.Covered)
                {
                    uiFormInfo.Covered = true;
                    uiForm.OnCover();
                }
                if (!uiFormInfo.Paused)
                {
                    uiFormInfo.Paused = true;
                    uiForm.OnPause();
                }

                if (m_CachedNode != null && m_CachedNode.Value.UIForm == uiForm)
                {
                    m_CachedNode = m_CachedNode.Next;
                }

                //"m_UIFormInfos"集合中只包含”当前显示出来的UIForm“，因此需要移除
                if (!m_UIFormInfos.Remove(uiFormInfo))
                {
                    throw new GameFrameworkException(Utility.Text.Format("UI group '{0}' not exists specified UI form '[{1}]{2}'.", m_Name, uiForm.SerialId, uiForm.UIFormAssetName));
                }

                ReferencePool.Release(uiFormInfo);
            }

            /// <summary>
            /// 激活界面。
            /// </summary>
            /// <param name="uiForm">要激活的界面。</param>
            /// <param name="userData">用户自定义数据。</param>
            public void RefocusUIForm(IUIForm uiForm, object userData)
            {
                UIFormInfo uiFormInfo = GetUIFormInfo(uiForm);
                if (uiFormInfo == null)
                {
                    throw new GameFrameworkException("Can not find UI form info.");
                }

                //所谓的“重新激活”就是将其放在“m_UIFormInfos”集合的第一位！！！！
                m_UIFormInfos.Remove(uiFormInfo);
                m_UIFormInfos.AddFirst(uiFormInfo);
            }

            /// <summary>
            /// 刷新界面组。
            /// </summary>
            public void Refresh()
            {
                //由于所有的UIForm都是按照“GameFrameworkLinkedList.AddFirst”的顺序插入该集合，因此集合中的第一个元素为“最新插入的UIForm”
                LinkedListNode<UIFormInfo> current = m_UIFormInfos.First;
                bool pause = m_Pause;   //该参数代表“是否整个游戏被暂停”，默认情况都为true
                bool cover = false;
                int depth = UIFormCount; //获取当前UIGroup下所有UIForm的总数量
                while (current != null && current.Value != null)
                {
                    LinkedListNode<UIFormInfo> next = current.Next;
                    //依据“UIFormCount”深度刷新每个UIForm，集合“m_UIFormInfos”中第一个则是最近使用的UIForm，默认显示在最上面
                    current.Value.UIForm.OnDepthChanged(Depth, depth--);
                    if (current.Value == null)
                    {
                        return;
                    }

                    //这里代表的是整个游戏是否暂停：此时需要将本UIGroup下所有UIForm都“置于Cover和Pause状态”
                    if (pause)
                    {
                        //当游戏暂停时，如果本UIForm当前没有被遮挡，仍然处于“显示状态”，则执行下述逻辑
                        if (!current.Value.Covered)
                        {
                            current.Value.Covered = true;
                            current.Value.UIForm.OnCover();
                            if (current.Value == null)    //该条件判断不是应该放在前面吗，放在这里毫无意义啊
                            {
                                return;
                            }
                        }

                        //当整个游戏暂停时，如果本UIForm界面没有，则执行其“OnPause”方法
                        if (!current.Value.Paused)
                        {
                            current.Value.Paused = true;
                            current.Value.UIForm.OnPause();
                            if (current.Value == null)
                            {
                                return;
                            }
                        }
                    }
                    else
                    {
                        //1.由于每个UIGroup的“m_UIFormInfos”集合中都只保存“当前所有显示状态的UIForm”，
                        //  因此如果在遍历该集合时，发现其中某个UIForm处于暂停状态，那么则需要将其回复OnResume
                        //2.有三种情况会导致某个UIForm被Pause：
                        //  第一种情况：整个游戏被暂停，此时会把“m_UIFormInfos”集合中所有元素都暂停
                        //  第二种情况：当外部调用“OpenUIForm”打开新的界面时，根据“对象池系统中取出”或直接创建出的“UIInstanceObject”，创建对应的“UIFormInfo”对象，此时其“Paused”和“Covered”参数均为true
                        //           PS：因为如果是从“对象池中取出UIInstanceObject”，则必然该对象之前经历了“OnCover”和“OnPause”两个状态，所以此时得到的“Paused”和“Coverd”参数均为true
                        //              对于“对象池中没有该UIForm对应的UIInstanceOjbect”，为了统一处理，这里在创建对应的“UIFormInfo”对象时，都将“Paused”和“Coverd”参数默认为“true”
                        //  第三种情况：该UIForm自身被Close，此时也会导致其Paused参数为true
                        //3."m_UIFormInfos"代表的是“理论上当前所有显示出来的UIForm”，但该UIForm可能由于被其他界面Covered(在刷新UIGroup下各个UIForm的“Canvas.sortingOrder”时，被遮挡的界面必然其sortingOrder没有“当前正在显示的界面的数值高”)或整个游戏暂停
                        //  这两种情况均会导致该UIForm没有在Game视图中直接看到。但其“activeSelf = true”
                        if (current.Value.Paused)
                        {
                            current.Value.Paused = false;
                            current.Value.UIForm.OnResume();
                            if (current.Value == null)
                            {
                                return;
                            }
                        }

                        //1.如果在本界面显示时需要将所有“被本界面覆盖的界面”都暂停，则设置“pause”参数为true。
                        //  PS：该种情况在大部分界面种并不会出现，除非出现像“断网”或者“弹出整个游戏的暂停界面”，只有这些界面会导致其他所有界面都处于“暂停”状态
                        //     该数值可以由每个UIForm界面自身来决定，可以写在该UIForm自身的脚本代码中，如"Config"这种
                        //2.由于UIGroup中“m_UIFormInfos”集合在添加新元素时都是使用“AddFirst”添加在“集合第一个位置”，
                        //  而遍历该集合时，是从集合的第一个位置开始的
                        //  因此如果发现前面的UIForm导致后面所有的其他UIForm都被暂停，则后面继续遍历该集合时，直接执行“pause = true”条件逻辑即可
                        if (current.Value.UIForm.PauseCoveredUIForm)
                        {
                            pause = true;
                        }

                        //注意：
                        //1.每个执行“Refresh”方法刷新本UIGroup下所有UIForm时，该“cover”参数初始时默认都为false
                        //2.“m_UIFormInfos”集合的遍历是从前往后的
                        if (cover)
                        {
                            if (!current.Value.Covered)
                            {
                                current.Value.Covered = true;
                                current.Value.UIForm.OnCover();
                                if (current.Value == null)
                                {
                                    return;
                                }
                            }
                        }
                        else
                        {
                            //由于“m_UIFormInfos”集合的遍历是从前往后的，这里分成三种情况：
                            //情况一：外部使用“OpenUIForm”向“m_UIFormInfos”集合中添加了新的“UIFormInfo”对象。而“UIFormInfo”的“Create”方法中，默认“paused”和“coverd”参数都为true
                            //      因此在遍历时会先获取到“最后添加到m_UIFormInfos”集合中的元素，即最后一个UIForm，此时会调用其”OnReveal“
                            //      (感觉这里的逻辑有点问题：如果该UIForm是直接从对象池中复用的，则调用”OnReveal“；如果是重新创建的新的UIForm，则不应该调用”OnReveal“。
                            //       在创建UIFormInfo对象时应该要针对性的设置下，不能所有UIFormInfo对象都设置”paused“和”coverd“参数都为true)
                            //      总之，在添加新UIForm时，其后面所有其他UIForm都会变成“Covered”状态
                            //情况二：如果最新显示的界面A被关闭了，此时其后紧邻的界面B由于之前被A遮挡，现在A被关闭了，此时则需要将B重新显示出来。这种情况也会调用”界面B的OnReveal“。但界面B后面的其他UIForm依然保持Coverd状态
                            //情况三：当整个游戏暂停时，所有的UIForm都会变成Covered状态，因此如果游戏恢复，则需要将m_UIFormInfos集合中第一个元素恢复成正常状态，此时调用该UIForm的OnReveal方法
                            if (current.Value.Covered)
                            {
                                current.Value.Covered = false;
                                current.Value.UIForm.OnReveal();
                                if (current.Value == null)
                                {
                                    return;
                                }
                            }

                            //该语句至关重要，其代表将本UIForm后所有的界面都设置为“Covered”状态
                            cover = true;
                        }
                    }

                    current = next;
                }
            }

            internal void InternalGetUIForms(string uiFormAssetName, List<IUIForm> results)
            {
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm.UIFormAssetName == uiFormAssetName)
                    {
                        results.Add(uiFormInfo.UIForm);
                    }
                }
            }

            internal void InternalGetAllUIForms(List<IUIForm> results)
            {
                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    results.Add(uiFormInfo.UIForm);
                }
            }

            private UIFormInfo GetUIFormInfo(IUIForm uiForm)
            {
                if (uiForm == null)
                {
                    throw new GameFrameworkException("UI form is invalid.");
                }

                foreach (UIFormInfo uiFormInfo in m_UIFormInfos)
                {
                    if (uiFormInfo.UIForm == uiForm)
                    {
                        return uiFormInfo;
                    }
                }

                return null;
            }
        }
    }
}
