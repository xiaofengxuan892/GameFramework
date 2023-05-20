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
        //PS：本脚本其实没啥用，就一个计算”下载速度“需要用到，其他的地方其实没啥用，有点废物的脚本
        private sealed partial class DownloadCounter
        {
            //每隔”m_UpdateInterval“时间重新计算一次”当前速度“
            private float m_UpdateInterval;
            private float m_TimeLeft;    //用于统计是否到了需要更新速度的时间

            //该参数有两个作用：1.超出该时间的”DownloadCounterNode“节点会从”m_DownloadCounterNodes链表“中回收  2.限制”计算”m_CurrentSpeed“的依据时间“
            private float m_RecordInterval;
            private float m_Accumulator;  //在计算速度时，即统计该时间内下载的总数据量大小。且该时间必然在”m_RecordInterval“以内
            private float m_CurrentSpeed;

            private readonly GameFrameworkLinkedList<DownloadCounterNode> m_DownloadCounterNodes; //保存所有的”DownloadCounterNode节点“

            #region 框架固定方法
            public DownloadCounter(float updateInterval, float recordInterval)
            {
                if (updateInterval <= 0f)
                {
                    throw new GameFrameworkException("Update interval is invalid.");
                }

                if (recordInterval <= 0f)
                {
                    throw new GameFrameworkException("Record interval is invalid.");
                }

                m_DownloadCounterNodes = new GameFrameworkLinkedList<DownloadCounterNode>();
                m_UpdateInterval = updateInterval;
                m_RecordInterval = recordInterval;
                Reset();
            }

            public void Shutdown()
            {
                Reset();
            }

            #endregion

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                if (m_DownloadCounterNodes.Count <= 0)
                {
                    return;
                }

                //设置”计算速度的最近时间段间隔“
                m_Accumulator += realElapseSeconds;
                if (m_Accumulator > m_RecordInterval)
                {
                    m_Accumulator = m_RecordInterval;
                }

                //这个傻逼的参数不是“故弄玄虚”吗？你用“-”号来累计，你是傻逼吗？
                //“m_TimeLeft”的实际作用：每秒钟更新一次“m_CurrentSpeed”
                m_TimeLeft -= realElapseSeconds;

                //这里的目的是“更新每个DownloadCounterNode”的“计时”，该计时有两个作用：
                //1.每个“DownloadCounterNode”存在的时间有限，当超出指定时间后(m_RecordInterval)，则会将其回收
                //2.在收到“DownloadHandler.ReceiveData“的数据后，如果距离上次间隔时间小于”m_UpdateInteval“,则将本次的数据直接放到上个”DownloadCounterNode“节点中
                //  不另外创建新的节点
                foreach (DownloadCounterNode downloadCounterNode in m_DownloadCounterNodes)
                {
                    downloadCounterNode.Update(elapseSeconds, realElapseSeconds);
                }

                //回收”链表中的超时DownloadCounterNode节点“
                while (m_DownloadCounterNodes.Count > 0)
                {
                    DownloadCounterNode downloadCounterNode = m_DownloadCounterNodes.First.Value;
                    //每个只保留“m_RecordInterval”指定时间，超出该时间则将节点回收
                    if (downloadCounterNode.ElapseSeconds < m_RecordInterval)
                    {
                        break;
                    }

                    ReferencePool.Release(downloadCounterNode);
                    m_DownloadCounterNodes.RemoveFirst();
                }

                //如果链表中节点为空，则无需计算当前速度，直接使用”Reset“设置为初始值即可
                if (m_DownloadCounterNodes.Count <= 0)
                {
                    Reset();
                    return;
                }

                //每秒钟更新一次速度：m_TimeLeft的“递减方式”完全就是“故弄玄虚”
                if (m_TimeLeft <= 0f)
                {
                    //累计本次已经下载完成的总数据大小
                    long totalDeltaLength = 0L;
                    foreach (DownloadCounterNode downloadCounterNode in m_DownloadCounterNodes)
                    {
                        totalDeltaLength += downloadCounterNode.DeltaLength;
                    }

                    //由于超出“m_RecordInterval”限制的“DownloadCounterNode节点”会被回收掉，并且“m_Accumulator”始终会被限制在最大值“m_RecordInterval”
                    //因此这里计算的是“指定时间内的速度”，如下载过程中“10s以内的平均速度”，其可以代表“当前网络整体下载情况”
                    //不是每次收到“DownloadHandler.ReceiveData”就计算速度，也不是“计算从开始到现在的整体平均速度”。而是取最近的指定时间段内的平均速度
                    m_CurrentSpeed = m_Accumulator > 0f ? totalDeltaLength / m_Accumulator : 0f;

                    //这里难道不应该直接赋值”m_TimeLeft = m_UpdateInterval“吗？
                    m_TimeLeft += m_UpdateInterval;
                }
            }

            //从以下逻辑来看：当“DownloadHandler.ReceiveData”相邻两次调用的间隔在1秒以内，则共用一个“downloadCounterNode”，
            //否则创建新的“DownloadCounterNode”节点
            //可是：这个功能到底有什么作用啊！！！！！有这个必要吗？
            public void RecordDeltaLength(int deltaLength)
            {
                if (deltaLength <= 0)
                {
                    return;
                }

                DownloadCounterNode downloadCounterNode = null;
                if (m_DownloadCounterNodes.Count > 0)
                {
                    downloadCounterNode = m_DownloadCounterNodes.Last.Value;
                    if (downloadCounterNode.ElapseSeconds < m_UpdateInterval)
                    {
                        downloadCounterNode.AddDeltaLength(deltaLength);
                        return;
                    }
                }

                downloadCounterNode = DownloadCounterNode.Create();
                downloadCounterNode.AddDeltaLength(deltaLength);
                m_DownloadCounterNodes.AddLast(downloadCounterNode);
            }

            private void Reset()
            {
                m_DownloadCounterNodes.Clear();
                m_CurrentSpeed = 0f;
                m_Accumulator = 0f;
                m_TimeLeft = 0f;
            }

            #region 属性
            public float UpdateInterval
            {
                get
                {
                    return m_UpdateInterval;
                }
                set
                {
                    if (value <= 0f)
                    {
                        throw new GameFrameworkException("Update interval is invalid.");
                    }

                    m_UpdateInterval = value;
                    Reset();
                }
            }

            public float RecordInterval
            {
                get
                {
                    return m_RecordInterval;
                }
                set
                {
                    if (value <= 0f)
                    {
                        throw new GameFrameworkException("Record interval is invalid.");
                    }

                    m_RecordInterval = value;
                    Reset();
                }
            }

            public float CurrentSpeed
            {
                get
                {
                    return m_CurrentSpeed;
                }
            }

            #endregion
        }
    }
}
