//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 任务池
    /// PS：任务池中只存有“当前正在执行”以及“等待执行”的任务，对于“已经执行完成的任务”，则不会存储，其会使用“引用池系统”直接回收“已经完成的TaskBase任务对象”
    /// 注意：每一个”T“对象的”任务池“中，其各个任务的编号SerialId都是从”0“开始编号的，并且”同一个任务池“中各个”任务的编号“必然不同，绝对不存在两个任务编号一致的情况
    ///      但如果是两个不同的”任务池对象“，由于其内部任务编号都是从"0"开始，因此是存在两个任务编号一致的。但两者是完全平行的，互不干扰。所以没有影响
    /// </summary>
    /// <typeparam name="T">任务类型。</typeparam>
    internal sealed class TaskPool<T> where T : TaskBase
    {
        //栈的特点在于“后进先出”
        private readonly Stack<ITaskAgent<T>> m_FreeAgents;
        //链表则直接在任意指定位置插入新的元素，“GameFrameworkLinkedList”是基于“System.Collection.Generic.LinkedList”类型的封装
        private readonly GameFrameworkLinkedList<ITaskAgent<T>> m_WorkingAgents;
        private readonly GameFrameworkLinkedList<T> m_WaitingTasks;
        //注意：
        //1.“m_WorkingAgents”代表当前正在执行任务的代理，每个代理中当前必然负责一个具体的“TaskBase”任务。
        //  而“m_WaitingTasks”代表当前正在等待执行的任务集合
        //2.两个集合必然是“互斥”的，即任何一个task不可能同时既在“m_WorkingAgents”又在“m_WaitingTasks”中
        //3.两个集合中任务的总和代表“任务池”中当前仍存在的任务总量。而对于“已经执行完毕”的任务，则不存在以上任意集合中
        //  执行完毕的“TaskBase对象”会被“引用池系统”使用“ReferencePool.Release”方法回收掉
        //4.对于”m_WaitingTasks“，在添加任务时需要根据”任务的priority“在”m_WaitingTasks“的指定位置添加，此时需要先找到”该位置节点“
        //5."m_WaitingTasks"只是保存一系列“IReference”任务对象而已，而“m_WorkingAgents”包含一系列的“TaskAgent对象”，每个对象当前都负责一个具体的“taskBase对象”
        //
        // PS: ”LinkedList<T>“的相关方法：
        //     查找指定节点：
        //     LinkedList.Find(T value)：在链表中查找到与目标value一致的节点
        //     添加元素：
        //     AddBefore, AddAfter: 在指定节点的前面或后面添加新节点
        //     AddFirst, AddLast：在整个”LinkedList“链表的开头处或结尾处添加节点
        //     删除元素：
        //     Remove：直接删除指定节点，
        // 注意：1.以上方法均需要先查找到目标节点，然后在”目标节点“的”前面、后面“添加新的节点，
        //       但无论哪种操作，均不需要考虑”链表中先断开原有节点之间的联系，然后为新节点添加联系“这样的过程
        //     2.链表的每个节点中包含”头部“和”value“两部分，在外部能够接触到的仅有”value“的部分，因此查找时也仅仅是通过”value“在”链表“中查找
        //
        //    ”LinkedList“可以直接使用的参数：
        //    LinkedList.First：链表起始节点
        //    LinkedList.Last：链表最后的节点

        private bool m_Paused;

        #region 核心方法1：驱动整个”任务池系统“
        /// <summary>
        /// 任务池轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_Paused)
            {
                return;
            }

            ProcessRunningTasks(elapseSeconds, realElapseSeconds);
            ProcessWaitingTasks(elapseSeconds, realElapseSeconds);
        }

        private void ProcessRunningTasks(float elapseSeconds, float realElapseSeconds)
        {
            LinkedListNode<ITaskAgent<T>> current = m_WorkingAgents.First;
            while (current != null)
            {
                T task = current.Value.Task;
                //“已经正在执行中”的任务，通常并不会将其强制终止，如“终止正在异步加载中的场景或assets”。
                //因此只需要检测其是否完成即可(如果将“StartTaskStatus”和“TaskStatus“两种枚举合并，并将”TaskBase“中的”m_Done“替换成”TaskStatus“类型，则只需要检测该变量是否为”TaskStatus.Done“即可)
                if (!task.Done)
                {
                    //执行每个TaskAgent自身的“Update轮询”逻辑
                    current.Value.Update(elapseSeconds, realElapseSeconds);
                    current = current.Next;
                    continue;
                }

                LinkedListNode<ITaskAgent<T>> next = current.Next;
                current.Value.Reset();
                m_FreeAgents.Push(current.Value);
                m_WorkingAgents.Remove(current);
                ReferencePool.Release(task);
                current = next;
            }
        }

        private void ProcessWaitingTasks(float elapseSeconds, float realElapseSeconds)
        {
            LinkedListNode<T> current = m_WaitingTasks.First;
            while (current != null && FreeAgentCount > 0)
            {
                ITaskAgent<T> agent = m_FreeAgents.Pop();
                //新的“workingAgent”总是在“链表末尾“添加
                //PS：按照当前”m_WorkingAgents“的实际作用，其实替换成”队列“更为方便，应该将其与”m_WaitingTasks“的必要性区分开
                LinkedListNode<ITaskAgent<T>> agentNode = m_WorkingAgents.AddLast(agent);
                T task = current.Value;
                LinkedListNode<T> next = current.Next;
                //每次都会执行”Start“方法尝试启动”各个TaskAgent当前负责的实际TaskBase对象“，如果”尝试启动失败“，则将该”TaskAgent重置“并重新放入”m_FreeAgents“列表中
                //从这样的逻辑来看，应该将”Start“方法重命名为”TryStart“才合适
                StartTaskStatus status = agent.Start(task);
                if (status == StartTaskStatus.Done || status == StartTaskStatus.HasToWait || status == StartTaskStatus.UnknownError)
                {
                    agent.Reset();
                    m_FreeAgents.Push(agent);
                    m_WorkingAgents.Remove(agentNode);
                }

                //至于该taskBase对象当前是否在”m_WorkingAgents“中则不用管
                //注意：当返回”CanResume“，则代表该任务启动成功，后续会继续执行
                //PS：如果更换方法名为”TryStart“，并且方法返回值为”bool“类型，代表是否启动任务成功。同时如果任务启动失败，则返回失败的原因
                //   如果失败的原因是”Done/UnknowError“，则将该”taskBase对象“回收掉
                if (status == StartTaskStatus.Done || status == StartTaskStatus.CanResume || status == StartTaskStatus.UnknownError)
                {
                    m_WaitingTasks.Remove(current);
                }

                if (status == StartTaskStatus.Done || status == StartTaskStatus.UnknownError)
                {
                    ReferencePool.Release(task);
                }

                current = next;
            }
        }

        #endregion

        #region 核心方法2：向”任务池系统“中添加”TaskAgent“以及”要执行的TaskBase对象“，以及“移除指定SerialId”的任务
        /// <summary>
        /// 增加任务代理。
        /// PS：对于“TaskAgent”没有“优先级”的必要，无论是”m_WorkingAgents“或”m_FreeAgents“，直接在末尾添加即可
        /// </summary>
        /// <param name="agent">要增加的任务代理。</param>
        public void AddAgent(ITaskAgent<T> agent)
        {
            if (agent == null)
            {
                throw new GameFrameworkException("Task agent is invalid.");
            }

            //每个“TaskAgent”在添加时都需要执行“Initialize”操作，并放入“空闲TaskAgent列表”中
            agent.Initialize();
            m_FreeAgents.Push(agent);
        }

        /// <summary>
        /// 根据“TaskBase优先级”在“GameFrameworkLinkedList”链表的指定位置插入元素。
        /// 注意：默认按照“优先级从大到小的顺序排列”，因此遍历时先从集合最后一个元素开始比较
        /// </summary>
        /// <param name="task">要增加的任务。</param>
        public void AddTask(T task)
        {
            //支持根据“每个任务的优先级”在“LinkedList”指定位置插入元素
            LinkedListNode<T> current = m_WaitingTasks.Last;
            while (current != null)
            {
                if (task.Priority <= current.Value.Priority)
                {
                    break;
                }

                current = current.Previous;
            }

            if (current != null)
            {
                m_WaitingTasks.AddAfter(current, task);
            }
            else
            {
                m_WaitingTasks.AddFirst(task);
            }
        }

        /// <summary>
        /// 根据任务的序列编号移除任务。
        /// </summary>
        /// <param name="serialId">要移除任务的序列编号。</param>
        /// <returns>是否移除任务成功。</returns>
        public bool RemoveTask(int serialId)
        {
            foreach (T task in m_WaitingTasks)
            {
                //“同一任务池”中“所有任务的编号”唯一
                if (task.SerialId == serialId)
                {
                    //注意：这里的”task“是”GameFrameworkLinkedList“中已经封装好的”Remove“方法，
                    //其内部会先根据”task“在”链表中查找指定value的节点“，之后直接移除该节点
                    m_WaitingTasks.Remove(task);
                    ReferencePool.Release(task);
                    return true;
                }
            }

            LinkedListNode<ITaskAgent<T>> currentWorkingAgent = m_WorkingAgents.First;
            while (currentWorkingAgent != null)
            {
                LinkedListNode<ITaskAgent<T>> next = currentWorkingAgent.Next;
                //节点仅仅只是“LinkedListNode”类型，必须通过“Value属性”获取到其中的实际数据
                ITaskAgent<T> workingAgent = currentWorkingAgent.Value;
                T task = workingAgent.Task;
                if (task.SerialId == serialId)
                {
                    workingAgent.Reset();
                    m_FreeAgents.Push(workingAgent);
                    m_WorkingAgents.Remove(currentWorkingAgent);
                    ReferencePool.Release(task);
                    return true;
                }

                //如果“SerialId不匹配”，则继续遍历下一个元素
                currentWorkingAgent = next;
            }

            return false;
        }

        #endregion

        #region 框架自身固定方法
        /// <summary>
        /// 初始化任务池的新实例。
        /// </summary>
        public TaskPool()
        {
            m_FreeAgents = new Stack<ITaskAgent<T>>();
            m_WorkingAgents = new GameFrameworkLinkedList<ITaskAgent<T>>();
            m_WaitingTasks = new GameFrameworkLinkedList<T>();
            m_Paused = false;
        }

        /// <summary>
        /// 关闭并清理任务池。
        /// </summary>
        public void Shutdown()
        {
            RemoveAllTasks();

            while (FreeAgentCount > 0)
            {
                m_FreeAgents.Pop().Shutdown();
            }
        }

        #endregion

        #region 其他重要方法：根据“SerialId”或“tag”获取指定任务的相关信息，以及“移除指定任务”的相关方法

        /// <summary>
        /// 根据任务的标签移除任务。
        /// </summary>
        /// <param name="tag">要移除任务的标签。</param>
        /// <returns>移除任务的数量。</returns>
        public int RemoveTasks(string tag)
        {
            int count = 0;

            LinkedListNode<T> currentWaitingTask = m_WaitingTasks.First;
            while (currentWaitingTask != null)
            {
                LinkedListNode<T> next = currentWaitingTask.Next;
                T task = currentWaitingTask.Value;
                if (task.Tag == tag)
                {
                    m_WaitingTasks.Remove(currentWaitingTask);
                    ReferencePool.Release(task);
                    count++;
                }

                currentWaitingTask = next;
            }

            LinkedListNode<ITaskAgent<T>> currentWorkingAgent = m_WorkingAgents.First;
            while (currentWorkingAgent != null)
            {
                LinkedListNode<ITaskAgent<T>> next = currentWorkingAgent.Next;
                ITaskAgent<T> workingAgent = currentWorkingAgent.Value;
                T task = workingAgent.Task;
                if (task.Tag == tag)
                {
                    workingAgent.Reset();
                    m_FreeAgents.Push(workingAgent);
                    m_WorkingAgents.Remove(currentWorkingAgent);
                    ReferencePool.Release(task);
                    count++;
                }

                currentWorkingAgent = next;
            }

            return count;
        }

        /// <summary>
        /// 移除所有任务。
        /// </summary>
        /// <returns>移除任务的数量。</returns>
        public int RemoveAllTasks()
        {
            int count = m_WaitingTasks.Count + m_WorkingAgents.Count;

            //各个任务其实仅仅只是使用“IReference”接口扩展出的引用类型而已，因此需要将其使用“引用池系统”回收
            foreach (T task in m_WaitingTasks)
            {
                ReferencePool.Release(task);
            }
            m_WaitingTasks.Clear();

            //“TaskAgent”并不会回收，因为还要留作下次任务使用。但这里会将其重置，并将其负责的task回收
            foreach (ITaskAgent<T> workingAgent in m_WorkingAgents)
            {
                T task = workingAgent.Task;
                workingAgent.Reset();
                m_FreeAgents.Push(workingAgent);
                ReferencePool.Release(task);
            }
            m_WorkingAgents.Clear();

            //注意：这里无论是“m_WaitingTasks”还是“m_WorkingAgents”，在将其内部元素都清空后，并不需要将其本身设置为null。
            //因为这里的目标是“清除当前的所有任务”，而并非“关闭整个框架或游戏”

            return count;   //这里真的需要返回“count”数值吗？什么情况下需要这个数值？感觉作用其实不大
        }

        /// <summary>
        /// 根据任务的序列编号获取任务的信息。
        /// </summary>
        /// <param name="serialId">要获取信息的任务的序列编号。</param>
        /// <returns>任务的信息。</returns>
        public TaskInfo GetTaskInfo(int serialId)
        {
            foreach (ITaskAgent<T> workingAgent in m_WorkingAgents)
            {
                T workingTask = workingAgent.Task;
                //每个任务的id是唯一的，因此只要满足即可直接return结束本方法
                if (workingTask.SerialId == serialId)
                {
                    return new TaskInfo(workingTask.SerialId, workingTask.Tag, workingTask.Priority, workingTask.UserData, workingTask.Done ? TaskStatus.Done : TaskStatus.Doing, workingTask.Description);
                }
            }

            foreach (T waitingTask in m_WaitingTasks)
            {
                if (waitingTask.SerialId == serialId)
                {
                    return new TaskInfo(waitingTask.SerialId, waitingTask.Tag, waitingTask.Priority, waitingTask.UserData, TaskStatus.Todo, waitingTask.Description);
                }
            }

            return default(TaskInfo);
        }

        /// <summary>
        /// 根据任务的标签获取任务的信息。
        /// </summary>
        /// <param name="tag">要获取信息的任务的标签。</param>
        /// <returns>任务的信息。</returns>
        public TaskInfo[] GetTaskInfos(string tag)
        {
            List<TaskInfo> results = new List<TaskInfo>();
            GetTaskInfos(tag, results);
            return results.ToArray();
        }

        /// <summary>
        /// 根据任务的标签获取任务的信息。
        /// </summary>
        /// <param name="tag">要获取信息的任务的标签。</param>
        /// <param name="results">任务的信息。</param>
        public void GetTaskInfos(string tag, List<TaskInfo> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (ITaskAgent<T> workingAgent in m_WorkingAgents)
            {
                T workingTask = workingAgent.Task;
                if (workingTask.Tag == tag)
                {
                    results.Add(new TaskInfo(workingTask.SerialId, workingTask.Tag, workingTask.Priority, workingTask.UserData, workingTask.Done ? TaskStatus.Done : TaskStatus.Doing, workingTask.Description));
                }
            }

            foreach (T waitingTask in m_WaitingTasks)
            {
                if (waitingTask.Tag == tag)
                {
                    results.Add(new TaskInfo(waitingTask.SerialId, waitingTask.Tag, waitingTask.Priority, waitingTask.UserData, TaskStatus.Todo, waitingTask.Description));
                }
            }
        }

        /// <summary>
        /// 获取所有任务的信息。
        /// </summary>
        /// <returns>所有任务的信息。</returns>
        public TaskInfo[] GetAllTaskInfos()
        {
            int index = 0;
            //“m_WorkingAgents”和“m_WaitingTasks”集合中“元素数量的总和”代表当前“任务池系统”中“还没有完成”的任务，对于“已经完成的任务”会被“引用池系统”直接回收，不会继续存在任务池中
            TaskInfo[] results = new TaskInfo[m_WorkingAgents.Count + m_WaitingTasks.Count];
            foreach (ITaskAgent<T> workingAgent in m_WorkingAgents)
            {
                T workingTask = workingAgent.Task;
                //“TaskStatus”的作用感觉和“StartTaskStatus”重复了，建议合并。合并之后只需要根据”该taskBase任务的状态是否为TaskStatus.Done“即可判断出其是否为”正在执行的状态“
                results[index++] = new TaskInfo(workingTask.SerialId, workingTask.Tag, workingTask.Priority, workingTask.UserData, workingTask.Done ? TaskStatus.Done : TaskStatus.Doing, workingTask.Description);
            }

            foreach (T waitingTask in m_WaitingTasks)
            {
                results[index++] = new TaskInfo(waitingTask.SerialId, waitingTask.Tag, waitingTask.Priority, waitingTask.UserData, TaskStatus.Todo, waitingTask.Description);
            }

            return results;
        }

        #endregion

        #region 属性
        /// <summary>
        /// 获取或设置任务池是否被暂停。
        /// </summary>
        public bool Paused
        {
            get
            {
                return m_Paused;
            }
            set
            {
                m_Paused = value;
            }
        }

        /// <summary>
        /// 获取任务代理总数量。
        /// </summary>
        public int TotalAgentCount
        {
            get
            {
                return FreeAgentCount + WorkingAgentCount;
            }
        }

        /// <summary>
        /// 获取可用任务代理数量。
        /// </summary>
        public int FreeAgentCount
        {
            get
            {
                return m_FreeAgents.Count;
            }
        }

        /// <summary>
        /// 获取工作中任务代理数量。
        /// </summary>
        public int WorkingAgentCount
        {
            get
            {
                return m_WorkingAgents.Count;
            }
        }

        /// <summary>
        /// 获取等待任务数量。
        /// </summary>
        public int WaitingTaskCount
        {
            get
            {
                return m_WaitingTasks.Count;
            }
        }

        #endregion



        #region 作用重复的垃圾方法
        /// <summary>
        /// 获取所有任务的信息。
        /// </summary>
        /// <param name="results">所有任务的信息。</param>
        public void GetAllTaskInfos(List<TaskInfo> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (ITaskAgent<T> workingAgent in m_WorkingAgents)
            {
                T workingTask = workingAgent.Task;
                results.Add(new TaskInfo(workingTask.SerialId, workingTask.Tag, workingTask.Priority, workingTask.UserData, workingTask.Done ? TaskStatus.Done : TaskStatus.Doing, workingTask.Description));
            }

            foreach (T waitingTask in m_WaitingTasks)
            {
                results.Add(new TaskInfo(waitingTask.SerialId, waitingTask.Tag, waitingTask.Priority, waitingTask.UserData, TaskStatus.Todo, waitingTask.Description));
            }
        }

        #endregion
    }
}
