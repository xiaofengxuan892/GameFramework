//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Fsm;
using System;

namespace GameFramework.Procedure
{
    /// <summary>
    /// 流程管理器。
    /// PS: ProcedureManager的作用就是一个普通的“xxxManager”类，专用于管理某个功能模块，
    ///     并且注意：该类并不是static的，所以需要使用“s_GameFrameworkModules”存储起来
    ///     其作用与Fsm, FsmManger不可“同日而语”
    ///     并且内部所有重要逻辑都需要借助FsmManager和Fsm来实现
    /// </summary>
    internal sealed class ProcedureManager : GameFrameworkModule, IProcedureManager
    {
        //代表本”Procedure模块“的Fsm实例，该参数会在”Initialize“方法中进行赋值，初始为null。
        //PS：”Procedure模块“所有核心逻辑均通过该”Fsm实例“来实现
        private IFsm<IProcedureManager> m_ProcedureFsm;

        //在”创建Fsm实例“以及”销毁该Fsm实例“时会用到”FsmManager“，因此这里提供该变量。
        //PS：从“m_FsmManager”的实际作用来看，其实可以将该参数做成“局部变量”，只有在“创建及销毁Fsm实例”时才从外部获取
        private IFsmManager m_FsmManager;

        /// <summary>
        /// 初始化流程管理器的新实例。
        /// </summary>
        public ProcedureManager()
        {
            m_FsmManager = null;
            m_ProcedureFsm = null;
        }

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        internal override int Priority
        {
            get
            {
                return -2;
            }
        }

        #region 核心方法：利用“框架中的FsmManager”创建本模块专属的“Fsm实例”，以及启动该Fsm实例
        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="fsmManager">有限状态机管理器。</param>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public void Initialize(IFsmManager fsmManager, params ProcedureBase[] procedures)
        {
            if (fsmManager == null)
            {
                throw new GameFrameworkException("FSM manager is invalid.");
            }

            m_FsmManager = fsmManager;  //本参数由于用到的地方有限，因此其实可以做成局部变量，只在用到的地方才获取
            //创建本模块专属的“Fsm实例”：从理论上讲，这里是需要传递该Fsm的name的，但由于“Procedure模块”包含的“Fsm实例”只有一个，因此不传递也可以
            m_ProcedureFsm = m_FsmManager.CreateFsm(this, procedures);
            //注意：这里已经把该“Fsm实例”中包含的所有“FsmState”状态都通过“procedures”参数传递进来了
        }

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public void StartProcedure(Type procedureType)
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            //启动该Fsm实例
            m_ProcedureFsm.Start(procedureType);
        }

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public bool HasProcedure(Type procedureType)
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            return m_ProcedureFsm.HasState(procedureType);
        }

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public ProcedureBase GetProcedure(Type procedureType)
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            return (ProcedureBase)m_ProcedureFsm.GetState(procedureType);
        }

        #endregion

        #region 框架固定方法
        /// <summary>
        /// 流程管理器轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理流程管理器。
        /// PS: 借助FsmManager来关闭其中某个Fsm，但最终的实际执行者仍旧是该Fsm自身的"Shutdown"方法
        /// </summary>
        internal override void Shutdown()
        {
            if (m_FsmManager != null)
            {
                if (m_ProcedureFsm != null)
                {
                    m_FsmManager.DestroyFsm(m_ProcedureFsm);
                    m_ProcedureFsm = null;
                }

                m_FsmManager = null;
            }
        }

        #endregion

        #region 通过本模块专属的“Fsm实例”来获取这些属性
        /// <summary>
        /// 获取当前流程。
        /// PS: 实际是通过Fsm来获取当前的FsmState
        /// </summary>
        public ProcedureBase CurrentProcedure
        {
            get
            {
                if (m_ProcedureFsm == null)
                {
                    throw new GameFrameworkException("You must initialize procedure first.");
                }

                return (ProcedureBase)m_ProcedureFsm.CurrentState;
            }
        }

        /// <summary>
        /// 获取当前流程持续时间。
        /// </summary>
        public float CurrentProcedureTime
        {
            get
            {
                if (m_ProcedureFsm == null)
                {
                    throw new GameFrameworkException("You must initialize procedure first.");
                }

                //实际仍旧是借助Fsm来实现
                return m_ProcedureFsm.CurrentStateTime;
            }
        }

        #endregion

        #region 垃圾重载方法
        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        public void StartProcedure<T>() where T : ProcedureBase
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            m_ProcedureFsm.Start<T>();
        }

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <typeparam name="T">要检查的流程类型。</typeparam>
        /// <returns>是否存在流程。</returns>
        public bool HasProcedure<T>() where T : ProcedureBase
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            return m_ProcedureFsm.HasState<T>();
        }

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <typeparam name="T">要获取的流程类型。</typeparam>
        /// <returns>要获取的流程。</returns>
        public ProcedureBase GetProcedure<T>() where T : ProcedureBase
        {
            if (m_ProcedureFsm == null)
            {
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            return m_ProcedureFsm.GetState<T>();
        }

        #endregion
    }
}
