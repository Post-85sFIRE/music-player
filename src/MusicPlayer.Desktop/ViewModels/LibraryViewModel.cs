using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

[ObservableObject]
public partial class LibraryViewModel
{
    private readonly ILibraryScanner _scanner;
    private readonly ITrackRepository _tracks;
    private readonly IPlaylistManager _playlist;

    [ObservableProperty] private ObservableCollection<Track> _tracksList = new();
    [ObservableProperty] private string _scanStatus = "未扫描";
    [ObservableProperty] private ObservableCollection<LibraryFolder> _folders = new();

    public LibraryViewModel(ILibraryScanner scanner, ITrackRepository tracks, IPlaylistManager playlist)
    {
        _scanner = scanner;
        _tracks = tracks;
        _playlist = playlist;
    }

    public void LoadTracks()
    {
        TracksList.Clear();
        foreach (var t in _tracks.GetAllAsync().GetAwaiter().GetResult()) TracksList.Add(t);
    }

    [RelayCommand]
    private void AddFolder()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var folder = new LibraryFolder { Path = dlg.SelectedPath, Recursive = true };
        ScanStatus = "扫描中…";
        _scanner.ScanFolderAsync(folder, null, CancellationToken.None).GetAwaiter().GetResult();
        LoadTracks();
        Folders.Add(folder);
        ScanStatus = $"已扫描，共 {TracksList.Count} 首";
    }

    [RelayCommand]
    private void ScanAll()
    {
        ScanStatus = "扫描中…";
        _scanner.ScanAllAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        LoadTracks();
        ScanStatus = $"完成，共 {TracksList.Count} 首";
    }

    [RelayCommand]
    private void AddToPlaylist(Track track)
    {
        // 加入播放列表属于常规操作，不打扰用户（不弹窗）；真正的错误才提示。
        _playlist.AddTracks(new[] { track.Id });
    }
}
