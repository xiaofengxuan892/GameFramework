//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 资源名称。
        /// </summary>
        [StructLayout(LayoutKind.Auto)]
        private struct ResourceName : IComparable, IComparable<ResourceName>, IEquatable<ResourceName>
        {
            private readonly string m_Name;
            private readonly string m_Variant;
            private readonly string m_Extension;

            //该参数的作用非常有限：主要因为其为static修饰，因此所有的“ResourceName”变量都可以通过该参数获取到包含所有“ResourceName变量”的集合“s_ResourceNames”
            //本质上在创建新的ResourceName变量前，应该在本集合中查找是否已经有该元素。但由于本集合中以“ResourceName对象”为key，却以“该ResourceName对象的FullName”为value
            //这其实已经颠倒了，应该反过来才能达到此效果。
            //同一FullName的ResourceName可能有多个，但每个ResourceName必然只有一个FullName
            //所以即使是这样的目标，本集合也无法实现
            //总结：该参数完全可以作废，直接删除
            private static readonly Dictionary<ResourceName, string> s_ResourceFullNames = new Dictionary<ResourceName, string>();

            /// <summary>
            /// 初始化资源名称的新实例。
            /// </summary>
            /// <param name="name">资源名称。</param>
            /// <param name="variant">变体名称。</param>
            /// <param name="extension">扩展名称。</param>
            public ResourceName(string name, string variant, string extension)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new GameFrameworkException("Resource name is invalid.");
                }

                if (string.IsNullOrEmpty(extension))
                {
                    throw new GameFrameworkException("Resource extension is invalid.");
                }

                m_Name = name;
                m_Variant = variant;
                m_Extension = extension;
            }

            #region 核心重写方法
            public override bool Equals(object obj)
            {
                return (obj is ResourceName) && Equals((ResourceName)obj);
            }

            public bool Equals(ResourceName value)
            {
                return string.Equals(m_Name, value.m_Name, StringComparison.Ordinal)
                       && string.Equals(m_Variant, value.m_Variant, StringComparison.Ordinal)
                       && string.Equals(m_Extension, value.m_Extension, StringComparison.Ordinal);
            }

            public static bool operator ==(ResourceName a, ResourceName b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(ResourceName a, ResourceName b)
            {
                return !(a == b);
            }

            public int CompareTo(object value)
            {
                if (value == null)
                {
                    return 1;
                }

                if (!(value is ResourceName))
                {
                    throw new GameFrameworkException("Type of value is invalid.");
                }

                return CompareTo((ResourceName)value);
            }

            //“string.CompareOrdinal”方法会直接返回两个对象的比较结果，用于两个ResourceName对象的排序
            public int CompareTo(ResourceName resourceName)
            {
                //对“AssetBundle”的“m_Name/m_Variant/m_Extension”如此的“重视排序”主要是为什么功能使用的？？
                int result = string.CompareOrdinal(m_Name, resourceName.m_Name);
                if (result != 0)
                {
                    return result;
                }

                result = string.CompareOrdinal(m_Variant, resourceName.m_Variant);
                if (result != 0)
                {
                    return result;
                }

                return string.CompareOrdinal(m_Extension, resourceName.m_Extension);
            }

            public override int GetHashCode()
            {
                if (m_Variant == null)
                {
                    return m_Name.GetHashCode() ^ m_Extension.GetHashCode();
                }

                return m_Name.GetHashCode() ^ m_Variant.GetHashCode() ^ m_Extension.GetHashCode();
            }

            public override string ToString()
            {
                return FullName;
            }

            #endregion

            #region 属性
            public string FullName
            {
                get
                {
                    //无论从哪个角度来看，都完全不需要“s_ResourceFullNames”集合
                    //对于任何一个ResourceName对象的“FullName”，应该由其“自身的m_Name, m_Variant, m_Extension”来决定
                    //增加“s_ResourceFullNames”集合完全是多余，可以直接删除即可
                    string fullName = null;
                    if (s_ResourceFullNames.TryGetValue(this, out fullName))
                    {
                        return fullName;
                    }

                    fullName = m_Variant != null ? Utility.Text.Format("{0}.{1}.{2}", m_Name, m_Variant, m_Extension)
                        : Utility.Text.Format("{0}.{1}", m_Name, m_Extension);
                    s_ResourceFullNames.Add(this, fullName);
                    return fullName;
                }
            }

            /// <summary>
            /// 获取资源名称。
            /// </summary>
            public string Name
            {
                get
                {
                    return m_Name;
                }
            }

            /// <summary>
            /// 获取变体名称。
            /// </summary>
            public string Variant
            {
                get
                {
                    return m_Variant;
                }
            }

            /// <summary>
            /// 获取扩展名称。
            /// </summary>
            public string Extension
            {
                get
                {
                    return m_Extension;
                }
            }

            #endregion
        }
    }
}
