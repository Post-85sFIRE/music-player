using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services;

/// <summary>
/// 播放大脑：协调音频引擎 + 播放列表 + 播放模式状态机。
/// UI/ViewModel 只与此对象交互。
/// </summary>
public class PlaybackManager
{
    private readonly IAudioEngine _engine;
    private readonly IPlaylistManager _playlist;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notify;
    private readonly CloudSourceRegistry _cloud;
    private readonly IDownloadCacheService _cache;
    private readonly LyricsService _lyrics;
    private PlaylistItem? _current;
    private string? _currentLocalPath;
    private float _rgLinear = 1f;

    public Track? CurrentTrack => _current?.Track;
    public bool IsPlaying => _engine.IsPlaying;
    public PlaylistItem? CurrentItem => _current;
    public long? CurrentItemId => _current?.Track?.Id;

    public event EventHandler? CurrentTrackChanged;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<LyricsLoadedEventArgs>? LyricsChanged;

    public PlaybackManager(IAudioEngine engine, IPlaylistManager playlist, ISettingsService settings,
        INotificationService notify, CloudSourceRegistry cloud, IDownloadCacheService cache, LyricsService lyrics)
    {
        _engine = engine;
        _playlist = playlist;
        _settings = settings;
        _notify = notify;
        _cloud = cloud;
        _cache = cache;
        _lyrics = lyrics;
        _lyrics.LyricsChanged += (_, e) => LyricsChanged?.Invoke(this, e);

        _engine.PlaybackEnded += (_, _) => OnPlaybackEnded();
        _engine.PositionChanged += (_, e) => PositionChanged?.Invoke(this, e);
        _engine.StateChanged += (_, e) => StateChanged?.Invoke(this, e);
        // 独占模式播放启动失败（设备被占/格式不支持等）：自动退回共享模式并重建当前曲，
        // 这样即便本机独占不可用，也绝不会"点了播放却没声音"。
        _engine.ExclusivePlaybackFailed += (_, _) =>
        {
            _ = Task.Run(async () =>
            {
                try { await ApplyAudioModeChangeAsync(false, resumePlay: true); }
                catch (Exception ex) { _notify.Notify("独占模式失败且退回共享时异常：" + ex.Message); }
            });
        };
        _playlist.PlayRequested += id => PlayItem(id);
    }

    public void PlayItem(long itemId)
    {
        var item = _playlist.GetItem(itemId);
        if (item?.Track is null) return;
        // 异步执行：云文件需要后台下载缓存，绝不能阻塞 UI 线程（否则会与缓存链路的 await 在 UI 同步上下文上互等 → 死锁卡死）。
        _ = LoadAndPlayAsync(item);
    }

