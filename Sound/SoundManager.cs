//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.Resource;
using System;
using System.Collections.Generic;

namespace GameFramework.Sound
{
    /// <summary>
    /// 声音管理器。
    /// PS:
    /// 问题1."Sound", “SoundGroup”, "SoundAgent"的区别是什么？
    ///
    ///
    /// 问题2.“SoundHelper”，"SoundGroupHelper"和“SoundAgentHelper”的区别是什么？
    ///
    ///
    /// </summary>
    internal sealed partial class SoundManager : GameFrameworkModule, ISoundManager
    {
        private readonly Dictionary<string, SoundGroup> m_SoundGroups;
        //这个命名要改，现在容易混淆，应该改成“m_SoundsLoading”
        private readonly List<int> m_SoundsBeingLoaded;
        private readonly HashSet<int> m_SoundsToReleaseOnLoad;
        private readonly LoadAssetCallbacks m_LoadAssetCallbacks;
        private IResourceManager m_ResourceManager;
        private ISoundHelper m_SoundHelper;
        private int m_Serial;
        private EventHandler<PlaySoundSuccessEventArgs> m_PlaySoundSuccessEventHandler;
        private EventHandler<PlaySoundFailureEventArgs> m_PlaySoundFailureEventHandler;
        private EventHandler<PlaySoundUpdateEventArgs> m_PlaySoundUpdateEventHandler;
        private EventHandler<PlaySoundDependencyAssetEventArgs> m_PlaySoundDependencyAssetEventHandler;

        /// <summary>
        /// 初始化声音管理器的新实例。
        /// </summary>
        public SoundManager()
        {
            m_SoundGroups = new Dictionary<string, SoundGroup>(StringComparer.Ordinal);
            m_SoundsBeingLoaded = new List<int>();
            m_SoundsToReleaseOnLoad = new HashSet<int>();
            //在加载声音资源时会将“m_LoadedAssetCallback”作为参数传递过去，当加载结束后会自动执行该回调
            //而回调的具体内容则写在本脚本中，因此具有很大的自由度
            m_LoadAssetCallbacks = new LoadAssetCallbacks(LoadAssetSuccessCallback, LoadAssetFailureCallback, LoadAssetUpdateCallback, LoadAssetDependencyAssetCallback);
            m_ResourceManager = null;
            m_SoundHelper = null;
            m_Serial = 0;
            m_PlaySoundSuccessEventHandler = null;
            m_PlaySoundFailureEventHandler = null;
            m_PlaySoundUpdateEventHandler = null;
            m_PlaySoundDependencyAssetEventHandler = null;
        }

        /// <summary>
        /// 获取声音组数量。
        /// </summary>
        public int SoundGroupCount
        {
            get
            {
                return m_SoundGroups.Count;
            }
        }

        /// <summary>
        /// 播放声音成功事件。
        /// </summary>
        public event EventHandler<PlaySoundSuccessEventArgs> PlaySoundSuccess
        {
            add
            {
                m_PlaySoundSuccessEventHandler += value;
            }
            remove
            {
                m_PlaySoundSuccessEventHandler -= value;
            }
        }

        /// <summary>
        /// 播放声音失败事件。
        /// </summary>
        public event EventHandler<PlaySoundFailureEventArgs> PlaySoundFailure
        {
            add
            {
                m_PlaySoundFailureEventHandler += value;
            }
            remove
            {
                m_PlaySoundFailureEventHandler -= value;
            }
        }

        /// <summary>
        /// 播放声音更新事件。
        /// </summary>
        public event EventHandler<PlaySoundUpdateEventArgs> PlaySoundUpdate
        {
            add
            {
                m_PlaySoundUpdateEventHandler += value;
            }
            remove
            {
                m_PlaySoundUpdateEventHandler -= value;
            }
        }

        /// <summary>
        /// 播放声音时加载依赖资源事件。
        /// </summary>
        public event EventHandler<PlaySoundDependencyAssetEventArgs> PlaySoundDependencyAsset
        {
            add
            {
                m_PlaySoundDependencyAssetEventHandler += value;
            }
            remove
            {
                m_PlaySoundDependencyAssetEventHandler -= value;
            }
        }

        /// <summary>
        /// 声音管理器轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理声音管理器。
        /// </summary>
        internal override void Shutdown()
        {
            //只是停止声音播放而已，并没有释放该声音的资源，此时不用处理资源的释放
            StopAllLoadedSounds();
            m_SoundGroups.Clear();
            m_SoundsBeingLoaded.Clear();
            m_SoundsToReleaseOnLoad.Clear();
        }

