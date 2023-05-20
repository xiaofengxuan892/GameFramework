//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.Runtime.InteropServices;

namespace GameFramework.Config
{
    internal sealed partial class ConfigManager : GameFrameworkModule, IConfigManager
    {
        //这里和“EditorResourceComponent”中用到的“Struct”数据“LoadAssetInfo”，“LoadSceneInfo”一样
        //完全可以将其直接放置到“ConfigManager.cs”内部，不需要重新创建新脚本，减少文件数量
        [StructLayout(LayoutKind.Auto)]
        private struct ConfigData
        {
            private readonly bool m_BoolValue;
            private readonly int m_IntValue;
            private readonly float m_FloatValue;
            private readonly string m_StringValue;

            public ConfigData(bool boolValue, int intValue, float floatValue, string stringValue)
            {
                m_BoolValue = boolValue;
                m_IntValue = intValue;
                m_FloatValue = floatValue;
                m_StringValue = stringValue;
            }

            public bool BoolValue
            {
                get
                {
                    return m_BoolValue;
                }
            }

            public int IntValue
            {
                get
                {
                    return m_IntValue;
                }
            }

            public float FloatValue
            {
                get
                {
                    return m_FloatValue;
                }
            }

            public string StringValue
            {
                get
                {
                    return m_StringValue;
                }
            }
        }
    }
}
