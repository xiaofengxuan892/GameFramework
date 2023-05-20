//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;

namespace GameFramework.Fsm
{
    /// <summary>
    /// 有限状态机基类
    /// </summary>
    public abstract class FsmBase
    {
        //理论上讲，任意一个Fsm都应该有唯一的"m_Name"参数，但由于加入“m_Owner”，而部分“Owner”在整个项目中其实只会有一个“Fsm”
        //因此在这种情况下，“FsmManager”的“m_Fsms”字典集合中直接可以通过“Owner”唯一标识单个Fsm
        //此时无需设置该Fsm的“m_Name”参数
        private string m_Name;

        /// <summary>
        /// 初始化有限状态机基类的新实例。
        /// </summary>
        public FsmBase()
        {
            m_Name = string.Empty;
        }

        /// <summary>
        /// 有限状态机轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">当前已流逝时间，以秒为单位。</param>
        internal abstract void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 关闭并清理有限状态机。
        /// </summary>
        internal abstract void Shutdown();

        #region 部分重要属性
        /// <summary>
        /// 获取有限状态机是否正在运行。
        /// </summary>
        public abstract bool IsRunning
        {
            get;
        }

        /// <summary>
        /// 获取有限状态机是否被销毁。
        /// </summary>
        public abstract bool IsDestroyed
        {
            get;
        }

        /// <summary>
        /// 获取当前有限状态机状态名称。
        /// </summary>
        public abstract string CurrentStateName
        {
            get;
        }

        /// <summary>
        /// 获取当前有限状态机状态持续时间。
        /// </summary>
        public abstract float CurrentStateTime
        {
            get;
        }
        #endregion

        #region 其他属性
        /// <summary>
        /// 获取有限状态机名称。
        /// </summary>
        public string Name
        {
            get
            {
                return m_Name;
            }
            protected set
            {
                m_Name = value ?? string.Empty;
            }
        }

        /// <summary>
        /// 获取有限状态机完整名称。
        /// </summary>
        public string FullName
        {
            get
            {
                return new TypeNamePair(OwnerType, m_Name).ToString();
            }
        }

        /// <summary>
        /// 获取有限状态机持有者类型。
        /// </summary>
        public abstract Type OwnerType
        {
            get;
        }

        /// <summary>
        /// 获取有限状态机中状态的数量。
        /// </summary>
        public abstract int FsmStateCount
        {
            get;
        }
        #endregion
    }
}
