//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 资源初始化器。
        /// </summary>
        private sealed class ResourceIniter
        {
            private readonly ResourceManager m_ResourceManager;
            private readonly Dictionary<ResourceName, string> m_CachedFileSystemNames;
            private string m_CurrentVariant;

            public GameFrameworkAction ResourceInitComplete;

            /// <summary>
            /// 初始化资源初始化器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public ResourceIniter(ResourceManager resourceManager)
            {
                m_ResourceManager = resourceManager;
                m_CachedFileSystemNames = new Dictionary<ResourceName, string>();
                m_CurrentVariant = null;

                ResourceInitComplete = null;
            }

            /// <summary>
            /// 关闭并清理资源初始化器。
            /// </summary>
            public void Shutdown()
            {
            }

            /// <summary>
            /// 初始化资源。
            /// </summary>
            public void InitResources(string currentVariant)
            {
                m_CurrentVariant = currentVariant;

                if (m_ResourceManager.m_ResourceHelper == null)
                {
                    throw new GameFrameworkException("Resource helper is invalid.");
                }

                if (string.IsNullOrEmpty(m_ResourceManager.m_ReadOnlyPath))
                {
                    throw new GameFrameworkException("Read-only path is invalid.");
                }

                //你确定这里应该用“远程服务器版本文件名”吗？这明显是“streamignAssetPath”中代表本地已经有的文件的列表，
                //这文件名不应该是“GameFrameworkList.dat”吗？
                var originalPath = Path.Combine(m_ResourceManager.m_ReadOnlyPath, RemoteVersionListFileName);
                var remotePath = Utility.Path.GetRemotePath(originalPath);
                Debug.LogFormat("originalPath: {0}, remotePath: {1}", originalPath, remotePath);
                //"m_ReadOnlyPath"为“streamingAssets路径”
                m_ResourceManager.m_ResourceHelper.LoadBytes(
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_ReadOnlyPath, RemoteVersionListFileName)),
                    new LoadBytesCallbacks(OnLoadPackageVersionListSuccess, OnLoadPackageVersionListFailure),
                    null);
            }

            private void OnLoadPackageVersionListSuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                MemoryStream memoryStream = null;
                try
                {
                    memoryStream = new MemoryStream(bytes, false);
                    //使用非常牛逼plus的“DeserializeCallback解析得到完整的包含“Resource/Asset/ResourcGroup”类型变量的“PackageVersionList”变量
                    PackageVersionList versionList = m_ResourceManager.m_PackageVersionListSerializer.Deserialize(memoryStream);
                    if (!versionList.IsValid)  //默认在构造方法中设置其为true
                    {
                        throw new GameFrameworkException("Deserialize package version list failure.");
                    }

                    PackageVersionList.Asset[] assets = versionList.GetAssets();
                    PackageVersionList.Resource[] resources = versionList.GetResources();
                    //"单机模式"的“fileSystem”默认为null
                    PackageVersionList.FileSystem[] fileSystems = versionList.GetFileSystems();
                    PackageVersionList.ResourceGroup[] resourceGroups = versionList.GetResourceGroups();
                    m_ResourceManager.m_ApplicableGameVersion = versionList.ApplicableGameVersion;
                    m_ResourceManager.m_InternalResourceVersion = versionList.InternalResourceVersion;
                    //所有“Asset”的集合
                    m_ResourceManager.m_AssetInfos = new Dictionary<string, AssetInfo>(assets.Length, StringComparer.Ordinal);
                    //所有“Resource”的集合，其容量为上述的“resources”变量的length，但内容还需要封装成“ResourceInfo”才能加入进去
                    m_ResourceManager.m_ResourceInfos = new Dictionary<ResourceName, ResourceInfo>(resources.Length, new ResourceNameComparer());
                    //创建一个默认的“ResourceGroup”，该分组内包含所有的“Resource”信息
                    //PS：从后续逻辑来看，完全没发现该变量的作用，应该可以省略掉
                    ResourceGroup defaultResourceGroup = m_ResourceManager.GetOrAddResourceGroup(string.Empty);

                    foreach (PackageVersionList.FileSystem fileSystem in fileSystems)
                    {
                        int[] resourceIndexes = fileSystem.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            PackageVersionList.Resource resource = resources[resourceIndex];
                            if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                            {
                                continue;
                            }

                            m_CachedFileSystemNames.Add(new ResourceName(resource.Name, resource.Variant, resource.Extension), fileSystem.Name);
                        }
                    }

                    foreach (PackageVersionList.Resource resource in resources)
                    {
                        //使用“UnityWebRequest”下载下来的bytes数组数据包含所有“变体版本”的资源
                        //而这里只需要根据当前设定，加载指定“变体版本”的“Resource”信息
                        if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                        {
                            continue;
                        }

                        //"AssetBundle"的“ResourceName”
                        ResourceName resourceName = new ResourceName(resource.Name, resource.Variant, resource.Extension);
                        int[] assetIndexes = resource.GetAssetIndexes();
                        foreach (int assetIndex in assetIndexes)
                        {
                            //这操作也是迷之物语，反复的在“index”和“assetName”之间转换，图啥啊，麻烦死了
                            PackageVersionList.Asset asset = assets[assetIndex];
                            int[] dependencyAssetIndexes = asset.GetDependencyAssetIndexes();
                            int index = 0;
                            string[] dependencyAssetNames = new string[dependencyAssetIndexes.Length];
                            foreach (int dependencyAssetIndex in dependencyAssetIndexes)
                            {
                                dependencyAssetNames[index++] = assets[dependencyAssetIndex].Name;
                            }

                            //解析拼装成“AssetInfo”
                            m_ResourceManager.m_AssetInfos.Add(asset.Name, new AssetInfo(asset.Name, resourceName, dependencyAssetNames));
                        }

                        string fileSystemName = null;
                        if (!m_CachedFileSystemNames.TryGetValue(resourceName, out fileSystemName))
                        {
                            fileSystemName = null;
                        }

                        //在此时创建“ResourceInfo”可以赋值“m_StorageReadOnly”和“m_Ready”都为true
                        m_ResourceManager.m_ResourceInfos.Add(resourceName, new ResourceInfo(resourceName, fileSystemName, (LoadType)resource.LoadType, resource.Length, resource.HashCode, resource.Length, true, true));
                        //这里不考虑“压缩”和“未压缩”。向默认的“ResourceGroup”中添加“Resource”信息
                        defaultResourceGroup.AddResource(resourceName, resource.Length, resource.Length);
                    }

                    foreach (PackageVersionList.ResourceGroup resourceGroup in resourceGroups)
                    {
                        //创建正式的“ResourceGroup”分组信息
                        ResourceGroup group = m_ResourceManager.GetOrAddResourceGroup(resourceGroup.Name);
                        int[] resourceIndexes = resourceGroup.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            PackageVersionList.Resource resource = resources[resourceIndex];
                            if (resource.Variant != null && resource.Variant != m_CurrentVariant)
                            {
                                continue;
                            }

                            group.AddResource(new ResourceName(resource.Name, resource.Variant, resource.Extension), resource.Length, resource.Length);
                        }
                    }

                    ResourceInitComplete();
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Parse package version list exception '{0}'.", exception), exception);
                }
                finally
                {
                    m_CachedFileSystemNames.Clear();
                    if (memoryStream != null)
                    {
                        memoryStream.Dispose();
                        memoryStream = null;
                    }
                }
            }

            private void OnLoadPackageVersionListFailure(string fileUri, string errorMessage, object userData)
            {
                throw new GameFrameworkException(Utility.Text.Format("Package version list '{0}' is invalid, error message is '{1}'.", fileUri, string.IsNullOrEmpty(errorMessage) ? "<Empty>" : errorMessage));
            }
        }
    }
}
