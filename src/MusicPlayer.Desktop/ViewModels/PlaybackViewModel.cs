using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

[ObservableObject]
public partial class PlaybackViewModel
{
    private readonly PlaybackManager _pm;
    private readonly ISettingsService _settings;
    private readonly LyricsService _lyrics;

    /// <summary>手动/自动搜索歌词命中多组候选时触发，由 View 弹出选择对话框。</summary>
    public event EventHandler<LyricsCandidatesRequestedEventArgs>? LyricsCandidatesRequested;

    [ObservableProperty] private string _currentTitle = "未播放";
    [ObservableProperty] private string _currentArtist = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private float _volume = 1f;
    [ObservableProperty] private PlaybackMode _mode = PlaybackMode.Sequential;

    // 歌词区绑定
    [ObservableProperty] private ObservableCollection<LyricLineViewModel> _lyricsLines = new();
    [ObservableProperty] private string _lyricsSource = "";
    [ObservableProperty] private bool _lyricsHasTiming;
    [ObservableProperty] private string _lyricsSearchText = "";

    // 播放/暂停按钮上的文字（随 IsPlaying 切换），避免再放一个停止键。
    [ObservableProperty] private string _playPauseLabel = "播放";

    /// <summary>非模态状态提示（云曲加载中 / 加载失败等），显示在"正在播放"面板，不打断播放。</summary>
    [ObservableProperty] private string _statusText = "";
    // 播放模式按钮上的中文文案。
    [ObservableProperty] private string _modeLabel = "顺序播放";

    private List<LyricLineViewModel> _lyricList = new();
    private int _activeIdx = -1;

