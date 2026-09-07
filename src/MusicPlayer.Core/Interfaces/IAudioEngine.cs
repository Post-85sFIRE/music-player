using System;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public class PositionChangedEventArgs : EventArgs
{
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
}

public class StateChangedEventArgs : EventArgs
{
    public bool IsPlaying { get; set; }
}

/// <summary>
/// 音频引擎契约：只负责"把音轨变成声音"，不关心播放逻辑。
/// 实现见 MusicPlayer.Audio（BASS）。
/// </summary>
public interface IAudioEngine : IDisposable
{
    bool Initialize();

    /// <summary>
    /// 运行时重新初始化音频引擎（切换 WASAPI 独占开关时调用，无需重启程序）。
    /// exclusive 表示期望的独占开关；内部会让运行时释放旧设备、按该开关重跑初始化+回退链；
    /// 成功返回 true（无论最终独占还是共享）。
    /// </summary>
    bool Reinitialize(bool exclusive);

    /// <summary>当前是否处于 WASAPI 独占模式（用于 UI 提示与诊断）。</summary>
    bool IsExclusive { get; }

    /// <summary>当前是否退到了无输出保底设备（BASS 能跑通但不发声）。</summary>
    bool UsingNoSoundDevice { get; }

    /// <summary>加载本地文件（第一期）。云源第二期由 IDownloadCacheService 落地后传本地路径。</summary>
    bool Load(string filePath);

    /// <summary>预加载下一首流，供播放结束瞬间无缝切换（gapless）。onAdvanced 在引擎内切换后回调上层。</summary>
    void PrepareNext(string filePath, Action onAdvanced);

    /// <summary>最近一次 Load 失败的详细原因（含 BASS 错误码中文说明）；成功加载后为 null。</summary>
    string? LastError { get; }

    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>应用内音量，线性 0..1。</summary>
    void SetVolume(float linear);

    bool IsPlaying { get; }

    event EventHandler? PlaybackEnded;
    event EventHandler<PositionChangedEventArgs>? PositionChanged;
    event EventHandler<StateChangedEventArgs>? StateChanged;
    /// <summary>独占模式播放启动失败时触发，供上层自动退回共享模式。</summary>
    event EventHandler? ExclusivePlaybackFailed;
}
