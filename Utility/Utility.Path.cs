//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using System.IO;
using UnityEngine;

namespace GameFramework
{
    public static partial class Utility
    {
        /// <summary>
        /// 路径相关的实用函数。
        /// </summary>
        public static class Path
        {
            /// <summary>
            /// 获取规范的路径。
            /// </summary>
            /// <param name="path">要规范的路径。</param>
            /// <returns>规范的路径。</returns>
            public static string GetRegularPath(string path)
            {
                if (path == null)
                {
                    return null;
                }

                return path.Replace('\\', '/');
            }

            /// <summary>
            /// 获取远程格式的路径（带有file:// 或 http:// 前缀）。
            /// PS：将任意地址转换成“远程格式”的地址。如果该地址本身已经是“http://”等类型的远程地址，则无需转换
            ///    否则需要为其添加“file://”前缀，转换成“远程地址”
            /// </summary>
            /// <param name="path">原始路径。</param>
            /// <returns>远程格式路径。</returns>
            public static string GetRemotePath(string path)
            {
                //替换“path”中所有的“正斜杠”和“反斜杠”
                string regularPath = GetRegularPath(path);
                if (regularPath == null)
                {
                    return null;
                }

                //如果路径中包含“://”，说明在此框架内基本都是“http://”的服务器地址前缀
                //如果不包含，说明必然就是“本地的streamingAssetPath/PersistentDataPath/TempporaryCachePath”等路径
                //如果是本地的地址，则为其添加“file:///”将其转换成“远程形式的地址”，代表是某些方法如”UnityWebRequest.Get/Post“只接受远程路径吧
                return regularPath.Contains("://") ? regularPath : ("file:///" + regularPath).Replace("file:////", "file:///");
            }

            /// <summary>
            /// 移除空文件夹。
            /// </summary>
            /// <param name="directoryName">要处理的文件夹名称。</param>
            /// <returns>是否移除空文件夹成功。</returns>
            public static bool RemoveEmptyDirectory(string directoryName)
            {
                if (string.IsNullOrEmpty(directoryName))
                {
                    throw new GameFrameworkException("Directory name is invalid.");
                }

                try
                {
                    if (!Directory.Exists(directoryName))
                    {
                        return false;
                    }

                    // 不使用 SearchOption.AllDirectories，以便于在可能产生异常的环境下删除尽可能多的目录
                    string[] subDirectoryNames = Directory.GetDirectories(directoryName, "*");
                    int subDirectoryCount = subDirectoryNames.Length;
                    foreach (string subDirectoryName in subDirectoryNames)
                    {
                        if (RemoveEmptyDirectory(subDirectoryName))
                        {
                            subDirectoryCount--;
                        }
                    }

                    if (subDirectoryCount > 0)
                    {
                        return false;
                    }

                    if (Directory.GetFiles(directoryName, "*").Length > 0)
                    {
                        return false;
                    }

                    Directory.Delete(directoryName);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