    public PlaybackViewModel(PlaybackManager pm, ISettingsService settings, LyricsService lyrics)
    {
        _pm = pm;
        _settings = settings;
        _lyrics = lyrics;
        _volume = settings.Settings.Volume;
        _mode = settings.Settings.DefaultMode;
        ModeLabel = ToModeLabel(_mode);

        _pm.CurrentTrackChanged += (_, _) => OnCurrentChanged();
        _pm.StateChanged += (_, e) => System.Windows.Application.Current.Dispatcher.Invoke(() => IsPlaying = e.IsPlaying);
        _pm.PositionChanged += (_, e) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            PositionSeconds = e.Position.TotalSeconds;
            DurationSeconds = e.Duration.TotalSeconds;
            UpdateActiveLine(e.Position);
        });
        _pm.LyricsChanged += (_, e) => OnLyricsChanged(e);
        _pm.LyricsCandidatesRequested += (_, e) => LyricsCandidatesRequested?.Invoke(this, e);
        _pm.StatusMessage += (_, msg) => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = msg);
    }

    private void OnCurrentChanged()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var t = _pm.CurrentTrack;
            CurrentTitle = string.IsNullOrWhiteSpace(t?.Title) ? "未播放" : t!.Title;
            CurrentArtist = t?.Artist ?? "";
            LyricsSearchText = BuildSearchText(CurrentArtist, CurrentTitle);
            PositionSeconds = 0;
            // 切歌先清空歌词，避免残留上一首。
            _lyricList = new List<LyricLineViewModel>();
            LyricsLines = new ObservableCollection<LyricLineViewModel>();
            LyricsSource = "歌词加载中…";
            LyricsHasTiming = false;
            _activeIdx = -1;
        });
    }

    private static string BuildSearchText(string artist, string title)
    {
        if (title == "未播放") return "";
        if (string.IsNullOrWhiteSpace(artist)) return title.Trim();
        return $"{artist.Trim()} - {title.Trim()}";
    }

    private void OnLyricsChanged(LyricsLoadedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var list = e.Lines.Select(LyricLineViewModel.From).ToList();
            _lyricList = list;
            LyricsLines = new ObservableCollection<LyricLineViewModel>(list);
            LyricsHasTiming = list.Any(l => l.Time > TimeSpan.Zero);
            _activeIdx = -1;
            LyricsSource = e.Source switch
            {
                "Local" => "歌词来源：本地 .lrc",
                "Embedded" => "歌词来源：音频内嵌",
                "Cloud" => "歌词来源：云盘 .lrc",
                "Lrclib" => "歌词来源：网络(LRCLIB)",
                "Online" => "歌词来源：网络",
                _ => _settings.Settings.OnlineLyricsEnabled
                    ? "未找到歌词（本地/云盘/网络均未匹配）"
                    : "无本地/云盘歌词 · 设置里可开启网络歌词",
            };
            // 立即按当前进度定位一次高亮行。
            UpdateActiveLine(TimeSpan.FromSeconds(PositionSeconds));
        });
    }

    /// <summary>根据当前进度二分查找并高亮当前歌词行；无时间轴（纯文本）时不高亮。</summary>
    private void UpdateActiveLine(TimeSpan pos)
    {
        if (!LyricsHasTiming || _lyricList.Count == 0) return;

        var lo = 0;
        var hi = _lyricList.Count - 1;
        var idx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_lyricList[mid].Time <= pos)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (idx == _activeIdx) return;
        if (_activeIdx >= 0 && _activeIdx < _lyricList.Count)
            _lyricList[_activeIdx].IsActive = false;
        if (idx >= 0)
            _lyricList[idx].IsActive = true;
        _activeIdx = idx;
    }

    // 播放/暂停合成一个按钮：根据当前状态执行相反动作。
    [RelayCommand]
    private void PlayPause() => _pm.PlayPause();

    [RelayCommand]
    private void Next() => _pm.Next();

    [RelayCommand]
    private void Previous() => _pm.Previous();

    [RelayCommand]
    private void CycleMode()
    {
        Mode = Mode switch
        {
            PlaybackMode.Sequential => PlaybackMode.RepeatAll,
            PlaybackMode.RepeatAll => PlaybackMode.RepeatOne,
            PlaybackMode.RepeatOne => PlaybackMode.Shuffle,
            _ => PlaybackMode.Sequential
        };
    }

    partial void OnModeChanged(PlaybackMode value)
    {
        ModeLabel = ToModeLabel(value);
        _settings.Settings.DefaultMode = value;
        _settings.Save();
    }

    partial void OnIsPlayingChanged(bool value) => PlayPauseLabel = value ? "暂停" : "播放";

    private static string ToModeLabel(PlaybackMode mode) => mode switch
    {
        PlaybackMode.Sequential => "顺序播放",
        PlaybackMode.RepeatAll => "列表循环",
        PlaybackMode.RepeatOne => "单曲循环",
        PlaybackMode.Shuffle => "随机播放",
        _ => "顺序播放"
    };

    partial void OnVolumeChanged(float value) => _pm.SetVolume(value);

    // 右侧歌词区手动搜索：先查候选；多组则弹选择框让用户挑；单组/无则直接处理。
    [RelayCommand]
    private async Task SearchLyrics()
    {
        if (string.IsNullOrWhiteSpace(LyricsSearchText)) return;
        var track = _pm.CurrentTrack;
        if (track is null) { LyricsSource = "请先播放一首歌曲再搜索歌词"; return; }

        LyricsSource = "歌词搜索中…";
        LyricsCandidate[] candidates;
        try { candidates = await _lyrics.SearchCandidatesAsync(LyricsSearchText, track, CancellationToken.None); }
        catch { candidates = Array.Empty<LyricsCandidate>(); }

        if (candidates.Length == 0) { LyricsSource = "未找到匹配歌词，可尝试更换关键词"; return; }
        if (candidates.Length == 1) { await LoadCandidateInternal(candidates[0], track); return; }

        // 多组候选：交给 View 弹出选择对话框（用户点击确定后回调 ChooseCandidate）。
        LyricsCandidatesRequested?.Invoke(this, new LyricsCandidatesRequestedEventArgs { Candidates = candidates });
    }

    /// <summary>用户在候选选择框中确认后，由 View 调用以加载选定的歌词。</summary>
    public void ChooseCandidate(LyricsCandidate candidate)
    {
        var track = _pm.CurrentTrack;
        if (track is null) return;
        _ = LoadCandidateInternal(candidate, track);
    }

    private async Task LoadCandidateInternal(LyricsCandidate candidate, Track track)
    {
        try
        {
            await _lyrics.LoadCandidateAsync(candidate, track, _pm.CurrentLocalPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[PlaybackViewModel] 加载候选歌词失败: " + ex.Message);
            LyricsSource = "歌词加载失败：" + ex.Message;
        }
    }

    public void Seek(TimeSpan position) => _pm.Seek(position);
    public void RefreshGain() => _pm.RefreshGain();
}
