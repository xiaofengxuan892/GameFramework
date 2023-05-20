//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.ObjectPool;
using UnityEngine;

namespace GameFramework.Entity
{
    internal sealed partial class EntityManager : GameFrameworkModule, IEntityManager
    {
        /// <summary>
        /// 实体实例对象。
        /// </summary>
        private sealed class EntityInstanceObject : ObjectBase
        {
            //这里之所以要加入“m_EntityAsset”参数是为了当需要将某个“EntityInstanceObject”销毁时可以直接“卸载其资源”
            //另外本质上应该把“m_EntityHelper.InstantiateEntity”放到“Create”中执行，这样的话也需要“entityAsset”参数
            //扩展：本脚本“EntityInstanceObject”存在的价值在于：需要使用“ReferencePool.Acquire/Release”对其进行管理
            private object m_EntityAsset;
            //每个模块的“xxManager”应该都有一个“xxHelper”，如果这里确定使用的是该“xxHelper”，为何要在这里加入新的参数呢？？
            private IEntityHelper m_EntityHelper;

            public EntityInstanceObject()
            {
                m_EntityAsset = null;
                m_EntityHelper = null;
            }

            //从实际输出来看：这里的”name“是”prefab“的路径
            //这里的”entityInstance“实际仅仅只是一个”GameObject“而已
            public static EntityInstanceObject Create(string name, object entityAsset, object entityInstance, IEntityHelper entityHelper)
            {
                //为方便鉴别，这里输出“name”
                //GameFrameworkLog.Debug("EntityInstanceObject.name: {0}", name);

                //如果这里确定要使用“entityInstance”，为什么不把“m_EntityHelper.InstantiateEntity”的执行放到这里呢！
                //这样该方法还能省却一个参数“entityInstance”！！！！
                if (entityAsset == null)
                {
                    throw new GameFrameworkException("Entity asset is invalid.");
                }

                if (entityHelper == null)
                {
                    throw new GameFrameworkException("Entity helper is invalid.");
                }

                //本质上所有对象都是属于“ReferencePool”管理的，“ObjectPool”只是其中一个分支(包含额外“释放”逻辑的引用池)
                EntityInstanceObject entityInstanceObject = ReferencePool.Acquire<EntityInstanceObject>();
                entityInstanceObject.Initialize(name, entityInstance);
                entityInstanceObject.m_EntityAsset = entityAsset;
                //此“entityHelper”并不是针对服务于单个“Entity”对象的，而是整个“实体模块”的helper
                //如下的“m_EntityHelper.ReleaseEntity”即可知
                entityInstanceObject.m_EntityHelper = entityHelper;
                return entityInstanceObject;
            }

            public override void Clear()
            {
                base.Clear();
                m_EntityAsset = null;
                m_EntityHelper = null;
            }

            protected internal override void Release(bool isShutdown)
            {
                //由此处的逻辑也能证明：应该把“m_EntityHelper.InstantiateEntity”放到上面的“Create”方法中
                //这样该方法也可以少一个无用的“entityInstance”参数！！
                m_EntityHelper.ReleaseEntity(m_EntityAsset, Target);
            }
        }
    }
}
