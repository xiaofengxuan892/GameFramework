﻿//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GameFramework
{
    /// <summary>
    /// 游戏框架链表类。
    /// </summary>
    /// <typeparam name="T">指定链表的元素类型。</typeparam>
    public sealed class GameFrameworkLinkedList<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable
    {
        //“LinkedList”集合的形式可以方便“Priority”参数起到作用，正好用于“GameFrameworkEntry.s_GameFrameworkModules”集合中，
        //用于存储拥有不同“Priority”的“GameFrameworkModule”
        //PS：除非该集合中的节点有“优先级”的需求，否则不建议使用“链表LinkedList”来存储元素，因为“查找”操作时间复杂度很高
        private readonly LinkedList<T> m_LinkedList;
        //加入“m_CachedNodes”的目的在于：“LinkedList”集合中的元素可能发生“增删”操作，为了节省资源，这里使用队列池子存储起来
        private readonly Queue<LinkedListNode<T>> m_CachedNodes;

        #region 固定方法
        /// <summary>
        /// 初始化游戏框架链表类的新实例。
        /// </summary>
        public GameFrameworkLinkedList()
        {
            m_LinkedList = new LinkedList<T>();
            m_CachedNodes = new Queue<LinkedListNode<T>>();
        }

        /// <summary>
        /// 逐个回收”m_LinkedList链表“中的所有节点，并将这些回收的节点放入”m_CachedNodes“集合中，以方便下次直接使用(类似于”引用池“的效果)，同时清空”m_LinkedList链表“
        /// </summary>
        public void Clear()
        {
            //注意：这里仅仅只是将”链表中的每个元素“放入”回收集合m_CachedNodes“中，但该元素依然同时保存在”m_LinkedList“
            //     因为如果在”正序遍历“集合的过程中，删除其中某个元素，则遍历的过程会被打乱。
            //     因此这里只是将所有节点放入”m_CachedNodes“集合，并没有直接将其从”m_LinkedList链表“中移除
            LinkedListNode<T> current = m_LinkedList.First;
            while (current != null)
            {
                ReleaseNode(current);
                current = current.Next;
            }

            //这里才是真正的将节点从”m_LinkedList链表“中移除
            m_LinkedList.Clear();
        }

        #endregion

        #region 核心方法1：通过”LinkedList链表“本身具有的一些重要方法，将其封装成可以被外部业务逻辑直接使用的public方法，如”添加元素“，”检测是否包含指定元素的节点“，”查找指定元素节点“

        /// <summary>
        /// 查找包含指定值的第一个结点。
        /// 注意：该方法只会返回”链表中第一个满足条件的节点“。如果链表后后续还有满足要求的节点，则是完全不考虑的。
        ///      根据此特性，部分情况下其实不需要遍历集合中所有节点，只要找到第一个满足条件的节点即可
        /// </summary>
        /// <param name="value">要查找的指定值。</param>
        /// <returns>包含指定值的第一个结点。</returns>
        public LinkedListNode<T> Find(T value)
        {
            return m_LinkedList.Find(value);
        }

        /// <summary>
        /// 查找包含指定值的最后一个结点。
        /// 注意：该方法在任何情况下都需要遍历整个链表集合，查找到最后一个满足需求的节点。并且不论之前有多少个满足需求的节点都不用，只要最后一个满足需求的节点即可
        /// </summary>
        /// <param name="value">要查找的指定值。</param>
        /// <returns>包含指定值的最后一个结点。</returns>
        public LinkedListNode<T> FindLast(T value)
        {
            return m_LinkedList.FindLast(value);
        }

        /// <summary>
        /// 确定某值是否在链表中。
        /// </summary>
        /// <param name="value">指定值。</param>
        /// <returns>某值是否在链表中。</returns>
        public bool Contains(T value)
        {
            return m_LinkedList.Contains(value);
        }

        /// <summary>
        /// 在链表中指定的现有结点后添加包含指定值的新结点。
        /// </summary>
        /// <param name="node">指定的现有结点。</param>
        /// <param name="value">指定值。</param>
        /// <returns>包含指定值的新结点。</returns>
        public LinkedListNode<T> AddAfter(LinkedListNode<T> node, T value)
        {
            LinkedListNode<T> newNode = AcquireNode(value);
            m_LinkedList.AddAfter(node, newNode);
            return newNode;
        }

        /// <summary>
        /// 在链表中指定的现有结点后添加指定的新结点。
        /// </summary>
        /// <param name="node">指定的现有结点。</param>
        /// <param name="newNode">指定的新结点。</param>
        public void AddAfter(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            m_LinkedList.AddAfter(node, newNode);
        }

        /// <summary>
        /// 在链表中指定的现有结点前添加包含指定值的新结点。
        /// </summary>
        /// <param name="node">指定的现有结点。</param>
        /// <param name="value">指定值。</param>
        /// <returns>包含指定值的新结点。</returns>
        public LinkedListNode<T> AddBefore(LinkedListNode<T> node, T value)
        {
            LinkedListNode<T> newNode = AcquireNode(value);
            m_LinkedList.AddBefore(node, newNode);
            return newNode;
        }

        /// <summary>
        /// 在链表中指定的现有结点前添加指定的新结点。
        /// </summary>
        /// <param name="node">指定的现有结点。</param>
        /// <param name="newNode">指定的新结点。</param>
        public void AddBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            m_LinkedList.AddBefore(node, newNode);
        }

        /// <summary>
        /// 在链表的开头处添加包含指定值的新结点。
        /// </summary>
        /// <param name="value">指定值。</param>
        /// <returns>包含指定值的新结点。</returns>
        public LinkedListNode<T> AddFirst(T value)
        {
            LinkedListNode<T> node = AcquireNode(value);
            m_LinkedList.AddFirst(node);
            return node;
        }

        /// <summary>
        /// 在链表的开头处添加指定的新结点。
        /// </summary>
        /// <param name="node">指定的新结点。</param>
        public void AddFirst(LinkedListNode<T> node)
        {
            m_LinkedList.AddFirst(node);
        }

        /// <summary>
        /// 在链表的结尾处添加包含指定值的新结点。
        /// </summary>
        /// <param name="value">指定值。</param>
        /// <returns>包含指定值的新结点。</returns>
        public LinkedListNode<T> AddLast(T value)
        {
            LinkedListNode<T> node = AcquireNode(value);
            m_LinkedList.AddLast(node);
            return node;
        }

        /// <summary>
        /// 在链表的结尾处添加指定的新结点。
        /// </summary>
        /// <param name="node">指定的新结点。</param>
        public void AddLast(LinkedListNode<T> node)
        {
            m_LinkedList.AddLast(node);
        }

        /// <summary>
        /// 从链表中移除指定值的第一个匹配项。
        /// </summary>
        /// <param name="value">指定值。</param>
        /// <returns>是否移除成功。</returns>
        public bool Remove(T value)
        {
            LinkedListNode<T> node = m_LinkedList.Find(value);
            if (node != null)
            {
                m_LinkedList.Remove(node);
                ReleaseNode(node);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从链表中移除指定的结点。
        /// </summary>
        /// <param name="node">指定的结点。</param>
        public void Remove(LinkedListNode<T> node)
        {
            m_LinkedList.Remove(node);
            ReleaseNode(node);
        }

        /// <summary>
        /// 移除位于链表开头处的结点。
        /// </summary>
        public void RemoveFirst()
        {
            //本质上直接调用”m_LinkedList.RemoveFirst()“即可，但为了安全，这里先检测下该节点是否为null
            //在上述”删除指定节点“时也是需要先检测该节点是否为null的
            //注意：这里只是检测该”LinkedListNode“是否为null，对于”该LinkedListNode.Value“则无需考虑
            LinkedListNode<T> first = m_LinkedList.First;
            if (first == null)
            {
                throw new GameFrameworkException("First is invalid.");
            }

            //这里的“Remove”只是从“LinkedList”集合中移除，该对象“first”本身依然存在
            m_LinkedList.RemoveFirst();
            //因此这里可以直接将该对象“first”加入缓存队列中
            ReleaseNode(first);
        }

        /// <summary>
        /// 移除位于链表结尾处的结点。
        /// </summary>
        public void RemoveLast()
        {
            LinkedListNode<T> last = m_LinkedList.Last;
            if (last == null)
            {
                throw new GameFrameworkException("Last is invalid.");
            }

            m_LinkedList.RemoveLast();
            ReleaseNode(last);
        }

        #endregion

        #region 核心方法2：为了循环利用”LinkedListNode“节点，而不用每次添加新节点时都创建新的”LinkedListNode对象“，这里将所有不用的节点都缓存起来，以方便下次直接使用
        private LinkedListNode<T> AcquireNode(T value)
        {
            LinkedListNode<T> node = null;
            if (m_CachedNodes.Count > 0)
            {
                node = m_CachedNodes.Dequeue();
                node.Value = value;
            }
            else
            {
                node = new LinkedListNode<T>(value);
            }

            return node;
        }

        private void ReleaseNode(LinkedListNode<T> node)
        {
            //注意：由于无法确定“T”类型本身是否“使用了引用池系统”，因此这里无法直接调用“ReferencePool.Release”方法回收该T类型对象
            //    只是重置该节点的Value，然后放入“m_CachedNodes”队列中

            //这里在"放入m_CachedNodes缓存列表前"将”LinkedListNode“中的”Value“重置为初始状态
            node.Value = default(T);
            //将重置为初始状态的对象入缓存队列中，以方便下次取用
            m_CachedNodes.Enqueue(node);
        }

        /// <summary>
        /// 清除链表结点缓存。
        /// </summary>
        public void ClearCachedNodes()
        {
            m_CachedNodes.Clear();
        }

        #endregion

        #region ”LinkedList链表“类型包含的其他可用方法

        /// <summary>
        /// 从目标数组的指定索引处开始将整个链表复制到兼容的一维数组。
        /// </summary>
        /// <param name="array">一维数组，它是从链表复制的元素的目标。数组必须具有从零开始的索引。</param>
        /// <param name="index">array 中从零开始的索引，从此处开始复制。</param>
        public void CopyTo(T[] array, int index)
        {
            m_LinkedList.CopyTo(array, index);
        }

        /// <summary>
        /// 从特定的 ICollection 索引开始，将数组的元素复制到一个数组中。
        /// </summary>
        /// <param name="array">一维数组，它是从 ICollection 复制的元素的目标。数组必须具有从零开始的索引。</param>
        /// <param name="index">array 中从零开始的索引，从此处开始复制。</param>
        public void CopyTo(Array array, int index)
        {
            ((ICollection)m_LinkedList).CopyTo(array, index);
        }

        #endregion

        #region 属性
        /// <summary>
        /// 获取链表中实际包含的结点数量。
        /// </summary>
        public int Count
        {
            get
            {
                return m_LinkedList.Count;
            }
        }

        /// <summary>
        /// 获取链表结点缓存数量。
        /// </summary>
        public int CachedNodeCount
        {
            get
            {
                return m_CachedNodes.Count;
            }
        }

        /// <summary>
        /// 获取链表的第一个结点。
        /// </summary>
        public LinkedListNode<T> First
        {
            get
            {
                return m_LinkedList.First;
            }
        }

        /// <summary>
        /// 获取链表的最后一个结点。
        /// </summary>
        public LinkedListNode<T> Last
        {
            get
            {
                return m_LinkedList.Last;
            }
        }

        /// <summary>
        /// 获取一个值，该值指示 ICollection`1 是否为只读。
        /// </summary>
        public bool IsReadOnly
        {
            get
            {
                return ((ICollection<T>)m_LinkedList).IsReadOnly;
            }
        }

        /// <summary>
        /// 获取可用于同步对 ICollection 的访问的对象。
        /// </summary>
        public object SyncRoot
        {
            get
            {
                return ((ICollection)m_LinkedList).SyncRoot;
            }
        }

        /// <summary>
        /// 获取一个值，该值指示是否同步对 ICollection 的访问（线程安全）。
        /// </summary>
        public bool IsSynchronized
        {
            get
            {
                return ((ICollection)m_LinkedList).IsSynchronized;
            }
        }

        #endregion

        #region 作用有些多余的方法：使用”Enumerator“遍历”LinkedList链表“中的元素。
        //PS：本质上讲并不需要创建新的”Enumerator结构“，直接使用”LinkedList.GetEnumerator()“获取到”枚举集合“，然后调用其”Current、MoveNext()“即可正常遍历”整个链表集合“
        //   并不需要搞得这么麻烦

        /// <summary>
        /// 返回循环访问集合的枚举数。
        /// </summary>
        /// <returns>循环访问集合的枚举数。</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(m_LinkedList);
        }

        /// <summary>
        /// 将值添加到 ICollection`1 的结尾处。
        /// </summary>
        /// <param name="value">要添加的值。</param>
        void ICollection<T>.Add(T value)
        {
            AddLast(value);
        }

        /// <summary>
        /// 返回循环访问集合的枚举数。
        /// </summary>
        /// <returns>循环访问集合的枚举数。</returns>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// 返回循环访问集合的枚举数。
        /// </summary>
        /// <returns>循环访问集合的枚举数。</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// 循环访问集合的枚举数。
        /// PS：其实不是很理解，直接使用”LinkedList.GetEnumerator()“就可以获取到”链表的枚举数“，
        ///     然后直接使用”LinkedList.GetEnumerator()“的”Current“或”MoveNext()“即可，
        ///     为什么这里要专门声明这样一个”Enumerator“类型出来呢？
        /// </summary>
        [StructLayout(LayoutKind.Auto)]
        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private LinkedList<T>.Enumerator m_Enumerator;

            internal Enumerator(LinkedList<T> linkedList)
            {
                if (linkedList == null)
                {
                    throw new GameFrameworkException("Linked list is invalid.");
                }

                m_Enumerator = linkedList.GetEnumerator();
            }

            /// <summary>
            /// 获取当前结点。
            /// </summary>
            public T Current
            {
                get
                {
                    return m_Enumerator.Current;
                }
            }

            /// <summary>
            /// 获取下一个结点。
            /// </summary>
            /// <returns>返回下一个结点。</returns>
            public bool MoveNext()
            {
                return m_Enumerator.MoveNext();
            }

            /// <summary>
            /// 获取当前的枚举数。
            /// </summary>
            object IEnumerator.Current
            {
                get
                {
                    return m_Enumerator.Current;
                }
            }

            /// <summary>
            /// 清理枚举数。
            /// </summary>
            public void Dispose()
            {
                m_Enumerator.Dispose();
            }

            /// <summary>
            /// 重置枚举数。
            /// </summary>
            void IEnumerator.Reset()
            {
                ((IEnumerator<T>)m_Enumerator).Reset();
            }
        }

        #endregion
    }
}
