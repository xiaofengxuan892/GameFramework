//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Resource;
using System;

namespace GameFramework
{
    /// <summary>
    /// 数据提供者。
    /// PS: 该脚本以”ReadData“, "ParseData"为核心，其他所有元素都依赖此来起作用
    /// 并且"DataProvider"实际只负责“加载资源”的需求，至于“资源加载成功后”是否对数据进行读取和解析，
    /// 则完全交由各个模块相应的“DataProviderHelper”来负责，预留了完全足够的空间
    /// </summary>
    /// <typeparam name="T">数据提供者的持有者的类型。</typeparam>
    internal sealed class DataProvider<T> : IDataProvider<T>
    {
        private const int BlockSize = 1024 * 4;
        private static byte[] s_CachedBytes = null;

        private readonly T m_Owner;
        private IResourceManager m_ResourceManager;
        private IDataProviderHelper<T> m_DataProviderHelper;
        //在加载资源结束时自动执行
        private readonly LoadAssetCallbacks m_LoadAssetCallbacks;
        private readonly LoadBinaryCallbacks m_LoadBinaryCallbacks;
        //根据“DataProviderHelper.ReadData”，即匹配解析数据的结果选择执行不同的委托(外部通过观察者模式访问)
        private EventHandler<ReadDataSuccessEventArgs> m_ReadDataSuccessEventHandler;
        private EventHandler<ReadDataFailureEventArgs> m_ReadDataFailureEventHandler;
        private EventHandler<ReadDataUpdateEventArgs> m_ReadDataUpdateEventHandler;
        private EventHandler<ReadDataDependencyAssetEventArgs> m_ReadDataDependencyAssetEventHandler;

        /// <summary>
        /// 初始化数据提供者的新实例。
        /// </summary>
        /// <param name="owner">数据提供者的持有者。</param>
        public DataProvider(T owner)
        {
            m_Owner = owner;
            m_ResourceManager = null;
            m_DataProviderHelper = null;

            //注意：“LoadAssetCallbacks/LoadBinaryCallbacks”内部的逻辑已经写死
            m_LoadAssetCallbacks = new LoadAssetCallbacks(LoadAssetSuccessCallback, LoadAssetOrBinaryFailureCallback, LoadAssetUpdateCallback, LoadAssetDependencyAssetCallback);
            m_LoadBinaryCallbacks = new LoadBinaryCallbacks(LoadBinarySuccessCallback, LoadAssetOrBinaryFailureCallback);

            m_ReadDataSuccessEventHandler = null;
            m_ReadDataFailureEventHandler = null;
            m_ReadDataUpdateEventHandler = null;
            m_ReadDataDependencyAssetEventHandler = null;
        }

        #region 设置该DataProvider的”m_ResourceManager“以及”m_DataProviderHelper“
        //在加载资源”LoadAsset“时需要借助”m_ResourceManger“,
        //解析加载的资源时部分情况需要借助”m_DataProviderHelper“的”ReadData“或”ParseData“方法
        //因此这里需要先设置该”DataProvider“的相关参数

        /// <summary>
        /// 设置资源管理器。
        /// </summary>
        /// <param name="resourceManager">资源管理器。</param>
        internal void SetResourceManager(IResourceManager resourceManager)
        {
            if (resourceManager == null)
            {
                throw new GameFrameworkException("Resource manager is invalid.");
            }

            m_ResourceManager = resourceManager;
        }

        /// <summary>
        /// 设置数据提供者辅助器。
        /// </summary>
        /// <param name="dataProviderHelper">数据提供者辅助器。</param>
        internal void SetDataProviderHelper(IDataProviderHelper<T> dataProviderHelper)
        {
            if (dataProviderHelper == null)
            {
                throw new GameFrameworkException("Data provider helper is invalid.");
            }

            m_DataProviderHelper = dataProviderHelper;
        }
        #endregion