        /// <summary>
        /// 设置资源管理器。
        /// </summary>
        /// <param name="resourceManager">资源管理器。</param>
        public void SetResourceManager(IResourceManager resourceManager)
        {
            if (resourceManager == null)
            {
                throw new GameFrameworkException("Resource manager is invalid.");
            }

            m_ResourceManager = resourceManager;
        }

        /// <summary>
        /// 设置声音辅助器。
        /// </summary>
        /// <param name="soundHelper">声音辅助器。</param>
        public void SetSoundHelper(ISoundHelper soundHelper)
        {
            if (soundHelper == null)
            {
                throw new GameFrameworkException("Sound helper is invalid.");
            }

            m_SoundHelper = soundHelper;
        }

        /// <summary>
        /// 是否存在指定声音组。
        /// </summary>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <returns>指定声音组是否存在。</returns>
        public bool HasSoundGroup(string soundGroupName)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                throw new GameFrameworkException("Sound group name is invalid.");
            }

            return m_SoundGroups.ContainsKey(soundGroupName);
        }

        /// <summary>
        /// 获取指定声音组。
        /// </summary>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <returns>要获取的声音组。</returns>
        public ISoundGroup GetSoundGroup(string soundGroupName)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                throw new GameFrameworkException("Sound group name is invalid.");
            }

            SoundGroup soundGroup = null;
            if (m_SoundGroups.TryGetValue(soundGroupName, out soundGroup))
            {
                return soundGroup;
            }

            return null;
        }

        /// <summary>
        /// 获取所有声音组。
        /// </summary>
        /// <returns>所有声音组。</returns>
        public ISoundGroup[] GetAllSoundGroups()
        {
            int index = 0;
            ISoundGroup[] results = new ISoundGroup[m_SoundGroups.Count];
            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                results[index++] = soundGroup.Value;
            }

            return results;
        }

        /// <summary>
        /// 获取所有声音组。
        /// </summary>
        /// <param name="results">所有声音组。</param>
        public void GetAllSoundGroups(List<ISoundGroup> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                results.Add(soundGroup.Value);
            }
        }

        /// <summary>
        /// 增加声音组。
        /// </summary>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="soundGroupHelper">声音组辅助器。</param>
        /// <returns>是否增加声音组成功。</returns>
        public bool AddSoundGroup(string soundGroupName, ISoundGroupHelper soundGroupHelper)
        {
            return AddSoundGroup(soundGroupName, false, Constant.DefaultMute, Constant.DefaultVolume, soundGroupHelper);
        }

        /// <summary>
        /// 增加声音组。
        /// </summary>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="soundGroupAvoidBeingReplacedBySamePriority">声音组中的声音是否避免被同优先级声音替换。</param>
        /// <param name="soundGroupMute">声音组是否静音。</param>
        /// <param name="soundGroupVolume">声音组音量。</param>
        /// <param name="soundGroupHelper">声音组辅助器。</param>
        /// <returns>是否增加声音组成功。</returns>
        public bool AddSoundGroup(string soundGroupName, bool soundGroupAvoidBeingReplacedBySamePriority, bool soundGroupMute, float soundGroupVolume, ISoundGroupHelper soundGroupHelper)
        {
            if (string.IsNullOrEmpty(soundGroupName))
            {
                throw new GameFrameworkException("Sound group name is invalid.");
            }

            if (soundGroupHelper == null)
            {
                throw new GameFrameworkException("Sound group helper is invalid.");
            }

            if (HasSoundGroup(soundGroupName))
            {
                return false;
            }

            SoundGroup soundGroup = new SoundGroup(soundGroupName, soundGroupHelper)
            {
                AvoidBeingReplacedBySamePriority = soundGroupAvoidBeingReplacedBySamePriority,
                Mute = soundGroupMute,
                Volume = soundGroupVolume
            };

            m_SoundGroups.Add(soundGroupName, soundGroup);

            return true;
        }

        /// <summary>
        /// 增加声音代理辅助器。
        /// </summary>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="soundAgentHelper">要增加的声音代理辅助器。</param>
        public void AddSoundAgentHelper(string soundGroupName, ISoundAgentHelper soundAgentHelper)
        {
            if (m_SoundHelper == null)
            {
                throw new GameFrameworkException("You must set sound helper first.");
            }

            SoundGroup soundGroup = (SoundGroup)GetSoundGroup(soundGroupName);
            if (soundGroup == null)
            {
                throw new GameFrameworkException(Utility.Text.Format("Sound group '{0}' is not exist.", soundGroupName));
            }

            soundGroup.AddSoundAgentHelper(m_SoundHelper, soundAgentHelper);
        }

        /// <summary>
        /// 获取所有正在加载声音的序列编号。
        /// </summary>
        /// <returns>所有正在加载声音的序列编号。</returns>
        public int[] GetAllLoadingSoundSerialIds()
        {
            return m_SoundsBeingLoaded.ToArray();
        }

        /// <summary>
        /// 获取所有正在加载声音的序列编号。
        /// </summary>
        /// <param name="results">所有正在加载声音的序列编号。</param>
        public void GetAllLoadingSoundSerialIds(List<int> results)
        {
            if (results == null)
            {
                throw new GameFrameworkException("Results is invalid.");
            }

            results.Clear();
            results.AddRange(m_SoundsBeingLoaded);
        }

        /// <summary>
        /// 是否正在加载声音。
        /// </summary>
        /// <param name="serialId">声音序列编号。</param>
        /// <returns>是否正在加载声音。</returns>
        public bool IsLoadingSound(int serialId)
        {
            return m_SoundsBeingLoaded.Contains(serialId);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName)
        {
            return PlaySound(soundAssetName, soundGroupName, Resource.Constant.DefaultPriority, null, null);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="priority">加载声音资源的优先级。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, int priority)
        {
            return PlaySound(soundAssetName, soundGroupName, priority, null, null);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="playSoundParams">播放声音参数。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, PlaySoundParams playSoundParams)
        {
            return PlaySound(soundAssetName, soundGroupName, Resource.Constant.DefaultPriority, playSoundParams, null);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, object userData)
        {
            return PlaySound(soundAssetName, soundGroupName, Resource.Constant.DefaultPriority, null, userData);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="priority">加载声音资源的优先级。</param>
        /// <param name="playSoundParams">播放声音参数。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, int priority, PlaySoundParams playSoundParams)
        {
            return PlaySound(soundAssetName, soundGroupName, priority, playSoundParams, null);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="priority">加载声音资源的优先级。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, int priority, object userData)
        {
            return PlaySound(soundAssetName, soundGroupName, priority, null, userData);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="playSoundParams">播放声音参数。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, PlaySoundParams playSoundParams, object userData)
        {
            return PlaySound(soundAssetName, soundGroupName, Resource.Constant.DefaultPriority, playSoundParams, userData);
        }

        /// <summary>
        /// 播放声音。
        /// </summary>
        /// <param name="soundAssetName">声音资源名称。</param>
        /// <param name="soundGroupName">声音组名称。</param>
        /// <param name="priority">加载声音资源的优先级。</param>
        /// <param name="playSoundParams">播放声音参数。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <returns>声音的序列编号。</returns>
        public int PlaySound(string soundAssetName, string soundGroupName, int priority, PlaySoundParams playSoundParams, object userData)
        {
            if (m_ResourceManager == null)
            {
                throw new GameFrameworkException("You must set resource manager first.");
            }

            if (m_SoundHelper == null)
            {
                throw new GameFrameworkException("You must set sound helper first.");
            }

            if (playSoundParams == null)
            {
                playSoundParams = PlaySoundParams.Create();
            }

            int serialId = ++m_Serial;
            PlaySoundErrorCode? errorCode = null;
            string errorMessage = null;
            SoundGroup soundGroup = (SoundGroup)GetSoundGroup(soundGroupName);
            if (soundGroup == null)
            {
                errorCode = PlaySoundErrorCode.SoundGroupNotExist;
                errorMessage = Utility.Text.Format("Sound group '{0}' is not exist.", soundGroupName);
            }
            else if (soundGroup.SoundAgentCount <= 0)
            {
                errorCode = PlaySoundErrorCode.SoundGroupHasNoAgent;
                errorMessage = Utility.Text.Format("Sound group '{0}' is have no sound agent.", soundGroupName);
            }

            //判断枚举变量是否有值也可以使用如下的方式
            if (errorCode.HasValue)
            {
                if (m_PlaySoundFailureEventHandler != null)
                {
                    PlaySoundFailureEventArgs playSoundFailureEventArgs = PlaySoundFailureEventArgs.Create(serialId, soundAssetName, soundGroupName, playSoundParams, errorCode.Value, errorMessage, userData);
                    m_PlaySoundFailureEventHandler(this, playSoundFailureEventArgs);
                    //这里回收的时机没有问题。因此上述委托执行完毕后是在外部监听所有的逻辑都执行完，
                    //然后才会到“ReferencePool.Release”，因此没有问题
                    ReferencePool.Release(playSoundFailureEventArgs);

                    //******** 释放playSoundParams参数的引用 ********
                    //本质上来讲，跟上面的“xxxEventArgs”变量一样，用完之后都是要回收的
                    if (playSoundParams.Referenced)
                    {
                        ReferencePool.Release(playSoundParams);
                    }

                    return serialId;
                }

                throw new GameFrameworkException(errorMessage);
            }

            //此时代表该声音资源正在加载中，只有在放入AudioSource.clip中才代表已经加载“loaded”
            m_SoundsBeingLoaded.Add(serialId);
            //正式开始加载声音资源
            //”PlaySoundInfo.Create“变量的目的在于传递过去，然后在回调时传递过来，这样就不用在本脚本中找一个全局变量
            //将其存储起来了
            m_ResourceManager.LoadAsset(soundAssetName, priority, m_LoadAssetCallbacks, PlaySoundInfo.Create(serialId, soundGroup, playSoundParams, userData));
            return serialId;
        }

        /// <summary>
        /// 停止播放声音。
        /// </summary>
        /// <param name="serialId">要停止播放声音的序列编号。</param>
        /// <returns>是否停止播放声音成功。</returns>
        public bool StopSound(int serialId)
        {
            return StopSound(serialId, Constant.DefaultFadeOutSeconds);
        }

        /// <summary>
        /// 停止播放声音。
        /// </summary>
        /// <param name="serialId">要停止播放声音的序列编号。</param>
        /// <param name="fadeOutSeconds">声音淡出时间，以秒为单位。</param>
        /// <returns>是否停止播放声音成功。</returns>
        public bool StopSound(int serialId, float fadeOutSeconds)
        {
            //在当前所有SoundAgent中检测，如果不是正在使用的Clip则无需停止(说明该clip已被替换掉)
            if (IsLoadingSound(serialId))
            {
                //由于可能出现在“加载声音资源”的过程中执行“StopSound”的情况
                m_SoundsToReleaseOnLoad.Add(serialId); //方便“LoadAssetSuccessCallback”执行

                //此处逻辑有问题：这里只是停止掉“正在加载中”的音频资源，但可能该资源当前正在加载过程中，并且之后一小段时间内根本无法直接终止
                //所以此时“m_SoundsBeingLoaded”集合中应该仍然保留该音频资源编号，知道加载结束时才能从集合中移除
                //为防止频繁点击导致“StopSound”方法重复执行，导致这里的“m_SoundsToReleaseOnLoad.Add”报错
                //可以事先加入“Contains”判断
                m_SoundsBeingLoaded.Remove(serialId);  //用于检测当前加载中的资源的集合，在多处使用

                return true;
            }

            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                if (soundGroup.Value.StopSound(serialId, fadeOutSeconds))
                {
                    return true;
                }

                //如果失败则说明该clip已被替换掉则自然无需再“StopSound”
            }

            return false;
        }

        /// <summary>
        /// 停止所有已加载的声音。
        /// </summary>
        public void StopAllLoadedSounds()
        {
            StopAllLoadedSounds(Constant.DefaultFadeOutSeconds);
        }

        /// <summary>
        /// 停止所有已加载的声音。
        /// </summary>
        /// <param name="fadeOutSeconds">声音淡出时间，以秒为单位。</param>
        public void StopAllLoadedSounds(float fadeOutSeconds)
        {
            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                soundGroup.Value.StopAllLoadedSounds(fadeOutSeconds);
            }
        }

        /// <summary>
        /// 停止所有正在加载中的声音。
        /// </summary>
        public void StopAllLoadingSounds()
        {
            foreach (int serialId in m_SoundsBeingLoaded)
            {
                //这里只是需要停止掉所有“正在加载中”的音频资源，但该音频可能当前或之后一小段时间内其仍然处于加载中
                //因此不能改变“m_SoundsBeingLoaded”集合中的元素
                m_SoundsToReleaseOnLoad.Add(serialId);
            }
        }

        /// <summary>
        /// 暂停播放声音。
        /// </summary>
        /// <param name="serialId">要暂停播放声音的序列编号。</param>
        public void PauseSound(int serialId)
        {
            PauseSound(serialId, Constant.DefaultFadeOutSeconds);
        }

        /// <summary>
        /// 暂停播放声音。
        /// </summary>
        /// <param name="serialId">要暂停播放声音的序列编号。</param>
        /// <param name="fadeOutSeconds">声音淡出时间，以秒为单位。</param>
        public void PauseSound(int serialId, float fadeOutSeconds)
        {
            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                if (soundGroup.Value.PauseSound(serialId, fadeOutSeconds))
                {
                    return;
                }
            }

            throw new GameFrameworkException(Utility.Text.Format("Can not find sound '{0}'.", serialId));
        }

        /// <summary>
        /// 恢复播放声音。
        /// </summary>
        /// <param name="serialId">要恢复播放声音的序列编号。</param>
        public void ResumeSound(int serialId)
        {
            ResumeSound(serialId, Constant.DefaultFadeInSeconds);
        }

        /// <summary>
        /// 恢复播放声音。
        /// </summary>
        /// <param name="serialId">要恢复播放声音的序列编号。</param>
        /// <param name="fadeInSeconds">声音淡入时间，以秒为单位。</param>
        public void ResumeSound(int serialId, float fadeInSeconds)
        {
            foreach (KeyValuePair<string, SoundGroup> soundGroup in m_SoundGroups)
            {
                if (soundGroup.Value.ResumeSound(serialId, fadeInSeconds))
                {
                    return;
                }
            }

            throw new GameFrameworkException(Utility.Text.Format("Can not find sound '{0}'.", serialId));
        }

        private void LoadAssetSuccessCallback(string soundAssetName, object soundAsset, float duration, object userData)
        {
            PlaySoundInfo playSoundInfo = (PlaySoundInfo)userData;
            if (playSoundInfo == null)
            {
                throw new GameFrameworkException("Play sound info is invalid.");
            }

            //在加载声音资源之初就已经记录了该资源的“serialId”
            if (m_SoundsToReleaseOnLoad.Contains(playSoundInfo.SerialId))
            {
                m_SoundsToReleaseOnLoad.Remove(playSoundInfo.SerialId);
                if (playSoundInfo.PlaySoundParams.Referenced)
                {
                    ReferencePool.Release(playSoundInfo.PlaySoundParams);
                }

                ReferencePool.Release(playSoundInfo);
                m_SoundHelper.ReleaseSoundAsset(soundAsset);

                //这里逻辑有问题：如果要提前“return”，则应该同时移除掉“m_SoundsBeingLoaded”集合中的该“音频id”
                return;
            }

            //为防止部分情况提前return，该句应该放到最前端执行
            m_SoundsBeingLoaded.Remove(playSoundInfo.SerialId);

            //加载声音资源完毕后开始“PlaySound”
            PlaySoundErrorCode? errorCode = null;
            ISoundAgent soundAgent = playSoundInfo.SoundGroup.PlaySound(playSoundInfo.SerialId, soundAsset, playSoundInfo.PlaySoundParams, out errorCode);
            //对于”播放声音“的需求而言，只要能够找到”合适的SoundAgent“基本就代表”声音播放成功“
            if (soundAgent != null)
            {
                if (m_PlaySoundSuccessEventHandler != null)
                {
                    PlaySoundSuccessEventArgs playSoundSuccessEventArgs = PlaySoundSuccessEventArgs.Create(playSoundInfo.SerialId, soundAssetName, soundAgent, duration, playSoundInfo.UserData);
                    m_PlaySoundSuccessEventHandler(this, playSoundSuccessEventArgs);
                    ReferencePool.Release(playSoundSuccessEventArgs);
                }

                if (playSoundInfo.PlaySoundParams.Referenced)
                {
                    ReferencePool.Release(playSoundInfo.PlaySoundParams);
                }

                ReferencePool.Release(playSoundInfo);
                return;
            }

            //这里逻辑有问题：走到这一步是因为”没有找到合适的SoundAgent“，但可以肯定”m_SoundsToReleaseOnLoad“集合中没有该音频serialId
            m_SoundsToReleaseOnLoad.Remove(playSoundInfo.SerialId);

            //由于”soundAgent为null导致声音播放失败“，因此需要释放该”已经加载成功的声音资源“
            m_SoundHelper.ReleaseSoundAsset(soundAsset);
            string errorMessage = Utility.Text.Format("Sound group '{0}' play sound '{1}' failure.", playSoundInfo.SoundGroup.Name, soundAssetName);
            if (m_PlaySoundFailureEventHandler != null)
            {
                PlaySoundFailureEventArgs playSoundFailureEventArgs = PlaySoundFailureEventArgs.Create(playSoundInfo.SerialId, soundAssetName, playSoundInfo.SoundGroup.Name, playSoundInfo.PlaySoundParams, errorCode.Value, errorMessage, playSoundInfo.UserData);
                m_PlaySoundFailureEventHandler(this, playSoundFailureEventArgs);
                ReferencePool.Release(playSoundFailureEventArgs);

                if (playSoundInfo.PlaySoundParams.Referenced)
                {
                    ReferencePool.Release(playSoundInfo.PlaySoundParams);
                }

                ReferencePool.Release(playSoundInfo);
                return;
            }

            if (playSoundInfo.PlaySoundParams.Referenced)
            {
                ReferencePool.Release(playSoundInfo.PlaySoundParams);
            }

            ReferencePool.Release(playSoundInfo);
            throw new GameFrameworkException(errorMessage);
        }

        private void LoadAssetFailureCallback(string soundAssetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            PlaySoundInfo playSoundInfo = (PlaySoundInfo)userData;
            if (playSoundInfo == null)
            {
                throw new GameFrameworkException("Play sound info is invalid.");
            }

            if (m_SoundsToReleaseOnLoad.Contains(playSoundInfo.SerialId))
            {
                m_SoundsToReleaseOnLoad.Remove(playSoundInfo.SerialId);
                if (playSoundInfo.PlaySoundParams.Referenced)
                {
                    ReferencePool.Release(playSoundInfo.PlaySoundParams);
                }

                return;
            }

            //由于有return语句，该句应该放在最前端执行
            m_SoundsBeingLoaded.Remove(playSoundInfo.SerialId);
            string appendErrorMessage = Utility.Text.Format("Load sound failure, asset name '{0}', status '{1}', error message '{2}'.", soundAssetName, status, errorMessage);
            if (m_PlaySoundFailureEventHandler != null)
            {
                PlaySoundFailureEventArgs playSoundFailureEventArgs = PlaySoundFailureEventArgs.Create(playSoundInfo.SerialId, soundAssetName, playSoundInfo.SoundGroup.Name, playSoundInfo.PlaySoundParams, PlaySoundErrorCode.LoadAssetFailure, appendErrorMessage, playSoundInfo.UserData);
                m_PlaySoundFailureEventHandler(this, playSoundFailureEventArgs);
                ReferencePool.Release(playSoundFailureEventArgs);

                if (playSoundInfo.PlaySoundParams.Referenced)
                {
                    ReferencePool.Release(playSoundInfo.PlaySoundParams);
                }

                return;
            }

            throw new GameFrameworkException(appendErrorMessage);
        }

        private void LoadAssetUpdateCallback(string soundAssetName, float progress, object userData)
        {
            PlaySoundInfo playSoundInfo = (PlaySoundInfo)userData;
            if (playSoundInfo == null)
            {
                throw new GameFrameworkException("Play sound info is invalid.");
            }

            if (m_PlaySoundUpdateEventHandler != null)
            {
                PlaySoundUpdateEventArgs playSoundUpdateEventArgs = PlaySoundUpdateEventArgs.Create(playSoundInfo.SerialId, soundAssetName, playSoundInfo.SoundGroup.Name, playSoundInfo.PlaySoundParams, progress, playSoundInfo.UserData);
                m_PlaySoundUpdateEventHandler(this, playSoundUpdateEventArgs);
                ReferencePool.Release(playSoundUpdateEventArgs);
            }
        }

        private void LoadAssetDependencyAssetCallback(string soundAssetName, string dependencyAssetName, int loadedCount, int totalCount, object userData)
        {
            PlaySoundInfo playSoundInfo = (PlaySoundInfo)userData;
            if (playSoundInfo == null)
            {
                throw new GameFrameworkException("Play sound info is invalid.");
            }

            if (m_PlaySoundDependencyAssetEventHandler != null)
            {
                PlaySoundDependencyAssetEventArgs playSoundDependencyAssetEventArgs = PlaySoundDependencyAssetEventArgs.Create(playSoundInfo.SerialId, soundAssetName, playSoundInfo.SoundGroup.Name, playSoundInfo.PlaySoundParams, dependencyAssetName, loadedCount, totalCount, playSoundInfo.UserData);
                m_PlaySoundDependencyAssetEventHandler(this, playSoundDependencyAssetEventArgs);
                ReferencePool.Release(playSoundDependencyAssetEventArgs);
            }
        }
    }
}
