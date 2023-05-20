//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace GameFramework.Fsm
{
    /// <summary>
    /// 有限状态机。
    /// PS：”IReference“是为了方便”引用池系统“，”IFsm<T>“代表Fsm必须要实现的一些方法(这是抽象类无法代替的特性，因为抽象类没有强制其子类重写其所有方法的要求)
    /// 问题：为什么要将”FsmBase“独立出来，”FsmBase“中包含的所有内容完全可以写在”Fsm“中？
    /// 解答：在”FsmManager“中表示某个Fsm时，可以直接使用”FsmBase“来表示，而无需使用”Fsm<T>“，这样”FsmManager”在声明时也无需添加“泛型T”
    ///      "FsmManager"的作用在于管理整个游戏中所有的“Fsm对象”，因此其声明中必然不适宜加入“泛型T”的限制。外部在调用“FsmManager”的方法时也无需考虑“泛型T”的影响
    ///      从目前来看，应该是“出于方便外部”以及“代码书写简洁”的目的，所以把“FsmBase”单独提取出来
    ///
    /// PS：由于实际逻辑中只是使用“Owner”作为“各个Fsm实例的唯一标识”，所以没有另外增加参数来代表该Fsm实例的拥有者对象
    /// 问题2：为什么不直接创建“object类型参数”来代表“其拥有者”？
    /// 解答：“object类型参数”无法获取其实际的“type”，无法作为标识。而“泛型T”是有明确的“type”的
    ///
    /// 总结：针对以上的特性，其实可以完全在脚本中增加“TypeNamePair”类型参数，代表每个Fsm实例的唯一标识，而无需使用“泛型T”，这样方便理解
    /// </summary>
    /// <typeparam name="T">有限状态机持有者类型。</typeparam>
    internal sealed class Fsm<T> : FsmBase, IReference, IFsm<T> where T : class
    {
        //注意：1.同一个Fsm中绝对不可能存在两个一样的FsmState
        //    2.Fsm中的FsmState以类型来进行区分，不存在两个相同类型的FsmState在一个Fsm中
        private readonly Dictionary<Type, FsmState<T>> m_States;
        private FsmState<T> m_CurrentState;
        private float m_CurrentStateTime;

        //部分情况下如果该“owner”只有一个Fsm，则“m_Name”参数可以设置为“string.Empty”
        private T m_Owner;
        //赋值时机：该参数在”调用构造方法“或”执行Clear方法“时才会为true，其他时候默认为false
        //作用：在”FsmManager.Update轮询”中会根据各个Fsm.Destroyed状态来决定是否需要“执行该Fsm自身的Update方法”
        //    而在“调用构造方法”和“引用池回收后Clear”，这两种情况是无需“调用其自身的Update方法”的
        private bool m_IsDestroyed;

        //该结构专用于存储Fsm中需要用到的一些数据：如各个FsmState运行时会从本集合中获取一些数据用于自身逻辑的执行
        //”Variable“代表”这些数据可以是任何类型，无论是值类型还是引用类型均可以“
        //PS：如果是这样，那为什么不直接使用”Object“来表示，还要专门封装一个新的”Variable类型“呢？是不是有点多余？还是说要通过”新类型Variable“来添加一些自定义方法
        //   从目前的角度看，这是一个很重要，很特别的设置，查看”Variable<T>”类型调用的地方知晓其特别之处
        private Dictionary<string, Variable> m_Datas;

        /// <summary>
        /// 初始化有限状态机的新实例。
        /// PS: 只有在“引用池”中没有该类型的“可用实例”时，才会调用“本构造方法”
        /// </summary>
        public Fsm()
        {
            m_Owner = null;
            m_States = new Dictionary<Type, FsmState<T>>();
            m_Datas = null;
            m_CurrentState = null;
            m_CurrentStateTime = 0f;
            m_IsDestroyed = true;
        }

        #region 核心方法1：创建Fsm对象，并为其添加FsmState。以及启动该Fsm，切换Fsm中当前FsmState
        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>创建的有限状态机。</returns>
        public static Fsm<T> Create(string name, T owner, List<FsmState<T>> states)
        {
            if (owner == null)
            {
                throw new GameFrameworkException("FSM owner is invalid.");
            }

            if (states == null || states.Count < 1)
            {
                throw new GameFrameworkException("FSM states is invalid.");
            }

            Fsm<T> fsm = ReferencePool.Acquire<Fsm<T>>();
            fsm.Name = name;
            fsm.m_Owner = owner;
            //由于此时要向Fsm中添加“各个FsmState”了，因此下一帧就需要对该Fsm中包含的所有FsmState进行遍历，因此这里需要设置为“false”
            fsm.m_IsDestroyed = false;
            foreach (FsmState<T> state in states)
            {
                if (state == null)
                {
                    throw new GameFrameworkException("FSM states is invalid.");
                }

                //这里得到的是“FsmState”的扩展子类型
                Type stateType = state.GetType();
                //1.理论上该将“Fsm”放入引用池之前已经该Fsm对象进行Clear操作，因此当再次从引用池中获取到该对象实例时，自然不会包含“stateType”
                //  这里增加判断可以在一定程度上避免报错
                //2.添加此判断也可以说明：同一个Fsm中是绝对不可能存在两个相同的FsmState的，并且Fsm中的FsmState以类型来进行区分
                if (fsm.m_States.ContainsKey(stateType))
                {
                    throw new GameFrameworkException(Utility.Text.Format("FSM '{0}' state '{1}' is already exist.", new TypeNamePair(typeof(T), name), stateType.FullName));
                }

                //为该Fsm的"m_States"(字典形式)中添加新的FsmState
                fsm.m_States.Add(stateType, state);
                //当向Fsm中添加FsmState时，即会马上执行该FsmState的“OnInit”函数
                state.OnInit(fsm);

                //扩展：以上的操作其实可以提取成"FSM"提供给外部的一个public方法：用于向Fsm中添加FsmState。现在这样直接操作其“m_States”感觉有些突兀，不是“高级程序员”的代码风格
            }

            //创建Fsm完毕后将其作为方法返回值
            return fsm;

            //注意：在创建Fsm时仅仅只是设置该Fsm中包含的各个FsmState，并将各个FsmState初始化
            //    此时该Fsm并没有启动，即”m_CurrentState”仍保持为null，直到外部调用“Start”方法启动该Fsm为止
        }

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        /// <param name="stateType">要开始的有限状态机状态类型。</param>
        public void Start(Type stateType)
        {
            //注意：在创建Fsm时仅仅只是将各个FsmState添加到该Fsm中，并执行各个FsmState.OnInit()，此时并没有设置其当前运行状态，该Fsm也没有启动
            //本方法的目的在于：提供给外部可以“自由设置该Fsm的第一个FsmState入口”
            if (IsRunning)
            {
                throw new GameFrameworkException("FSM is running, can not start again.");
            }

            if (stateType == null)
            {
                throw new GameFrameworkException("State type is invalid.");
            }

            //验证“外部传进来的stateType”参数必须为”FsmState<T>“的扩展子类
            if (!typeof(FsmState<T>).IsAssignableFrom(stateType))
            {
                throw new GameFrameworkException(Utility.Text.Format("State type '{0}' is invalid.", stateType.FullName));
            }

            //从Fsm的“m_States”集合中获取该“FsmState扩展类型”的相应实例(本质上这里直接使用扩展类的string形式即可，还方便理解)
            FsmState<T> state = GetState(stateType);
            if (state == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("FSM '{0}' can not start state '{1}' which is not exist.", new TypeNamePair(typeof(T), Name), stateType.FullName));
            }

            m_CurrentStateTime = 0f;   //开始计时当前FsmState持续的时间
            m_CurrentState = state;
            m_CurrentState.OnEnter(this);  //执行该FsmState自身的“OnEnter”方法
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        /// <param name="stateType">要切换到的有限状态机状态类型。</param>
        internal void ChangeState(Type stateType)
        {
            if (m_CurrentState == null)
            {
                throw new GameFrameworkException("Current state is invalid.");
            }

            FsmState<T> state = GetState(stateType);
            if (state == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("FSM '{0}' can not change state to '{1}' which is not exist.", new TypeNamePair(typeof(T), Name), stateType.FullName));
            }

            m_CurrentState.OnLeave(this, false);    //先退出当前状态
            //由于需要切换状态，因此重置当前状态的计时
            //注意：重置该计时必须在”旧状态的FsmState.OnLeave“后，且在”新状态的FsmState.OnEnter“之前执行。
            //    因为调用”旧状态的FsmState.OnLeave“或”新状态FsmState.OnEnter“方法，其执行过程可能都需要一段时间.
            //    因此必须在两者之间重置该计时数据
            //    而”m_CurrentStateTime = 0f, m_CurrentState = state“这两句的执行几乎不花任何时间，
            //    因此对”新状态的持续时间“也不会有影响(顶多可能相隔”一帧deltaTime“的时间，有些情况甚至”一帧都没有“，因此可忽略不计)
            m_CurrentStateTime = 0f;
            m_CurrentState = state;
            m_CurrentState.OnEnter(this);
        }

        #endregion

        #region 核心方法2：该Fsm的”Update轮询“，回收该Fsm以及清理其相关数据
        /// <summary>
        /// 有限状态机轮询。
        /// Fsm的Update轮询的主要作用：更新当前状态的持续时间，以及调用当前FsmState的Update方法执行该状态自身的”Update轮询“
        /// 所以若出现以下任一情况，则无需执行该Fsm的Update轮询：1.Fsm.isDestroyed = true   2.该Fsm的”m_CurrentState = null“
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            //只要该Fsm的”当前状态为null“，则无需执行该Fsm的”Update轮询“
            if (m_CurrentState == null)
            {
                return;
            }

            //更新当前FsmState的持续时间(该参数在切换FsmState时会重置)
            m_CurrentStateTime += elapseSeconds;
            //执行该FsmState自身的“Update轮询”逻辑
            m_CurrentState.OnUpdate(this, elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 关闭并清理有限状态机。
        /// </summary>
        internal override void Shutdown()
        {
            //由于该Fsm本身实现了”IReference“接口，因此可以直接使用”引用池“进行回收。引用池内部会自动调用该Fsm的”Clear“方法重置相关数据
            ReferencePool.Release(this);
        }

        /// <summary>
        /// 清理有限状态机。提供给“引用池系统”回收该Fsm对象时使用
        /// </summary>
        public void Clear()
        {
            if (m_CurrentState != null)
            {
                m_CurrentState.OnLeave(this, true);
            }

            foreach (KeyValuePair<Type, FsmState<T>> state in m_States)
            {
                state.Value.OnDestroy(this);
            }

            Name = null;
            m_Owner = null;
            //以上“遍历m_States集合，对一个FsmState执行OnDestroy”，主要是改变集合中的各个元素，但该元素依然在集合中。所以这里需要重新调用“Clear”方法才行
            m_States.Clear();

            if (m_Datas != null)
            {
                //回收集合中的每个元素自身，但该元素依然在集合中
                foreach (KeyValuePair<string, Variable> data in m_Datas)
                {
                    if (data.Value == null)
                    {
                        continue;
                    }

                    ReferencePool.Release(data.Value);
                }

                //清理该集合
                m_Datas.Clear();
            }

            m_CurrentState = null;
            m_CurrentStateTime = 0f;
            m_IsDestroyed = true;
        }

        /// <summary>
        /// 设置有限状态机数据。
        /// </summary>
        /// <param name="name">有限状态机数据名称。</param>
        /// <param name="data">要设置的有限状态机数据。</param>
        public void SetData(string name, Variable data)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new GameFrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                m_Datas = new Dictionary<string, Variable>(StringComparer.Ordinal);
            }

            //先回收之前的旧数据
            Variable oldData = GetData(name);
            if (oldData != null)
            {
                ReferencePool.Release(oldData);
            }

            //再设置新数据
            m_Datas[name] = data;
        }

        /// <summary>
        /// 移除有限状态机数据。
        /// </summary>
        /// <param name="name">有限状态机数据名称。</param>
        /// <returns>是否移除有限状态机数据成功。</returns>
        public bool RemoveData(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new GameFrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return false;
            }

            Variable oldData = GetData(name);
            if (oldData != null)
            {
                ReferencePool.Release(oldData);
            }

            return m_Datas.Remove(name);
        }

        #endregion

        #region 工具方法：外部获取Fsm中相关数据的常用方法
        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        /// <param name="stateType">要检查的有限状态机状态类型。</param>
        /// <returns>是否存在有限状态机状态。</returns>
        public bool HasState(Type stateType)
        {
            if (stateType == null)
            {
                throw new GameFrameworkException("State type is invalid.");
            }

            if (!typeof(FsmState<T>).IsAssignableFrom(stateType))
            {
                throw new GameFrameworkException(Utility.Text.Format("State type '{0}' is invalid.", stateType.FullName));
            }

            return m_States.ContainsKey(stateType);
        }

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        /// <param name="stateType">要获取的有限状态机状态类型。</param>
        /// <returns>要获取的有限状态机状态。</returns>
        public FsmState<T> GetState(Type stateType)
        {
            if (stateType == null)
            {
                throw new GameFrameworkException("State type is invalid.");
            }

            if (!typeof(FsmState<T>).IsAssignableFrom(stateType))
            {
                throw new GameFrameworkException(Utility.Text.Format("State type '{0}' is invalid.", stateType.FullName));
            }

            FsmState<T> state = null;
            if (m_States.TryGetValue(stateType, out state))
            {
                return state;
            }

            return null;
        }

        /// <summary>
        /// 获取有限状态机的所有状态。
        /// </summary>
        /// <returns>有限状态机的所有状态。</returns>
        public FsmState<T>[] GetAllStates()
        {
            int index = 0;
            FsmState<T>[] results = new FsmState<T>[m_States.Count];
            foreach (KeyValuePair<Type, FsmState<T>> state in m_States)
            {
                results[index++] = state.Value;
            }

            return results;
        }

        /// <summary>
        /// 获取有限状态机的所有状态。
        /// </summary>
        /// <param name="results">有限状态机的所有状态。</param>
        public void GetAllStates(List<FsmState<T>> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            //确保该参数只包含”目标需要的元素“
            results.Clear();
            foreach (KeyValuePair<Type, FsmState<T>> state in m_States)
            {
                results.Add(state.Value);
            }
        }

        /// <summary>
        /// 是否存在有限状态机数据。
        /// </summary>
        /// <param name="name">有限状态机数据名称。</param>
        /// <returns>有限状态机数据是否存在。</returns>
        public bool HasData(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new GameFrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return false;
            }

            return m_Datas.ContainsKey(name);
        }

        /// <summary>
        /// 获取有限状态机数据。
        /// </summary>
        /// <typeparam name="TData">要获取的有限状态机数据的类型。</typeparam>
        /// <param name="name">有限状态机数据名称。</param>
        /// <returns>要获取的有限状态机数据。</returns>
        public TData GetData<TData>(string name) where TData : Variable
        {
            return (TData)GetData(name);
        }

        /// <summary>
        /// 获取有限状态机数据。
        /// </summary>
        /// <param name="name">有限状态机数据名称。</param>
        /// <returns>要获取的有限状态机数据。</returns>
        public Variable GetData(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new GameFrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return null;
            }

            Variable data = null;
            if (m_Datas.TryGetValue(name, out data))
            {
                return data;
            }

            return null;
        }

        /// <summary>
        /// 设置有限状态机数据。
        /// </summary>
        /// <typeparam name="TData">要设置的有限状态机数据的类型。</typeparam>
        /// <param name="name">有限状态机数据名称。</param>
        /// <param name="data">要设置的有限状态机数据。</param>
        public void SetData<TData>(string name, TData data) where TData : Variable
        {
            SetData(name, (Variable)data);
        }

        #endregion

        #region 属性
        /// <summary>
        /// 获取有限状态机持有者。
        /// </summary>
        public T Owner
        {
            get
            {
                return m_Owner;
            }
        }

        /// <summary>
        /// 获取有限状态机持有者类型。
        /// </summary>
        public override Type OwnerType
        {
            get
            {
                return typeof(T);
            }
        }

        /// <summary>
        /// 获取有限状态机中状态的数量。
        /// </summary>
        public override int FsmStateCount
        {
            get
            {
                return m_States.Count;
            }
        }

        /// <summary>
        /// 获取有限状态机是否正在运行。
        /// </summary>
        public override bool IsRunning
        {
            get
            {
                return m_CurrentState != null;
            }
        }

        /// <summary>
        /// 获取有限状态机是否被销毁。
        /// </summary>
        public override bool IsDestroyed
        {
            get
            {
                return m_IsDestroyed;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态。
        /// </summary>
        public FsmState<T> CurrentState
        {
            get
            {
                return m_CurrentState;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态名称。
        /// </summary>
        public override string CurrentStateName
        {
            get
            {
                return m_CurrentState != null ? m_CurrentState.GetType().FullName : null;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态持续时间。
        /// </summary>
        public override float CurrentStateTime
        {
            get
            {
                return m_CurrentStateTime;
            }
        }
        #endregion

        #region 垃圾重载方法
        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>创建的有限状态机。</returns>
        public static Fsm<T> Create(string name, T owner, params FsmState<T>[] states)
        {
            if (owner == null)
            {
                throw new GameFrameworkException("FSM owner is invalid.");
            }

            if (states == null || states.Length < 1)
            {
                throw new GameFrameworkException("FSM states is invalid.");
            }

            Fsm<T> fsm = ReferencePool.Acquire<Fsm<T>>();
            fsm.Name = name;
            fsm.m_Owner = owner;
            fsm.m_IsDestroyed = false;
            foreach (FsmState<T> state in states)
            {
                if (state == null)
                {
                    throw new GameFrameworkException("FSM states is invalid.");
                }

                Type stateType = state.GetType();
                if (fsm.m_States.ContainsKey(stateType))
                {
                    throw new GameFrameworkException(Utility.Text.Format("FSM '{0}' state '{1}' is already exist.", new TypeNamePair(typeof(T), name), stateType.FullName));
                }
                fsm.m_States.Add(stateType, state);
                state.OnInit(fsm);
            }

            return fsm;
        }

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        /// <typeparam name="TState">要开始的有限状态机状态类型。</typeparam>
        public void Start<TState>() where TState : FsmState<T>
        {
            if (IsRunning)
            {
                throw new GameFrameworkException("FSM is running, can not start again.");
            }

            FsmState<T> state = GetState<TState>();
            if (state == null)
            {
                //“Format”方法中会自动触发“TypeNamePair”的“ToString”方法
                throw new GameFrameworkException(Utility.Text.Format("FSM '{0}' can not start state '{1}' which is not exist.", new TypeNamePair(typeof(T), Name), typeof(TState).FullName));
            }

            m_CurrentStateTime = 0f;  //设置FsmState持续时间
            m_CurrentState = state;
            m_CurrentState.OnEnter(this);
        }

        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        /// <typeparam name="TState">要检查的有限状态机状态类型。</typeparam>
        /// <returns>是否存在有限状态机状态。</returns>
        public bool HasState<TState>() where TState : FsmState<T>
        {
            return m_States.ContainsKey(typeof(TState));
        }

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        /// <typeparam name="TState">要获取的有限状态机状态类型。</typeparam>
        /// <returns>要获取的有限状态机状态。</returns>
        public TState GetState<TState>() where TState : FsmState<T>
        {
            FsmState<T> state = null;
            if (m_States.TryGetValue(typeof(TState), out state))
            {
                return (TState)state;
            }

            return null;
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        /// <typeparam name="TState">要切换到的有限状态机状态类型。</typeparam>
        internal void ChangeState<TState>() where TState : FsmState<T>
        {
            ChangeState(typeof(TState));
        }

        #endregion
    }
}
