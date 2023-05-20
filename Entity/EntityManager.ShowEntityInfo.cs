//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Entity
{
    internal sealed partial class EntityManager : GameFrameworkModule, IEntityManager
    {
        private sealed class ShowEntityInfo : IReference
        {
            private int m_SerialId;
            private int m_EntityId;
            private EntityGroup m_EntityGroup;
            private object m_UserData;

            public ShowEntityInfo()
            {
                m_SerialId = 0;
                m_EntityId = 0;
                m_EntityGroup = null;
                m_UserData = null;
            }

            public int SerialId
            {
                get
                {
                    return m_SerialId;
                }
            }

            public int EntityId
            {
                get
                {
                    return m_EntityId;
                }
            }

            public EntityGroup EntityGroup
            {
                get
                {
                    return m_EntityGroup;
                }
            }

            public object UserData
            {
                get
                {
                    return m_UserData;
                }
            }

            public static ShowEntityInfo Create(int serialId, int entityId, EntityGroup entityGroup, object userData)
            {
                //请注意：当创建一个新的对象时，使用默认的“new”和这里的“Create”是不一样的
                //这里的“Create”是该类型的“Static”方法，并且在方法体内部使用“ReferencePool.Acquire”来获取该对象实例
                //避免了频繁创建导致的代码GC
                ShowEntityInfo showEntityInfo = ReferencePool.Acquire<ShowEntityInfo>();
                showEntityInfo.m_SerialId = serialId;
                showEntityInfo.m_EntityId = entityId;
                showEntityInfo.m_EntityGroup = entityGroup;
                showEntityInfo.m_UserData = userData;
                return showEntityInfo;
            }

            public void Clear()
            {
                m_SerialId = 0;
                m_EntityId = 0;
                m_EntityGroup = null;
                m_UserData = null;
            }
        }
    }
}
