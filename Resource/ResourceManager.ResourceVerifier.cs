//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GameFramework.Resource
{
    internal sealed partial class ResourceManager : GameFrameworkModule, IResourceManager
    {
        /// <summary>
        /// 资源校验器。
        /// PS：该脚本主要用于检验”PersistentDataPath“中的记录本地已经下载完成的”版本列表文件GameFrameworkList.dat“
        ///    根据校验的结果：如果有Resource有误，则重新生成“读写区”的"GameFrameworkList.dat"文件
        ///                 如果所有Resource都正确无误，则直接切换到下个流程
        ///    本脚本的主要作用就是：确保“读写区”的“GameFrameworkList.dat”文件一定是正确无误的，仅此而已
        ///    至于该文件当前有没有(如初次进游戏时)并不重要，因为后续的“ResourceUpdater”中会下载资源(此时会自动生成该文件)
        /// </summary>
        private sealed partial class ResourceVerifier
        {
            //该集合中存储所有从“GameFrameworkList.dat”中读取到的”Resource“的信息
            //并且经过校验后，最后保存的都是“正确无误”的Resource信息，有问题的Resource信息都会从该集合中剔除
            private readonly List<VerifyInfo> m_VerifyInfos;
            //扩展：该集合的最终结果是“验证通过的所有正确无误的AssetBundle相应的VerifyInfo”，其“index”编号对应
            //     “LocalVersionList.Resources”中各个Resource编号也是按照“m_VerifyInfos”集合元素顺序而来，
            //     “LocalVersionList.FileSystems”中各个FileSystem中包含的“ResourceIndex”编号也是从“m_VerifyInfos”集合中元素顺序而来

            //代表读取”GameFrameworkList.dat“文件并将其中记录的Resource，fileSystem转换成“VerifyInfo”的过程是否已完成
            private bool m_LoadReadWriteVersionListComplete;

            //加入该参数的主要目的在于：如果没有此参数，那么就会在一帧中遍历完“m_VerifyInfos”集合中的所有“Resource”
            //那么如果集合中元素过多，执行时间必然过长，此时就会出现卡顿
            private int m_VerifyResourceLengthPerFrame;
            private int m_VerifyResourceIndex;

            //当有资源错误时则需要重新生成“本地版本文件”
            private bool m_FailureFlag;

            private const int CachedHashBytesLength = 4;
            //读取HashCode时使用
            private readonly byte[] m_CachedHashBytes;

            //“校验资源”时各种情况的回调：该回调的逻辑注册在”ResourceManager.VerifyResources“方法中
            //在创建”ResourceVerifier“对象时添加相关委托的执行逻辑
            public GameFrameworkAction<int, long> ResourceVerifyStart;
            public GameFrameworkAction<ResourceName, int> ResourceVerifySuccess;
            public GameFrameworkAction<ResourceName> ResourceVerifyFailure;
            public GameFrameworkAction<bool> ResourceVerifyComplete;

            private readonly ResourceManager m_ResourceManager;

            /// <summary>
            /// 初始化资源校验器的新实例。
            /// </summary>
            /// <param name="resourceManager">资源管理器。</param>
            public ResourceVerifier(ResourceManager resourceManager)
            {
                m_ResourceManager = resourceManager;
                m_VerifyInfos = new List<VerifyInfo>();
                m_CachedHashBytes = new byte[CachedHashBytesLength];
                m_LoadReadWriteVersionListComplete = false;
                m_VerifyResourceLengthPerFrame = 0;
                m_VerifyResourceIndex = 0;
                m_FailureFlag = false;

                ResourceVerifyStart = null;
                ResourceVerifySuccess = null;
                ResourceVerifyFailure = null;
                ResourceVerifyComplete = null;
            }

            /// <summary>
            /// 关闭并清理资源校验器。
            /// </summary>
            public void Shutdown()
            {
                //注意：这里只是清除“List”集合中的元素，其实应该再将“m_VerifyInfos”变量赋值为null
                m_VerifyInfos.Clear();
                m_LoadReadWriteVersionListComplete = false;
                m_VerifyResourceLengthPerFrame = 0;
                m_VerifyResourceIndex = 0;
                m_FailureFlag = false;
            }

            #region 第一步：读取本地读写区的“GameFrameworkList.dat”文件，并将其中记录的所有Resource都封装成“VerifyInfo”对象并放入“m_VerifyInfos”集合中，等待“Update轮询”启动“每个VerifyInfo”对象的校验
            /// <summary>
            /// 提供给外部开启“校验资源”的过程
            /// </summary>
            /// <param name="verifyResourceLengthPerFrame">每帧至少校验资源的大小，以字节为单位。</param>
            public void VerifyResources(int verifyResourceLengthPerFrame)
            {
                if (verifyResourceLengthPerFrame < 0)
                {
                    throw new GameFrameworkException("Verify resource count per frame is invalid.");
                }

                if (m_ResourceManager.m_ResourceHelper == null)
                {
                    throw new GameFrameworkException("Resource helper is invalid.");
                }

                if (string.IsNullOrEmpty(m_ResourceManager.m_ReadWritePath))
                {
                    throw new GameFrameworkException("Read-write path is invalid.");
                }

                m_VerifyResourceLengthPerFrame = verifyResourceLengthPerFrame;
                //1.这里使用”LoadBytes“，而没有使用”AddWebRequest“，主要是因为此时”加载的路径“为”本地路径(以’file:///‘为前缀)“
                //  由于加载本地路径其实很快，不需要使用“TaskPool”建立“WebRequestTask”来下载“远程服务器上的文件，如Version.txt等”
                //  因此这里直接使用“LoadBytes”即可
                //2.这里读取的是“记录本地已经下载完成的资源文件列表GameFrameworkList.dat”，而不是“代表远程服务器上最新资源的列表GameFrameworkVersion.dat”
                //  其主要目的在于通过检测该“GameFrameworkList.dat”文件中的数据来确定当前还有哪些资源需要下载
                //注意：如果本地读写区PersistentDataPath中没有“GameFrameworkList.dat”(例如第一次启动游戏时)
                //     此时会直接切换到“ProcedureCheckResources”流程中
                m_ResourceManager.m_ResourceHelper.LoadBytes(
                    Utility.Path.GetRemotePath(Path.Combine(m_ResourceManager.m_ReadWritePath, LocalVersionListFileName)),
                    new LoadBytesCallbacks(OnLoadReadWriteVersionListSuccess, OnLoadReadWriteVersionListFailure),
                    null);
            }

            private void OnLoadReadWriteVersionListSuccess(string fileUri, byte[] bytes, float duration, object userData)
            {
                Debug.LogFormat("load localVersionList success");
                MemoryStream memoryStream = null;
                try
                {
                    //从“GameFrameworkList.dat”中封装成的“memoryStream”主要用于检测鉴别使用，因此不具有“写入Writable”的功能
                    memoryStream = new MemoryStream(bytes, false);
                    //根据当前框架执行逻辑，由于序列化“LocalVersionList”对象时使用的“序列化回调版本号”为最新的，因此这里“反序列化”时也会使用相应版本
                    LocalVersionList versionList = m_ResourceManager.m_ReadWriteVersionListSerializer.Deserialize(memoryStream);
                    if (!versionList.IsValid)
                    {
                        throw new GameFrameworkException("Deserialize read write version list failure.");
                    }

                    //获取其中记录的“Resource“以及”FileSystem“信息
                    LocalVersionList.Resource[] resources = versionList.GetResources();
                    LocalVersionList.FileSystem[] fileSystems = versionList.GetFileSystems();
                    //1.创建Resource与其所属的FileSystem之间的对应关系，以ResourceName为key
                    Dictionary<ResourceName, string> resourceInFileSystemNames = new Dictionary<ResourceName, string>();
                    foreach (LocalVersionList.FileSystem fileSystem in fileSystems)
                    {
                        //该”fileSytem“中包含的”AssetBundle“的编号，该编号对应上述“resources”数组中的编号
                        int[] resourceIndexes = fileSystem.GetResourceIndexes();
                        foreach (int resourceIndex in resourceIndexes)
                        {
                            //这里的“resourceIndex”对应上述“resources”数组中的编号
                            LocalVersionList.Resource resource = resources[resourceIndex];
                            //将每个”resourceIndex“对应的”Resource“转换成”ResourceName“的形式存储到”resourceInFileSystemNames“集合中
                            resourceInFileSystemNames.Add(new ResourceName(resource.Name, resource.Variant, resource.Extension),
                                fileSystem.Name);
                            //注意：该集合的作用在于可以直接根据该”resourceName“，查找到其对应的”fileSystem“
                            //     之前的”fileSystems“集合中只知道”每个fileSystem“对象中包含哪些Resource
                            //     不方便在以”ResourceName“为标准的后续逻辑中查找
                            //     所以这里将其转换下。但是如果从这种角度看，命名是不是应该为”fileSystemNameOfResource“
                        }
                    }

                    //2.创建每个Resource对应的"VerifyInfo"对象，放入集合“m_VerifyInfos”中
                    long totalLength = 0L;
                    foreach (LocalVersionList.Resource resource in resources)
                    {
                        ResourceName resourceName = new ResourceName(resource.Name, resource.Variant, resource.Extension);
                        string fileSystemName = null;
                        //从这一步也可以看出”resouceInFileSystems“的真正作用：为了方便查找每个”Resource“对应的”fileSystem“
                        resourceInFileSystemNames.TryGetValue(resourceName, out fileSystemName);
                        totalLength += resource.Length;
                        //把“GameFrameworkList.dat”记录到的所有“Resource”放入“m_VerifyInfos”集合中，后续会马上开始校验这些Resource对象
                        m_VerifyInfos.Add(new VerifyInfo(resourceName, fileSystemName,
                            (LoadType)resource.LoadType, resource.Length, resource.HashCode));
                    }

                    //由于只有在读取“GameFrameworkList.dat”成功并解析其中的“Resource”和“FileSystem”完全结束后
                    //才会设置“m_LoadReadWriteVersionListComplete”参数，因此不存在“边往m_VerifyInfos中添加元素边校验的情况”
                    //因为有的Resource校验不通过会被移除m_VerifyInfos集合中
                    m_LoadReadWriteVersionListComplete = true;
                    if (ResourceVerifyStart != null)
                    {
                        ResourceVerifyStart(m_VerifyInfos.Count, totalLength);
                    }
                }
                catch (Exception exception)
                {
                    if (exception is GameFrameworkException)
                    {
                        throw;
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Parse read-write version list exception '{0}'.", exception), exception);
                }
                finally
                {
                    //释放该stream
                    if (memoryStream != null)
                    {
                        memoryStream.Dispose();
                        memoryStream = null;
                    }
                }
            }

            //初始时，本地读写区“PerisistentDataPath”没有“GameFrameworkList.dat”文件，此时必然会执行本回调
            //但是并不影响整体流程。因为本脚本的作用仅仅只是检测“本地已保存的Resources信息是否正确”而已
            //有没有保存的Resources并不重要，说明不需要校验，直接执行下一步即可
            //对于“m_VerifyInfos”则不用考虑，因为其为“private”变量
            //在这种情况下，不会生成“GameFrameworkList.dat”文件，该文件会在“ResourceUpdater”中下载资源时再生成
            private void OnLoadReadWriteVersionListFailure(string fileUri, string errorMessage, object userData)
            {
                if (ResourceVerifyComplete != null)
                {
                    Debug.LogFormat("ResourceVerifier load localVersionList failure");
                    ResourceVerifyComplete(true);
                }
            }
            #endregion

            #region 第二步：在“Update轮询”中开始校验“m_VerifyInos”集合中的所有VerifyInfo对象
            /// <summary>
            /// 资源校验器轮询。
            /// PS：在创建该对象时，“Update”就一直在执行，只是如果不是进入到特定的流程“ProcedureVerifyResources”流程
            ///    或者调用“VerifyResource”方法后，都会因为“m_LoadReadWriteVersionListComplete”被阻挡
            ///    只有在读取“GameFrameworkList.dat”文件成功后才会改变该参数值
            /// </summary>
            /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
            /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                //如果”GameFrameworkList.dat“文件没有加载完成(包含该文件不存在的情况)，则无需执行后续逻辑
                if (!m_LoadReadWriteVersionListComplete)
                {
                    return;
                }

                //每一帧都重置该数值，以确定当前帧是否已达到“m_VerifyResourceLengthPerFrame”限制
                int length = 0;
                //依次校验“m_VerifyInfos”集合中的所有Resources
                while (m_VerifyResourceIndex < m_VerifyInfos.Count)
                {
                    VerifyInfo verifyInfo = m_VerifyInfos[m_VerifyResourceIndex];
                    //不论验证结果如何，“m_VerifyResourceLengthPerFrame”代表的是验证的限制，
                    //只要执行了“VerifyResource”方法就需要递增
                    length += verifyInfo.Length;
                    if (VerifyResource(verifyInfo))
                    {
                        m_VerifyResourceIndex++;
                        if (ResourceVerifySuccess != null)
                        {
                            ResourceVerifySuccess(verifyInfo.ResourceName, verifyInfo.Length);
                        }
                    }
                    else
                    {
                        m_FailureFlag = true;
                        //很重要：未通过校验的Resource会从“m_VerifyInfos”集合中移除
                        //所以最后校验结束时，“m_VerifyInfos”中遗留的都是“正确无误通过校验”的Resource信息
                        m_VerifyInfos.RemoveAt(m_VerifyResourceIndex);
                        if (ResourceVerifyFailure != null)
                        {
                            ResourceVerifyFailure(verifyInfo.ResourceName);
                        }
                    }

                    //不管验证成功还是失败，本帧都已经做了“验证工作”，因此“length”变量在“验证初始时就已递增”
                    if (length >= m_VerifyResourceLengthPerFrame)
                    {
                        //本帧执行完毕，开始下一帧的执行，避免同一帧执行过多导致卡顿的情况
                        return;
                    }
                }

                //这个参数有点莫名奇妙：之前代表读取“GameFrameworkList.dat”文件完成，这里又是代表什么？
                //应该“新声明一个参数，代表校验完成”，而不应该反复用一个参数
                m_LoadReadWriteVersionListComplete = false;

                //只要有一个Resource校验失败，那么就需要重新生成本地的“GameFrameworkList.dat”文件
                if (m_FailureFlag)
                {
                    GenerateReadWriteVersionList();
                }

                if (ResourceVerifyComplete != null)
                {
                    ResourceVerifyComplete(!m_FailureFlag);
                }
            }

            //仅脚本内部使用，用于校验“m_VerifyInfos”集合中的每个“Resource”封装成的“VerifyInfo”
            private bool VerifyResource(VerifyInfo verifyInfo)
            {
                //根据现有的逻辑：每个Resource下载完成后会检测其是否配置了“FileSystem”
                //如果该Resource配置了“fileSystem”，那么就会将该Resource写入指定的FileSystem中
                //并同时删除本地已经保存好的“Resource”文件
                if (verifyInfo.UseFileSystem)
                {
                    //这里的“FileSytem”和“LocalVersionList”中新声明的“FileSytem”是完全不同的
                    //其实理论上来讲“LocalVersionList”中是不应该命名“FileSytem”的，太容易混淆了
                    //这里只校验“读写区”的AB文件，所以“storageInReadOnly”为false
                    IFileSystem fileSystem = m_ResourceManager.GetFileSystem(verifyInfo.FileSystemName, false);
                    string fileName = verifyInfo.ResourceName.FullName;
                    //从“配置的文件系统”中获取该“Resource”的数据信息(该信息已经封装成了FileInfo对象)
                    FileSystem.FileInfo fileInfo = fileSystem.GetFileInfo(fileName);
                    if (!fileInfo.IsValid)
                    {
                        return false;
                    }

                    //由于是读取本地下载好的AB文件，因此这里得到的应该是“解压缩之后的Length”，而非“compressedLength”
                    int length = fileInfo.Length;
                    if (length == verifyInfo.Length)
                    {
                        //重置cachedStream以供后面写入数据
                        m_ResourceManager.PrepareCachedStream();
                        //从“目标文件系统”中读取“目标Resource”的信息到“缓存流cachedStream”中
                        fileSystem.ReadFile(fileName, m_ResourceManager.m_CachedStream);
                        //设置初始位置，方便后续读取stream中的数据
                        m_ResourceManager.m_CachedStream.Position = 0L;
                        int hashCode = 0;
                        //如果是需要解密的类型
                        if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt
                            || verifyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                        {
                            //“hashcode”是十进制的int数值
                            Utility.Converter.GetBytes(verifyInfo.HashCode, m_CachedHashBytes);
                            if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt)
                            {
                                hashCode = Utility.Verifier.GetCrc32(m_ResourceManager.m_CachedStream, m_CachedHashBytes, Utility.Encryption.QuickEncryptLength);
                            }
                            else if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                            {
                                hashCode = Utility.Verifier.GetCrc32(m_ResourceManager.m_CachedStream, m_CachedHashBytes, length);
                            }

                            Array.Clear(m_CachedHashBytes, 0, CachedHashBytesLength);
                        }
                        else
                        {
                            hashCode = Utility.Verifier.GetCrc32(m_ResourceManager.m_CachedStream);
                        }

                        //这里“hashcode”的校验有点东西，暂时没有看懂，估计需要先看“ResourceBuilder”中打包时为每个Resource
                        //生成hashcode的逻辑才行
                        //PS：每个Resource的length/hashcode等信息都会存储在“GameFrameworkVersion.dat”文件中
                        //   而该文件是在打包时一起生成的，之后上传到服务器上，所以应该在“打包逻辑”里查看“每个Resource的hashcode生成逻辑”
                        if (hashCode == verifyInfo.HashCode)
                        {
                            return true;
                        }
                    }

                    //如果length不一致，则说明下载未完成或错误，此时直接移除已经存储到“目标fileSystem”中的该Resouce的数据
                    //并返回false，代表该Resource校验未通过
                    fileSystem.DeleteFile(fileName);
                    return false;
                }
                else
                {
                    //如果没有使用“fileSystem”，则该Resource下载完成后会直接在“读写区”存储下来
                    string resourcePath = Utility.Path.GetRegularPath(Path.Combine(m_ResourceManager.ReadWritePath, verifyInfo.ResourceName.FullName));
                    //如果“读写区”不存在该Resource，则代表“校验失败”
                    if (!File.Exists(resourcePath))
                    {
                        return false;
                    }

                    using (FileStream fileStream = new FileStream(resourcePath, FileMode.Open, FileAccess.Read))
                    {
                        int length = (int)fileStream.Length;
                        if (length == verifyInfo.Length)
                        {
                            int hashCode = 0;
                            //针对加密类型文件的“hasncode”计算方式应该可以“提取出个通用方法”来
                            if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt
                                || verifyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                            {
                                Utility.Converter.GetBytes(verifyInfo.HashCode, m_CachedHashBytes);
                                if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndQuickDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndQuickDecrypt)
                                {
                                    //这里其实和上述的“m_ResourceManager.m_CachedStream”是一样的作用
                                    //只是这里可以直接读取“resourcePath”的文件得到FileStream，
                                    //而上面需要通过“FileSystem”来获取到该Resource的FileStream
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes, Utility.Encryption.QuickEncryptLength);
                                }
                                else if (verifyInfo.LoadType == LoadType.LoadFromMemoryAndDecrypt || verifyInfo.LoadType == LoadType.LoadFromBinaryAndDecrypt)
                                {
                                    hashCode = Utility.Verifier.GetCrc32(fileStream, m_CachedHashBytes, length);
                                }

                                //因为“hashCode”已经得到，所以清除该“m_CachedHashBytes”数组即可
                                Array.Clear(m_CachedHashBytes, 0, CachedHashBytesLength);
                            }
                            else
                            {
                                hashCode = Utility.Verifier.GetCrc32(fileStream);
                            }

                            if (hashCode == verifyInfo.HashCode)
                            {
                                return true;
                            }
                        }
                    }

                    //如果上面没有执行到“return true”的语句，则说明该Resource的校验不通过，所有需要删除“读写区”该Resource文件
                    //这里直接用“File.Delete”即可
                    File.Delete(resourcePath);
                    return false;
                }
            }
            #endregion

            #region 第三步：校验所有“VerifyInfo“结束后，根据”校验结果“确定是否重写“本地读写区的版本文件GameFrameworkList.dat”
            private void GenerateReadWriteVersionList()
            {
                //虽然这里有两个路径，但都是在“PersistentDataPath”下的文件。整个项目的读写区除了特殊情况之外都是“PersistentDataPath”
                string readWriteVersionListFileName = Utility.Path.GetRegularPath(
                    Path.Combine(m_ResourceManager.m_ReadWritePath, LocalVersionListFileName));
                //在原有名字的基础上再次添加后缀“tmp”
                string readWriteVersionListTempFileName = Utility.Text.Format("{0}.{1}",
                    readWriteVersionListFileName, TempExtension);
                SortedDictionary<string, List<int>> cachedFileSystemsForGenerateReadWriteVersionList
                    = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
                FileStream fileStream = null;
                try
                {
                    //创建“本地版本列表GameFrameworkList.dat”的临时文件，开启“Write”权限
                    fileStream = new FileStream(readWriteVersionListTempFileName, FileMode.Create, FileAccess.Write);
                    //验证完毕，所有“正确无误”的“Resource”的信息集合“m_VerifyInfos”
                    LocalVersionList.Resource[] resources = m_VerifyInfos.Count > 0 ? new LocalVersionList.Resource[m_VerifyInfos.Count] : null;
                    if (resources != null)
                    {
                        int index = 0;
                        //该“index”对应最终“m_VerifyInfos”集合中的各个“verifyInfo”的编号
                        foreach (VerifyInfo i in m_VerifyInfos)
                        {
                            //创建新的Resource对象，并不使用原有的“LocalVersionList”中的“Resource”对象，这样更安全，更方便
                            resources[index] = new LocalVersionList.Resource(i.ResourceName.Name, i.ResourceName.Variant,
                                i.ResourceName.Extension, (byte)i.LoadType, i.Length, i.HashCode);
                            if (i.UseFileSystem)
                            {
                                List<int> resourceIndexes = null;
                                //1.由于在封装“VerifyInfo”对象时就已经获取了该Resource的FileSystem，因此这里可以直接使用
                                //2.将用到FileSystem的Resource进行统计，但根据“fileSytemName”来划分所有的Resource
                                if (!cachedFileSystemsForGenerateReadWriteVersionList.TryGetValue(i.FileSystemName, out resourceIndexes))
                                {
                                    resourceIndexes = new List<int>();
                                    cachedFileSystemsForGenerateReadWriteVersionList.Add(i.FileSystemName, resourceIndexes);
                                }

                                //这里的“index”是对应“最终的m_VerifyInfos”集合中的顺序编号
                                resourceIndexes.Add(index);
                            }

                            index++;
                        }
                    }

                    //根据上述统计完成的“FileSytem”信息，生成相应的“FileSytem”对象
                    //“LocalVersionList”中新创建的类型“FileSystem”其实很简单，与正式的“FileSystem模块”中的不一样
                    LocalVersionList.FileSystem[] fileSystems = cachedFileSystemsForGenerateReadWriteVersionList.Count > 0
                        ? new LocalVersionList.FileSystem[cachedFileSystemsForGenerateReadWriteVersionList.Count] : null;
                    if (fileSystems != null)
                    {
                        int index = 0;
                        foreach (KeyValuePair<string, List<int>> i in cachedFileSystemsForGenerateReadWriteVersionList)
                        {
                            fileSystems[index++] = new LocalVersionList.FileSystem(i.Key, i.Value.ToArray());
                            i.Value.Clear();
                        }
                    }

                    //localVersionList中的FileSytem啥也不是
                    LocalVersionList versionList = new LocalVersionList(resources, fileSystems);
                    //这傻逼的，如果“fileStream”对象只开始在这里用到，那为什么要定义的这么远，把“fileStream”移到这里来啊
                    //真费劲！！！
                    if (!m_ResourceManager.m_ReadWriteVersionListSerializer.Serialize(fileStream, versionList))
                    {
                        throw new GameFrameworkException("Serialize read-write version list failure.");
                    }

                    //“临时本地版本文件”写入完毕，因此关闭该stream
                    fileStream.Dispose();
                    fileStream = null;
                }
                catch (Exception exception)
                {
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                        fileStream = null;
                    }

                    //若出现异常，则删除“临时本地版本文件”。其时不删除也没关系。因为该“FileStream”始终使用“FileMode.Create”
                    //所以如果再次执行创建该FileStream会将本地已经存在的同名文件删除掉
                    if (File.Exists(readWriteVersionListTempFileName))
                    {
                        File.Delete(readWriteVersionListTempFileName);
                    }

                    throw new GameFrameworkException(Utility.Text.Format("Generate read-write version list exception '{0}'.", exception), exception);
                }

                //由于真正要用的是“本地版本文件”，而这里又没有“FileMode.Create”，
                //所以检测下，如果本地已经有，就删除掉。同时也是保证后面“File.Move”文件时必然成功，
                //保持“GameFrameworkList.dat”为最新的数据
                if (File.Exists(readWriteVersionListFileName))
                {
                    File.Delete(readWriteVersionListFileName);
                }

                //移动文件：File.Move方法会在目标路径下创建文件，所以内部逻辑应该是会读取“sourcePath”的文件数据的，然后写入“destPath”的文件中
                //并且File.Move方法中，如果destPath已经有同名文件，则会报错，这也是上面要删除原有的“GameFrameworkList.dat”文件的原因
                //否则File.Move会执行失败
                File.Move(readWriteVersionListTempFileName, readWriteVersionListFileName);
            }
            #endregion
        }
    }
}