        #region 使用观察者模式监听数据读取完全结束后的操作
        /// <summary>
        /// 读取数据成功事件。
        /// </summary>
        public event EventHandler<ReadDataSuccessEventArgs> ReadDataSuccess
        {
            add
            {
                m_ReadDataSuccessEventHandler += value;
            }
            remove
            {
                m_ReadDataSuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// 读取数据失败事件。
        /// </summary>
        public event EventHandler<ReadDataFailureEventArgs> ReadDataFailure
        {
            add
            {
                m_ReadDataFailureEventHandler += value;
            }
            remove
            {
                m_ReadDataFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// 读取数据更新事件。
        /// </summary>
        public event EventHandler<ReadDataUpdateEventArgs> ReadDataUpdate
        {
            add
            {
                m_ReadDataUpdateEventHandler += value;
            }
            remove
            {
                m_ReadDataUpdateEventHandler -= value;
            }
        }

        /// <summary>
        /// 读取数据时加载依赖资源事件。
        /// </summary>
        public event EventHandler<ReadDataDependencyAssetEventArgs> ReadDataDependencyAsset
        {
            add
            {
                m_ReadDataDependencyAssetEventHandler += value;
            }
            remove
            {
                m_ReadDataDependencyAssetEventHandler -= value;
            }
        }
        #endregion

        #region 设置缓存二进制数组的大小
        /// <summary>
        /// 获取缓冲二进制流的大小。
        /// </summary>
        public static int CachedBytesSize
        {
            get
            {
                return s_CachedBytes != null ? s_CachedBytes.Length : 0;
            }
        }

        /// <summary>
        /// 确保二进制流缓存分配足够大小的内存并缓存。
        /// </summary>
        /// <param name="ensureSize">要确保二进制流缓存分配内存的大小。</param>
        public static void EnsureCachedBytesSize(int ensureSize)
        {
            if (ensureSize < 0)
            {
                throw new GameFrameworkException("Ensure size is invalid.");
            }

            if (s_CachedBytes == null || s_CachedBytes.Length < ensureSize)
            {
                FreeCachedBytes();
                int size = (ensureSize - 1 + BlockSize) / BlockSize * BlockSize;
                s_CachedBytes = new byte[size];
            }
        }

        /// <summary>
        /// 释放缓存的二进制流。
        /// </summary>
        public static void FreeCachedBytes()
        {
            //释放“byte”数组如此简单的吗？ ！！！
            s_CachedBytes = null;
        }
        #endregion

        #region 包含不同参数的“ReadData”重载方法以及“DataProvider“中读取数据的”真实过程“
        /// <summary>
        /// 读取数据。
        /// </summary>
        /// <param name="dataAssetName">内容资源名称。</param>
        public void ReadData(string dataAssetName)
        {
            ReadData(dataAssetName, Constant.DefaultPriority, null);
        }

        /// <summary>
        /// 读取数据。
        /// </summary>
        /// <param name="dataAssetName">内容资源名称。</param>
        /// <param name="priority">加载数据资源的优先级。</param>
        public void ReadData(string dataAssetName, int priority)
        {
            ReadData(dataAssetName, priority, null);
        }

        /// <summary>
        /// 读取数据。
        /// </summary>
        /// <param name="dataAssetName">内容资源名称。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void ReadData(string dataAssetName, object userData)
        {
            //从以下代码可知：当从“DataProvider”中加载“asset”资源时默认使用同一优先级“DefaultPriority”
            ReadData(dataAssetName, Constant.DefaultPriority, userData);
        }

        /// <summary>
        /// 读取数据。
        /// </summary>
        /// <param name="dataAssetName">内容资源名称。</param>
        /// <param name="priority">加载数据资源的优先级。</param>
        /// <param name="userData">用户自定义数据。</param>
        public void ReadData(string dataAssetName, int priority, object userData)
        {
            if (m_ResourceManager == null)
            {
                throw new GameFrameworkException("You must set resource manager first.");
            }

            if (m_DataProviderHelper == null)
            {
                throw new GameFrameworkException("You must set data provider helper first.");
            }

            //1.“EditorResourceMode”下，“m_ResourceManager”为“EditorResourceComponent”，其他情况为正式的“ResourceManager”
            //2.将”m_LoadAssetCallbacks“, "m_LoadBinaryCallbacks"作为参数传递，
            //  当”m_ResourceManger“加载资源完毕后自动执行其中的回调方法
            //注意：游戏中用到的”AssetName“都是包含Assets前缀的完整路径，
            //    而”资源模块“中每个Resource内包含的所有Asset也是用的该完整路径，即包含”Assets“前缀的路径
            HasAssetResult result = m_ResourceManager.HasAsset(dataAssetName);
            switch (result)
            {
                //“通用的asset加载逻辑”
                case HasAssetResult.AssetOnDisk:
                case HasAssetResult.AssetOnFileSystem:
                    m_ResourceManager.LoadAsset(dataAssetName, priority, m_LoadAssetCallbacks, userData);
                    break;
                //如上两种情况“AssetOnDisk”，“AssetOnFileSystem”使用同一处理逻辑

                //以下两种情况完完全全可以合并到一起，两者都可以使用“ResourceLoader.LoadBinary”方法达到效果
                //该方法内部本身就已经区分“是否配置了fileSystemName”，并且也都完全可以都使用“LoadBinaryFromFileSystem(dataAssetName)”达到效果
                //不用在这里搞特例：又另外弄一个“EnsureCachedBytesSize”来多此一举
                //对于“二进制格式”，最终得到的都只是“字节数组形式”的“bytes数据”。这些数据会专门使用“DataProviderHelper.ReadData”来定向解析
                //具体“解析内容”交由各个“DataProviderHelper”自身来实现 —— “两种情况”在这里的处理逻辑都是一样的
                //PS：写成这样不知道是为了“装逼”，还是因为后期没时间整理
                case HasAssetResult.BinaryOnDisk:
                    m_ResourceManager.LoadBinary(dataAssetName, m_LoadBinaryCallbacks, userData);
                    break;
                case HasAssetResult.BinaryOnFileSystem:
                    int dataLength = m_ResourceManager.GetBinaryLength(dataAssetName);
                    //这傻逼方法其实就是“弄出一个至少满足二进制格式文件大小的字节数组”而已
                    EnsureCachedBytesSize(dataLength);
                    //这傻逼的方法故弄玄虚：直接使用“LoadBinaryFromFileSystem(dataAssetName)”不就行了，硬是要另外搞一个“s_CachedBytes”变量出来
                    //两者都是读取从fileSystem对象中读取“dataAssetName”的数据
                    //这傻逼故意显摆自己的
                    if (dataLength != m_ResourceManager.LoadBinaryFromFileSystem(dataAssetName, s_CachedBytes))
                    {
                        throw new GameFrameworkException(Utility.Text.Format("Load binary '{0}' from file system with internal error.", dataAssetName));
                    }

                    //这里和“HasAssetResult.BinaryOnDisk”的处理逻辑是“一摸一样”的，都是“在读取到bytes数据”后使用“m_DataProviderHelper.ReadData”解析
                    //这两种情况完全可以合并处理
                    try
                    {
                        //”s_CachedBytes“中是包含所有AB数据的，每个AB中通常会包含很多asset。”dataAssetName“是指”目标asset“的name
                        if (!m_DataProviderHelper.ReadData(m_Owner, dataAssetName, s_CachedBytes, 0, dataLength, userData))
                        {
                            throw new GameFrameworkException(Utility.Text.Format("Load data failure in data provider helper, data asset name '{0}'.", dataAssetName));
                        }

                        if (m_ReadDataSuccessEventHandler != null)
                        {
                            ReadDataSuccessEventArgs loadDataSuccessEventArgs = ReadDataSuccessEventArgs.Create(dataAssetName, 0f, userData);
                            m_ReadDataSuccessEventHandler(this, loadDataSuccessEventArgs);
                            ReferencePool.Release(loadDataSuccessEventArgs);
                        }
                    }
                    catch (Exception exception)
                    {
                        if (m_ReadDataFailureEventHandler != null)
                        {
                            ReadDataFailureEventArgs loadDataFailureEventArgs = ReadDataFailureEventArgs.Create(dataAssetName, exception.ToString(), userData);
                            m_ReadDataFailureEventHandler(this, loadDataFailureEventArgs);
                            ReferencePool.Release(loadDataFailureEventArgs);
                            return;
                        }

                        throw;
                    }

                    break;

                default:
                    throw new GameFrameworkException(Utility.Text.Format("Data asset '{0}' is '{1}'.", dataAssetName, result));
            }
        }
        #endregion

        #region ”ParseData“的多个重载方法，执行核心在于借助”DataProviderHelper.ParseData“来实现逻辑

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataString">要解析的内容字符串。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(string dataString)
        {
            return ParseData(dataString, null);
        }

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataString">要解析的内容字符串。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(string dataString, object userData)
        {
            if (m_DataProviderHelper == null)
            {
                throw new GameFrameworkException("You must set data helper first.");
            }

            if (dataString == null)
            {
                throw new GameFrameworkException("Data string is invalid.");
            }

            try
            {
                return m_DataProviderHelper.ParseData(m_Owner, dataString, userData);
            }
            catch (Exception exception)
            {
                if (exception is GameFrameworkException)
                {
                    throw;
                }

                throw new GameFrameworkException(Utility.Text.Format("Can not parse data string with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataBytes">要解析的内容二进制流。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(byte[] dataBytes)
        {
            if (dataBytes == null)
            {
                throw new GameFrameworkException("Data bytes is invalid.");
            }

            return ParseData(dataBytes, 0, dataBytes.Length, null);
        }

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataBytes">要解析的内容二进制流。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(byte[] dataBytes, object userData)
        {
            if (dataBytes == null)
            {
                throw new GameFrameworkException("Data bytes is invalid.");
            }

            return ParseData(dataBytes, 0, dataBytes.Length, userData);
        }

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataBytes">要解析的内容二进制流。</param>
        /// <param name="startIndex">内容二进制流的起始位置。</param>
        /// <param name="length">内容二进制流的长度。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(byte[] dataBytes, int startIndex, int length)
        {
            return ParseData(dataBytes, startIndex, length, null);
        }

        /// <summary>
        /// 解析内容。
        /// </summary>
        /// <param name="dataBytes">要解析的内容二进制流。</param>
        /// <param name="startIndex">内容二进制流的起始位置。</param>
        /// <param name="length">内容二进制流的长度。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>是否解析内容成功。</returns>
        public bool ParseData(byte[] dataBytes, int startIndex, int length, object userData)
        {
            if (m_DataProviderHelper == null)
            {
                throw new GameFrameworkException("You must set data helper first.");
            }

            if (dataBytes == null)
            {
                throw new GameFrameworkException("Data bytes is invalid.");
            }

            if (startIndex < 0 || length < 0 || startIndex + length > dataBytes.Length)
            {
                throw new GameFrameworkException("Start index or length is invalid.");
            }

            try
            {
                return m_DataProviderHelper.ParseData(m_Owner, dataBytes, startIndex, length, userData);
            }
            catch (Exception exception)
            {
                if (exception is GameFrameworkException)
                {
                    throw;
                }

                throw new GameFrameworkException(Utility.Text.Format("Can not parse data bytes with exception '{0}'.", exception), exception);
            }
        }

        #endregion

        #region LoadAsset后多种结果的回调，组成”m_LoadAssetCallbacks“, "m_LoadBinaryCallbacks"的成员
        //如LoadAssetSuccessCallback, LoadAssetOrBinaryFailureCallback等

        private void LoadAssetSuccessCallback(string dataAssetName, object dataAsset, float duration, object userData)
        {
            try
            {
                //第一步：解析数据
                //PS: 虽然方法名是”ReadData“，但内部实际逻辑是开始解析数据
                if (!m_DataProviderHelper.ReadData(m_Owner, dataAssetName, dataAsset, userData))
                {
                    throw new GameFrameworkException(Utility.Text.Format("Load data failure in data provider helper, data asset name '{0}'.", dataAssetName));
                }

                //第二步：执行委托
                //PS: 这里其实并不是发送消息或事件，这里更像是执行委托或lua中的闭包函数
                //    事件”ReadDataSuccessEventArgs“更像是委托中传递的参数而已
                if (m_ReadDataSuccessEventHandler != null)
                {
                    ReadDataSuccessEventArgs loadDataSuccessEventArgs = ReadDataSuccessEventArgs.Create(dataAssetName, duration, userData);
                    m_ReadDataSuccessEventHandler(this, loadDataSuccessEventArgs);
                    //关键：在委托执行完毕后需要将这里的引用类型参数”loadDataSuccessEventArgs“释放掉
                    ReferencePool.Release(loadDataSuccessEventArgs);
                }
            }
            catch (Exception exception)
            {
                if (m_ReadDataFailureEventHandler != null)
                {
                    ReadDataFailureEventArgs loadDataFailureEventArgs = ReadDataFailureEventArgs.Create(dataAssetName, exception.ToString(), userData);
                    m_ReadDataFailureEventHandler(this, loadDataFailureEventArgs);
                    ReferencePool.Release(loadDataFailureEventArgs);
                    return;
                }

                throw;
            }
            finally
            {
                m_DataProviderHelper.ReleaseDataAsset(m_Owner, dataAsset);
            }
        }

        private void LoadAssetOrBinaryFailureCallback(string dataAssetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            string appendErrorMessage = Utility.Text.Format("Load data failure, data asset name '{0}', status '{1}', error message '{2}'.", dataAssetName, status, errorMessage);
            if (m_ReadDataFailureEventHandler != null)
            {
                ReadDataFailureEventArgs loadDataFailureEventArgs = ReadDataFailureEventArgs.Create(dataAssetName, appendErrorMessage, userData);
                m_ReadDataFailureEventHandler(this, loadDataFailureEventArgs);
                ReferencePool.Release(loadDataFailureEventArgs);
                return;
            }

            throw new GameFrameworkException(appendErrorMessage);
        }

        private void LoadAssetUpdateCallback(string dataAssetName, float progress, object userData)
        {
            if (m_ReadDataUpdateEventHandler != null)
            {
                ReadDataUpdateEventArgs loadDataUpdateEventArgs = ReadDataUpdateEventArgs.Create(dataAssetName, progress, userData);
                m_ReadDataUpdateEventHandler(this, loadDataUpdateEventArgs);
                ReferencePool.Release(loadDataUpdateEventArgs);
            }
        }

        private void LoadAssetDependencyAssetCallback(string dataAssetName, string dependencyAssetName, int loadedCount, int totalCount, object userData)
        {
            if (m_ReadDataDependencyAssetEventHandler != null)
            {
                ReadDataDependencyAssetEventArgs loadDataDependencyAssetEventArgs = ReadDataDependencyAssetEventArgs.Create(dataAssetName, dependencyAssetName, loadedCount, totalCount, userData);
                m_ReadDataDependencyAssetEventHandler(this, loadDataDependencyAssetEventArgs);
                ReferencePool.Release(loadDataDependencyAssetEventArgs);
            }
        }

        private void LoadBinarySuccessCallback(string dataAssetName, byte[] dataBytes, float duration, object userData)
        {
            try
            {
                if (!m_DataProviderHelper.ReadData(m_Owner, dataAssetName, dataBytes, 0, dataBytes.Length, userData))
                {
                    throw new GameFrameworkException(Utility.Text.Format("Load data failure in data provider helper, data asset name '{0}'.", dataAssetName));
                }

                if (m_ReadDataSuccessEventHandler != null)
                {
                    ReadDataSuccessEventArgs loadDataSuccessEventArgs = ReadDataSuccessEventArgs.Create(dataAssetName, duration, userData);
                    m_ReadDataSuccessEventHandler(this, loadDataSuccessEventArgs);
                    ReferencePool.Release(loadDataSuccessEventArgs);
                }
            }
            catch (Exception exception)
            {
                if (m_ReadDataFailureEventHandler != null)
                {
                    ReadDataFailureEventArgs loadDataFailureEventArgs = ReadDataFailureEventArgs.Create(dataAssetName, exception.ToString(), userData);
                    m_ReadDataFailureEventHandler(this, loadDataFailureEventArgs);
                    ReferencePool.Release(loadDataFailureEventArgs);
                    return;
                }

                throw;
            }
        }

        #endregion
    }
}
