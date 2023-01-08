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
        private IFsmManager m_FsmManager;
        private IFsm<IProcedureManager> m_ProcedureFsm;

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

            //由于“ProcedureBase”本质上其实是“FsmState”，隶属于Fsm，因此若需要关闭该Fsm，则需要通过FsmManager来实现
            //因此“ProcedureManager”包含参数“m_FsmManager”是为此而设立，
            //同时也借助“FsmManager”来创建新的Fsm
            m_FsmManager = fsmManager;
            //通过“FsmManager”来创建和关闭某个Fsm
            //注意：在创建Fsm时需要为该Fsm设置所有的FsmState，并且执行所有FsmState的“OnInit”方法
            m_ProcedureFsm = m_FsmManager.CreateFsm(this, procedures);

            //扩展：
            //本质上FsmManger是管理系统中所有的Fsm的，而每个Fsm都需要通过“owner + name”作为key来唯一标识某个Fsm
            //但由于在整个项目中“ProcedureManager”作为Owner只可能包含一个Fsm，不可能创建多个，因此这里无需设置“name”参数
            //如果该Owner包含多个Fsm，那么此时在该Owner内部应该使用“name”作为key来唯一标识某个Fsm
            //后续在执行Fsm中某些方法时都需要先在字典中查找到该Fsm再执行相关操作
        }

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <typeparam name="T">要开始的流程类型。</typeparam>
        public void StartProcedure<T>() where T : ProcedureBase
        {
            if (m_ProcedureFsm == null)
            {
                //"m_ProcedureFsm"在初始化该“xxxManager”时进行赋值，之后所有的操作都基于该Fsm来实现
                throw new GameFrameworkException("You must initialize procedure first.");
            }

            //借助“Fsm”来启动某个FsmState
            m_ProcedureFsm.Start<T>();
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

            m_ProcedureFsm.Start(procedureType);
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
    }
}
