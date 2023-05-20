//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

namespace GameFramework.Resource
{
    /// <summary>
    /// 加载资源状态。
    /// </summary>
    public enum LoadResourceStatus : byte
    {
        /// <summary>
        /// 加载资源完成。
        /// </summary>
        Success = 0,

        /// <summary>
        /// 资源不存在。
        /// PS：这里的”不存在“指的是在”ResourceManager“的”m_ResourceInfos“集合中不存在该集合的记录信息
        ///     不代表其在本地是否有下载好的文件，跟这没有任何关系
        ///     即如果该资源在本地有下载好的文件，但其不是”资源的目标变体版本“，则同样返回此结果
        /// </summary>
        NotExist,

        /// <summary>
        /// 资源尚未准备完毕。
        /// PS：能得到本结果，说明该资源必然在”m_ResourceInfos“中有记录，必然是本次运行中需要用到的资源，只是该资源当前尚未存储到本地
        /// </summary>
        NotReady,

        /// <summary>
        /// 依赖资源错误。
        /// </summary>
        DependencyError,

        /// <summary>
        /// 资源类型错误。
        /// PS：
        /// 1.使用”普通文本文件“的方式加载”二进制文件“
        /// 2.使用“二进制文件”的方式加载“普通文本文件”
        /// 3.从AB中加载目标asset时发现“传递过来的resource参数”不是“AssetBundle类型”
        /// </summary>
        TypeError,

        /// <summary>
        /// 加载资源错误。
        /// </summary>
        AssetError
    }
}
