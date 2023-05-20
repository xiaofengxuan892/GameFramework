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
    /// 有限状态机状态基类。
    /// 注意：1.虽然声明该类型时，弄了一个“T”，但实际上FsmState的所有逻辑都是通过其所属的Fsm来实现的，与“T —— 持有者”没有任何关系，
    ///        "T —— 持有者"是用来限制“Fsm”的，跟”FsmState“其实没有直接的关系
    ///        并且该FsmState拥有的“OnInit/OnEnter/OnUpdate/OnLeave/OnDestroy”，包含后续的“ChangeState”工具方法也是通过"其所属的Fsm”来实现的
    ///     2.该类型中应该增加一个“FsmBase”参数，用于代表本FsmState所属的Fsm实例。“切换FsmState“的操作必须通过其所属的Fsm来实现，而不能用外部传过来的Fsm实例
    /// 总结：在声明该FsmState时完全不需要“泛型T”，只需要在内部增加“FsmBase”参数，用于代表本FsmState所属的Fsm实例即可
    /// </summary>
    /// <typeparam name="T">有限状态机持有者类型。</typeparam>
    public abstract class FsmState<T> where T : class
    {
        /// <summary>
        /// 初始化有限状态机状态基类的新实例。
        /// </summary>
        public FsmState()
        {
        }

        #region 核心方法1：每个FsmState中必须包含的一些方法，用于执行其核心逻辑
        /// <summary>
        /// 有限状态机状态初始化时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnInit(IFsm<T> fsm)
        {
        }

        /// <summary>
        /// 有限状态机状态进入时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnEnter(IFsm<T> fsm)
        {
        }

        /// <summary>
        /// 有限状态机状态轮询时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        protected internal virtual void OnUpdate(IFsm<T> fsm, float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 有限状态机状态离开时调用。
        /// PS：“isShutdown”参数的主要目的在于：
        /// 如果其为true，代表“整个游戏关闭”，此时需要清理所有数据；
        /// 而如果为false，代表只是“普通的切换状态”而已，此时则只需要将相关数据重置即可
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="isShutdown">是否是关闭有限状态机时触发。</param>
        protected internal virtual void OnLeave(IFsm<T> fsm, bool isShutdown)
        {
        }

        /// <summary>
        /// 有限状态机状态销毁时调用。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        protected internal virtual void OnDestroy(IFsm<T> fsm)
        {
        }

        #endregion

        #region 核心方法2：切换状态ChangeState。该方法应该由每个FsmState来提供，因为外部在调用时，不应该去关注其所属的Fsm实例，直接在当前状态来切换即可。所以需要在“FsmState”类型中添加该方法
        //PS: 应该增加一个“FsmBase”参数用于代表“本FsmState所属的Fsm实例”，如果本FsmState需要“ChangeState”，则只能通过“其所属的Fsm实例”来执行，而不能直接通过外部传递过来的Fsm执行

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        /// <param name="fsm">有限状态机引用。</param>
        /// <param name="stateType">要切换到的有限状态机状态类型。</param>
        protected void ChangeState(IFsm<T> fsm, Type stateType)
        {
            Fsm<T> fsmImplement = (Fsm<T>)fsm;
            if (fsmImplement == null)
            {
                throw new GameFrameworkException("FSM is invalid.");
            }

            if (stateType == null)
            {
                throw new GameFrameworkException("State type is invalid.");
            }

            if (!typeof(FsmState<T>).IsAssignableFrom(stateType))
            {
                throw new GameFrameworkException(Utility.Text.Format("State type '{0}' is invalid.", stateType.FullName));
            }

            fsmImplement.ChangeState(stateType);
        }

        #endregion

        #region 垃圾重载方法
        /// <summary>
        /// 切换当前有限状态机状态。
        /// PS：参数弄的这么复杂，实际就是完全通过该FsmState所属的Fsm来执行的
        /// </summary>
        /// <typeparam name="TState">要切换到的有限状态机状态类型。</typeparam>
        /// <param name="fsm">有限状态机引用。</param>
        protected void ChangeState<TState>(IFsm<T> fsm) where TState : FsmState<T>
        {
            Fsm<T> fsmImplement = (Fsm<T>)fsm;
            if (fsmImplement == null)
            {
                throw new GameFrameworkException("FSM is invalid.");
            }

            fsmImplement.ChangeState<TState>();
        }
        #endregion
    }
}