    private async Task LoadAndPlayAsync(PlaylistItem item, TimeSpan? resumeAt = null, bool autoPlay = true)
    {
        // 音频引擎已在 CompositionRoot.Build() 启动时初始化；此处不再需要重复 Init，
        // 避免 DI 实例错位或重复初始化导致 BASS 状态异常。
        string path = item.Track!.FilePath;

        // M2：云源先缓存为本地源文件（落在下载/缓存目录），再交给 BASS 播放。
        if (item.Track.SourceType == TrackSourceType.Cloud)
        {
            var provider = _cloud.Get(item.Track.SourceId ?? "");
            if (provider is null) { _notify.Notify("云源未连接：" + (item.Track.SourceId ?? "")); return; }
            // 用户反馈：播放云文件时不要弹“正在缓存”提示，直接后台加载并播放。
            try
            {
                // 关键修复：在后台线程池执行下载缓存，避免 UI 线程上的同步等待死锁（原 GetAwaiter().GetResult() 在 UI 上下文会永久卡死）。
                path = await Task.Run(async () => await _cache.CacheToLocalAsync(
                    provider.Id + "|" + item.Track!.FilePath,
                    () => provider.OpenStreamAsync(item.Track!.FilePath, null, CancellationToken.None),
                    null, CancellationToken.None));
            }
            catch (Exception ex)
            {
                _notify.Notify("云文件缓存失败：" + ex.Message);
                return;
            }
        }

        _currentLocalPath = path;

        if (!_engine.Load(path))
        {
            var detail = string.IsNullOrEmpty(_engine.LastError) ? "" : "\n" + _engine.LastError;
            _notify.Notify("无法加载：" + path + detail);
            return;
        }

        _current = item;
        ApplyGain(item.Track!);
        // 切换音频模式后恢复播放进度（如从暂停态重建流，或用户中途切换独占开关）。
        if (resumeAt.HasValue && resumeAt.Value > TimeSpan.Zero) _engine.Seek(resumeAt.Value);
        if (autoPlay) _engine.Play();
        CurrentTrackChanged?.Invoke(this, EventArgs.Empty);

        // 后台加载歌词（含网络/磁盘 IO，绝不可阻塞 UI 线程）；加载完成后触发 LyricsChanged。
        _ = Task.Run(async () =>
        {
            try { await _lyrics.LoadAsync(item.Track!, path, CancellationToken.None); }
            catch (Exception ex) { Console.Error.WriteLine("[PlaybackManager] 歌词加载异常: " + ex.Message); }
        });

        // 无缝播放：预加载下一首（仅本地曲；云源在结束事件时再缓存），播放结束瞬间引擎内切换。
        if (autoPlay && _settings.Settings.DefaultMode != PlaybackMode.RepeatOne)
        {
            var next = _playlist.Next(_settings.Settings.DefaultMode, item.Id);
            if (next?.Track != null && next.Track.SourceType == TrackSourceType.Local
                && !string.IsNullOrEmpty(next.Track.FilePath))
            {
                _engine.PrepareNext(next.Track.FilePath, () => OnGaplessAdvanced(next));
            }
        }
    }

    /// <summary>
    /// 运行时切换 WASAPI 独占开关：在后台线程重新初始化音频引擎（独占 Init 可能挂起，绝不可阻塞 UI），
    /// 重新就绪后按结果提示用户，并在原本正在播放时无缝恢复当前曲目与进度。
    /// resumePlay 为 null 时按"切换前是否在播"决定；独占总播失败的内部回退强制传 true（用户本意就是播放）。
    /// 返回给用户的可读结论（同时会弹窗提示）。
    /// </summary>
    public async Task<string> ApplyAudioModeChangeAsync(bool exclusive, bool? resumePlay = null)
    {
        // 记录切换前的播放状态与进度，便于切换后恢复。
        var id = _current?.Track?.Id;
        var resume = _current != null ? _engine.Position : TimeSpan.Zero;
        var wasPlaying = resumePlay ?? _engine.IsPlaying;

        // 1) 后台线程重新初始化（WASAPI 独占 Init 带超时，UI 线程等会卡界面）。
        var ok = await Task.Run(() => _engine.Reinitialize(exclusive));

        // 2) 引擎重新初始化失败（如缺原生 dll）：直接提示，不破坏现有状态。
        if (!ok)
        {
            var msg = "音频引擎重新初始化失败：" + (_engine.LastError ?? "未知原因");
            _notify.Notify(msg);
            return msg;
        }

        // 3) 构造结论文案：独占成功 / 自动退回共享 / 退到无声保底。
        string conclusion;
        if (_engine.IsExclusive)
            conclusion = "已切换到 WASAPI 独占输出（bit-perfect）。";
        else if (_engine.UsingNoSoundDevice)
            conclusion = "本机音频初始化失败，已退回无声保底（请检查音频设备 / Windows Audio 服务）。";
        else
            conclusion = "本机不支持 WASAPI 独占（或设备被占用），已自动退回 WASAPI 共享（默认）输出。";

        // 4) 若切换前有当前曲目，重建并恢复进度：原本在播则继续播，原本暂停则保持暂停态。
        if (id.HasValue)
        {
            var item = _playlist.GetItem(id.Value);
            if (item?.Track != null)
                await LoadAndPlayAsync(item, resume, autoPlay: wasPlaying);
        }

        _notify.Notify(conclusion);
        return conclusion;
    }

