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
        private sealed partial class DownloadCounter
        {
            private sealed class DownloadCounterNode : IReference
            {
                private long m_DeltaLength;  //本节点的数据量大小
                private float m_ElapseSeconds;  //本节点当前总共存在的时间，超出指定时间则会被回收掉

                public DownloadCounterNode()
                {
                    m_DeltaLength = 0L;
                    m_ElapseSeconds = 0f;
                }

                public static DownloadCounterNode Create()
                {
                    return ReferencePool.Acquire<DownloadCounterNode>();
                }

                //更新每个”DownloadCounterNode“的存在时间，超出”m_RecordInterval“则会被回收掉
                public void Update(float elapseSeconds, float realElapseSeconds)
                {
                    m_ElapseSeconds += realElapseSeconds;
                }

                //当相邻的”DownloadCounterNode节点“之间间隔小于”m_UpdateInteval“时，则无需创建新的DownloadCounterNode节点
                //直接把”本次DownloadHandler.ReceiveData“中的”dataLength“附加给上一个”DownloadCounterNode节点“即可
                public void AddDeltaLength(int deltaLength)
                {
                    m_DeltaLength += deltaLength;
                }

                public void Clear()
                {
                    m_DeltaLength = 0L;
                    m_ElapseSeconds = 0f;
                }

                public long DeltaLength
                {
                    get
                    {
                        return m_DeltaLength;
                    }
                }

                public float ElapseSeconds
                {
                    get
                    {
                        return m_ElapseSeconds;
                    }
                }
            }
        }
    }
}
