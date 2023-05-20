//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.Runtime.InteropServices;

namespace GameFramework.Resource
{
    /// <summary>
    /// 单机模式版本资源列表。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public partial struct PackageVersionList
    {
        //这里声明的四个变量其实就是用作“常数”的，没啥特别的。从以下逻辑来看，完全可以在“构造方法”中创建，而不用在这里做成“全局变量”增加“复杂性”
        private static readonly Asset[] EmptyAssetArray = new Asset[] { };
        private static readonly Resource[] EmptyResourceArray = new Resource[] { };
        private static readonly FileSystem[] EmptyFileSystemArray = new FileSystem[] { };
        private static readonly ResourceGroup[] EmptyResourceGroupArray = new ResourceGroup[] { };

        //“单机模式版本资源列表”包含的参数
        private readonly bool m_IsValid;
        private readonly string m_ApplicableGameVersion;
        private readonly int m_InternalResourceVersion;
        private readonly Asset[] m_Assets;  //所有的“Asset”
        private readonly Resource[] m_Resources;  //所有的“AssetBundle”
        private readonly FileSystem[] m_FileSystems;
        private readonly ResourceGroup[] m_ResourceGroups; //所有的“AssetBundle”分组的集合

        /// <summary>
        /// 初始化单机模式版本资源列表的新实例。
        /// </summary>
        /// <param name="applicableGameVersion">适配的游戏版本号。</param>
        /// <param name="internalResourceVersion">内部资源版本号。</param>
        /// <param name="assets">包含的资源集合。</param>
        /// <param name="resources">包含的资源集合。</param>
        /// <param name="fileSystems">包含的文件系统集合。</param>
        /// <param name="resourceGroups">包含的资源组集合。</param>
        public PackageVersionList(string applicableGameVersion, int internalResourceVersion, Asset[] assets, Resource[] resources, FileSystem[] fileSystems, ResourceGroup[] resourceGroups)
        {
            m_IsValid = true; //当使用“构造方法”创建该类型实例对象时，默认“m_IsValid”变量为“true”
            m_ApplicableGameVersion = applicableGameVersion;
            m_InternalResourceVersion = internalResourceVersion;
            m_Assets = assets ?? EmptyAssetArray;
            m_Resources = resources ?? EmptyResourceArray;
            m_FileSystems = fileSystems ?? EmptyFileSystemArray;
            m_ResourceGroups = resourceGroups ?? EmptyResourceGroupArray;
        }

        /// <summary>
        /// 获取单机模式版本资源列表是否有效。
        /// </summary>
        public bool IsValid
        {
            get
            {
                return m_IsValid;
            }
        }

        /// <summary>
        /// 获取适配的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion
        {
            get
            {
                if (!m_IsValid)
                {
                    throw new GameFrameworkException("Data is invalid.");
                }

                return m_ApplicableGameVersion;
            }
        }

        /// <summary>
        /// 获取内部资源版本号。
        /// </summary>
        public int InternalResourceVersion
        {
            get
            {
                if (!m_IsValid)
                {
                    throw new GameFrameworkException("Data is invalid.");
                }

                return m_InternalResourceVersion;
            }
        }

        /// <summary>
        /// 获取包含的资源集合。
        /// </summary>
        /// <returns>包含的资源集合。</returns>
        public Asset[] GetAssets()
        {
            if (!m_IsValid)
            {
                throw new GameFrameworkException("Data is invalid.");
            }

            return m_Assets;
        }

        /// <summary>
        /// 获取包含的资源集合。
        /// </summary>
        /// <returns>包含的资源集合。</returns>
        public Resource[] GetResources()
        {
            if (!m_IsValid)
            {
                throw new GameFrameworkException("Data is invalid.");
            }

            return m_Resources;
        }

        /// <summary>
        /// 获取包含的文件系统集合。
        /// </summary>
        /// <returns>包含的文件系统集合。</returns>
        public FileSystem[] GetFileSystems()
        {
            if (!m_IsValid)
            {
                throw new GameFrameworkException("Data is invalid.");
            }

            return m_FileSystems;
        }

        /// <summary>
        /// 获取包含的资源组集合。
        /// </summary>
        /// <returns>包含的资源组集合。</returns>
        public ResourceGroup[] GetResourceGroups()
        {
            if (!m_IsValid)
            {
                throw new GameFrameworkException("Data is invalid.");
            }

            return m_ResourceGroups;
        }
    }
}
