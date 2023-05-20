//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;

namespace GameFramework
{
    /// <summary>
    /// 变量。
    /// PS：虽然“基类Variable”中的内容完全可以写在“Variable<T>”中，但为了方便外部不用关注“泛型T”，这里单独创建“Variable”基类
    ///    因此在声明该类型变量时，直接使用“Variable”即可
    /// 注意：如果对”泛型T“没有要求，则不需要添加”Where T ：xx“语句
    /// </summary>
    /// <typeparam name="T">变量类型。</typeparam>
    public abstract class Variable<T> : Variable
    {
        private T m_Value;

        /// <summary>
        /// 初始化变量的新实例。
        /// </summary>
        public Variable()
        {
            m_Value = default(T);
        }

        /// <summary>
        /// 获取或设置变量值。
        /// PS：外部在获取或设置本类型变量的值时，可以直接通过本属性来执行，其内部会自动调用“隐式转换方法”
        /// </summary>
        public T Value
        {
            get
            {
                return m_Value;
            }
            set
            {
                m_Value = value;
            }
        }

        /// <summary>
        /// 清理变量值。
        /// </summary>
        public override void Clear()
        {
            m_Value = default(T);
        }

        /// <summary>
        /// 获取变量字符串。
        /// </summary>
        /// <returns>变量字符串。</returns>
        public override string ToString()
        {
            //如果该T为”值类型“，则必然不为null，因为”值类型都有默认赋值“；如果该T为”引用类型“，则可直接使用”null“进行比较
            //因此该语句是可以正常使用的
            return (m_Value != null) ? m_Value.ToString() : "<Null>";
        }

        /// <summary>
        /// 获取变量类型。
        /// </summary>
        public override Type Type
        {
            get
            {
                return typeof(T);
            }
        }

        #region 由于每个扩展子类型都重写了“implict operator”方法，因此外部可以直接通过“Value”属性实现“GetValue”和“SetValue”的操作，这两个方法其实无用了
        /// <summary>
        /// 获取变量值。
        /// </summary>
        /// <returns>变量值。</returns>
        public override object GetValue()
        {
            return m_Value;
        }

        /// <summary>
        /// 设置变量值。
        /// </summary>
        /// <param name="value">变量值。</param>
        public override void SetValue(object value)
        {
            m_Value = (T)value;
        }

        #endregion

    }
}
