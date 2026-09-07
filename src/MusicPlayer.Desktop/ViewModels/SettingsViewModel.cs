using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

[ObservableObject]
public partial class SettingsViewModel
{
    private readonly ISettingsService _settings;
    private readonly SpaceStatisticsService _space;
    private readonly PlaybackManager _playback;

    [ObservableProperty] private string _downloadPath = "";
    [ObservableProperty] private string _usedSpace = "—";
    [ObservableProperty] private bool _exclusiveWasapi;
    [ObservableProperty] private float _volume;
    [ObservableProperty] private ReplayGainMode _replayGainMode;
    [ObservableProperty] private string _replayGainLabel = "关闭增益";

    // 歌词设置
    [ObservableProperty] private string _lyricsPath = "";
    [ObservableProperty] private bool _onlineLyricsEnabled;
    [ObservableProperty] private bool _autoUploadCloudLyrics = true;

    public Array ReplayGainModes => Enum.GetValues(typeof(ReplayGainMode));

    public SettingsViewModel(ISettingsService settings, SpaceStatisticsService space, PlaybackManager playback)
    {
        _settings = settings;
        _space = space;
        _playback = playback;
        _downloadPath = settings.Settings.DownloadPath;
        _exclusiveWasapi = settings.Settings.ExclusiveWasapi;
        _volume = settings.Settings.Volume;
        _replayGainMode = settings.Settings.ReplayGainMode;
        ReplayGainLabel = ToReplayGainLabel(_replayGainMode);
        _lyricsPath = settings.Settings.LyricsPath;
        _onlineLyricsEnabled = settings.Settings.OnlineLyricsEnabled;
        _autoUploadCloudLyrics = settings.Settings.AutoUploadCloudLyrics;
    }

    [RelayCommand]
    private void Browse()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;
        DownloadPath = dlg.SelectedPath;
        _settings.Settings.DownloadPath = dlg.SelectedPath;
        _settings.Save();
        _ = RefreshSpaceAsync();
    }

    [RelayCommand]
    private async Task RefreshSpaceAsync()
    {
        var (bytes, files) = await _space.ComputeAsync(_settings.Settings.DownloadPath);
        UsedSpace = $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB / {files} 个文件";
    }

    partial void OnExclusiveWasapiChanged(bool value)
    {
        _settings.Settings.ExclusiveWasapi = value;
        _settings.Save();
        // 运行时即时切换音频输出模式（独占/共享），无需重启程序；
        // 重新初始化在后台线程执行，结束会弹窗告知最终结果（独占成功 / 自动退回共享）。
        _ = _playback.ApplyAudioModeChangeAsync(value);
    }

    partial void OnVolumeChanged(float value)
    {
        _settings.Settings.Volume = value;
        _settings.Save();
    }

    [RelayCommand]
    private void BrowseLyrics()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;
        LyricsPath = dlg.SelectedPath;
        _settings.Settings.LyricsPath = dlg.SelectedPath;
        _settings.Save();
    }

    partial void OnLyricsPathChanged(string value)
    {
        _settings.Settings.LyricsPath = value;
        _settings.Save();
    }

    partial void OnOnlineLyricsEnabledChanged(bool value)
    {
        _settings.Settings.OnlineLyricsEnabled = value;
        _settings.Save();
    }

    partial void OnAutoUploadCloudLyricsChanged(bool value)
    {
        _settings.Settings.AutoUploadCloudLyrics = value;
        _settings.Save();
    }

    partial void OnReplayGainModeChanged(ReplayGainMode value)
    {
        _settings.Settings.ReplayGainMode = value;
        _settings.Save();
        ReplayGainLabel = ToReplayGainLabel(value);
        ReplayGainModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>控制条上的增益模式按钮：关 → 曲目 → 专辑 循环。</summary>
    [RelayCommand]
    private void CycleReplayGain()
    {
        ReplayGainMode = ReplayGainMode switch
        {
            ReplayGainMode.Off => ReplayGainMode.Track,
            ReplayGainMode.Track => ReplayGainMode.Album,
            _ => ReplayGainMode.Off
        };
    }

    /// <summary>切换增益模式后通知播放管理器即时重算当前曲音量（若正在播放）。</summary>
    public event EventHandler? ReplayGainModeChanged;

    private static string ToReplayGainLabel(ReplayGainMode mode) => mode switch
    {
        ReplayGainMode.Off => "关闭增益",
        ReplayGainMode.Track => "曲目增益",
        ReplayGainMode.Album => "专辑增益",
        _ => "关闭增益"
    };
}
