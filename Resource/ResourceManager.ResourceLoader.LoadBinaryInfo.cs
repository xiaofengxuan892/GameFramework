//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        private sealed partial class ResourceLoader
        {
            private sealed class LoadBinaryInfo : IReference
            {
                //代表要加载的目标asset：这里的命名其实有点傻逼，该asset所属的AssetBundle是二进制格式，这和该asset有何关系
                //应该将”Binary“关键字放到其所属的”AssetBundle“的命名中
                private string m_BinaryAssetName;
                //该asset所属的”AssetBundle“的信息(其实应该将”Binary”关键字放到这里的命名中，因此该AssetBundle是二进制格式的)
                private ResourceInfo m_ResourceInfo;

                private LoadBinaryCallbacks m_LoadBinaryCallbacks;
                private object m_UserData;  //自定义数据(用于传递某些自定义数据)

                public LoadBinaryInfo()
                {
                    m_BinaryAssetName = null;
                    m_ResourceInfo = null;
                    m_LoadBinaryCallbacks = null;
                    m_UserData = null;
                }

                public string BinaryAssetName
                {
                    get
                    {
                        return m_BinaryAssetName;
                    }
                }

                public ResourceInfo ResourceInfo
                {
                    get
                    {
                        return m_ResourceInfo;
                    }
                }

                public LoadBinaryCallbacks LoadBinaryCallbacks
                {
                    get
                    {
                        return m_LoadBinaryCallbacks;
                    }
                }

                public object UserData
                {
                    get
                    {
                        return m_UserData;
                    }
                }

                public static LoadBinaryInfo Create(string binaryAssetName, ResourceInfo resourceInfo, LoadBinaryCallbacks loadBinaryCallbacks, object userData)
                {
                    LoadBinaryInfo loadBinaryInfo = ReferencePool.Acquire<LoadBinaryInfo>();
                    loadBinaryInfo.m_BinaryAssetName = binaryAssetName;
                    loadBinaryInfo.m_ResourceInfo = resourceInfo;
                    loadBinaryInfo.m_LoadBinaryCallbacks = loadBinaryCallbacks;
                    loadBinaryInfo.m_UserData = userData;
                    return loadBinaryInfo;
                }

                public void Clear()
                {
                    m_BinaryAssetName = null;
                    m_ResourceInfo = null;
                    m_LoadBinaryCallbacks = null;
                    m_UserData = null;
                }
            }
        }
    }
}
