using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

public class MainViewModel
{
    public PlaybackViewModel Playback { get; }
    public PlaylistViewModel Playlist { get; }
    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel(PlaybackViewModel playback, PlaylistViewModel playlist,
        LibraryViewModel library, SettingsViewModel settings)
    {
        Playback = playback;
        Playlist = playlist;
        Library = library;
        Settings = settings;
        Library.LoadTracks();

        // 控制条切换 ReplayGain 模式时，即时重算当前曲音量（若正在播放）。
        settings.ReplayGainModeChanged += (_, _) => playback.RefreshGain();
    }
}