    public void Play()
    {
        if (_current is null)
        {
            var first = _playlist.Current.Items.FirstOrDefault();
            if (first is not null) { PlayItem(first.Id); return; }
            _notify.Notify("播放列表为空");
            return;
        }

        _engine.Play();
    }

    public void Pause() => _engine.Pause();
    public void Stop() { _engine.Stop(); _current = null; CurrentTrackChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>播放/暂停合一：正在播放则暂停，否则播放（无当前曲时自动播第一首）。</summary>
    public void PlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Next()
    {
        var item = _playlist.Next(_settings.Settings.DefaultMode, _current?.Id ?? -1);
        Advance(item);
    }

    public void Previous()
    {
        var item = _playlist.Previous(_settings.Settings.DefaultMode, _current?.Id ?? -1);
        Advance(item);
    }

    public void Seek(TimeSpan position) => _engine.Seek(position);
    public void SetVolume(float linear) { _settings.Settings.Volume = linear; _engine.SetVolume(linear * _rgLinear); _settings.Save(); }

    /// <summary>手动搜索歌词：用户输入关键词，仅走在线源，命中后写回与当前曲目绑定。</summary>
    public void SearchLyrics(string query)
    {
        if (_current?.Track is null || string.IsNullOrWhiteSpace(query)) return;
        _ = Task.Run(async () =>
        {
            try { await _lyrics.SearchAsync(query, _current.Track, _currentLocalPath, CancellationToken.None); }
            catch (Exception ex) { Console.Error.WriteLine("[PlaybackManager] 歌词搜索异常: " + ex.Message); }
        });
    }

    /// <summary>重新按当前 ReplayGain 设置计算并应用增益（切换增益模式时即时生效）。</summary>
    public void RefreshGain()
    {
        if (_current?.Track != null) ApplyGain(_current.Track);
    }

    /// <summary>按 ReplayGain 设置计算当前曲的增益并应用到引擎。用户音量会与 RG 增益相乘。</summary>
    private void ApplyGain(Track track)
    {
        double db = 0;
        var mode = _settings.Settings.ReplayGainMode;
        if (mode != ReplayGainMode.Off)
        {
            if (mode == ReplayGainMode.Album && track.ReplayGainAlbumGain != 0)
                db = track.ReplayGainAlbumGain;
            else if (track.ReplayGainTrackGain != 0)
                db = track.ReplayGainTrackGain;
        }
        _rgLinear = (float)Math.Pow(10.0, db / 20.0);
        _engine.SetVolume(_settings.Settings.Volume * _rgLinear);
    }

    private void Advance(PlaylistItem? item)
    {
        if (item is not null) PlayItem(item.Id);
        else _notify.Notify("没有可播放的下一首");
    }

    private void OnPlaybackEnded()
    {
        if (_settings.Settings.DefaultMode == PlaybackMode.RepeatOne && _current is not null)
        {
            _engine.Play();
            return;
        }

        Next();
    }

    /// <summary>引擎内无缝切换后由音频引擎回调：更新当前曲并继续预取下下首。</summary>
    private void OnGaplessAdvanced(PlaylistItem next)
    {
        _current = next;
        CurrentTrackChanged?.Invoke(this, EventArgs.Empty);
        if (_settings.Settings.DefaultMode != PlaybackMode.RepeatOne)
        {
            var n2 = _playlist.Next(_settings.Settings.DefaultMode, next.Id);
            if (n2?.Track != null && n2.Track.SourceType == TrackSourceType.Local
                && !string.IsNullOrEmpty(n2.Track.FilePath))
            {
                _engine.PrepareNext(n2.Track.FilePath, () => OnGaplessAdvanced(n2));
            }
        }
    }
}
