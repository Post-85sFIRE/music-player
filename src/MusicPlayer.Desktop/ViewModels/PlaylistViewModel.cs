using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

[ObservableObject]
public partial class PlaylistViewModel
{
    private readonly IPlaylistManager _playlist;
    private readonly PlaybackManager _pm;
    private readonly ITrackRepository _tracks;
    private readonly ILibraryScanner _scanner;
    private readonly CloudSourceRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly IDownloadCacheService _cache;
    private readonly CloudPlaylistSyncService _sync = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<PlaylistRow> _items = new();
    [ObservableProperty] private long? _currentItemId;
    [ObservableProperty] private string _nowPlayingCaption = "未播放";
    [ObservableProperty] private string _syncStatus = "";

    public ICollectionView ItemsView { get; }

    public PlaylistViewModel(IPlaylistManager playlist, PlaybackManager pm, ITrackRepository tracks, ILibraryScanner scanner,
        CloudSourceRegistry registry, ISettingsService settings, IDownloadCacheService cache)
    {
        _playlist = playlist;
        _pm = pm;
        _tracks = tracks;
        _scanner = scanner;
        _registry = registry;
        _settings = settings;
        _cache = cache;
        _cache.Cached += OnCached;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => Filter(o as PlaylistRow);
        _playlist.ItemsChanged += OnPlaylistChanged;
        _pm.CurrentTrackChanged += OnCurrentTrackChanged;
        Refresh();
        UpdatePlayState();
    }

    private void OnPlaylistChanged(object? sender, EventArgs e)
    {
        // ItemsChanged 可能从非 UI 线程触发（云盘加入/同步等异步路径），
        // 直接改 ObservableCollection 会抛 NotSupportedException 并被上游 AsyncRelayCommand 吞掉，
        // 表现为"点了没反应"。统一回到 Dispatcher 执行 Refresh。
        var app = System.Windows.Application.Current;
        if (app is null) { Refresh(); return; }
        if (app.Dispatcher.CheckAccess()) Refresh();
        else app.Dispatcher.Invoke(Refresh);
    }

    private void Refresh()
    {
        Items.Clear();
        var idx = 1;
        foreach (var i in _playlist.Current.Items)
        {
            var row = new PlaylistRow(i, idx++);
            if (i.Track is { SourceType: TrackSourceType.Cloud } t
                && !string.IsNullOrEmpty(t.SourceId)
                && !string.IsNullOrEmpty(t.FilePath))
            {
                row.IsCached = _cache.IsCached(t.SourceId + "|" + t.FilePath);
            }
            Items.Add(row);
        }
        ItemsView.Refresh();
        UpdateCaption();
    }

    /// <summary>缓存落盘完成后，把播放列表里对应云曲目点亮"已缓存"标记。</summary>
    private void OnCached(string sourceId)
    {
        // sourceId 约定为 "providerId|filePath"
        var parts = sourceId.Split('|', 2);
        if (parts.Length < 2) return;
        var providerId = parts[0];
        var filePath = parts[1];

        var app = System.Windows.Application.Current;
        if (app is null) return;

        void Apply()
        {
            foreach (var row in Items)
            {
                var t = row.Item.Track;
                if (t?.SourceType == TrackSourceType.Cloud
                    && t.SourceId == providerId
                    && t.FilePath == filePath)
                {
                    row.IsCached = true;
                }
            }
        }

        if (app.Dispatcher.CheckAccess()) Apply();
        else app.Dispatcher.Invoke(Apply);
    }

    private bool Filter(PlaylistRow? i)
    {
        if (i is null) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.ToLowerInvariant();
        return (i.Item.Track?.Title ?? "").ToLowerInvariant().Contains(q)
            || (i.Item.Track?.Artist ?? "").ToLowerInvariant().Contains(q)
            || (i.Item.Track?.Album ?? "").ToLowerInvariant().Contains(q);
    }

    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "音频文件|*.mp3;*.flac;*.ape;*.wav;*.ogg;*.oga;*.m4a;*.aac;*.opus;*.wma;*.dsf;*.dff"
        };
        if (dlg.ShowDialog() == true)
        {
            _playlist.AddTracks(ResolveIds(dlg.FileNames));
            Refresh();
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var folder = new LibraryFolder { Path = dlg.SelectedPath, Recursive = true };
            _scanner.ScanFolderAsync(folder, null, CancellationToken.None).GetAwaiter().GetResult();
            var all = _tracks.GetAllAsync().GetAwaiter().GetResult();
            _playlist.AddTracks(all.Select(t => t.Id));
            Refresh();
        }
    }

    [RelayCommand]
    private void Remove(PlaylistRow? row)
    {
        if (row is null) return;
        _playlist.RemoveItem(row.Item.Id);
        Refresh();
    }

    /// <summary>多选移除：命令参数来自 ListBox.SelectedItems（IList&lt;PlaylistRow&gt;）。</summary>
    [RelayCommand]
    private void RemoveSelected(IList? selected)
    {
        if (selected is null || selected.Count == 0) return;
        var ids = selected.Cast<PlaylistRow>().Select(r => r.Item.Id).ToList();
        foreach (var id in ids) _playlist.RemoveItem(id);
        Refresh();
    }

    [RelayCommand]
    private void Clear()
    {
        _playlist.Clear();
        Refresh();
    }

    [RelayCommand]
    private void Play(PlaylistRow? row)
    {
        if (row is null) return;
        _pm.PlayItem(row.Item.Id);
    }

    public void Reorder(PlaylistRow source, int targetIndex)
    {
        _playlist.Reorder(source.Item.Id, targetIndex);
        Refresh();
    }

    private List<long> ResolveIds(IEnumerable<string> paths)
    {
        var ids = new List<long>();
        foreach (var p in paths)
        {
            var existing = _tracks.GetByPathAsync(p).GetAwaiter().GetResult();
            if (existing is not null) { ids.Add(existing.Id); continue; }

            var t = new Track
            {
                Title = Path.GetFileNameWithoutExtension(p),
                FilePath = p,
                DateAddedUtc = DateTime.UtcNow
            };
            ids.Add(_tracks.UpsertAsync(t).GetAwaiter().GetResult());
        }

        return ids;
    }

    // ===== 当前播放状态 / 序号 / 云同步 / 下载 =====

    private void OnCurrentTrackChanged(object? sender, EventArgs e)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) UpdatePlayState();
        else app.Dispatcher.Invoke(UpdatePlayState);
    }

    private void UpdatePlayState()
    {
        CurrentItemId = _pm.CurrentItemId;
        UpdateCaption();
    }

    private void UpdateCaption()
    {
        var cur = _pm.CurrentItem;
        if (cur is null) { NowPlayingCaption = "未播放"; return; }
        // 按 TrackId 匹配当前曲目（引用未必相同），取连续序号；无匹配则只显示总数。
        var row = Items.FirstOrDefault(r => r.Item.TrackId == cur.TrackId);
        var n = row?.Index ?? 0;
        NowPlayingCaption = n > 0
            ? $"正在播放 第{n}首 / 共{Items.Count}首"
            : $"正在播放 / 共{Items.Count}首";
    }

    [RelayCommand]
    private async Task SaveToCloudAsync()
    {
        var p = await GetCloudProviderAsync();
        if (p is null) return;
        try
        {
            await _sync.PushAsync(p, _playlist.Current, CancellationToken.None);
            SyncStatus = "已保存到云盘";
        }
        catch (Exception ex) { SyncStatus = "保存失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task LoadFromCloudAsync()
    {
        var p = await GetCloudProviderAsync();
        if (p is null) return;
        try
        {
            var r = await _sync.PullAsync(p, _playlist, _tracks, CancellationToken.None);
            SyncStatus = r.Message;
            Refresh(); // 合并后确保列表刷新
        }
        catch (Exception ex) { SyncStatus = "加载失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DownloadAsync(PlaylistRow? row)
    {
        var item = row?.Item;
        if (item?.Track is null) return;
        var t = item.Track;
        if (t.SourceType != TrackSourceType.Cloud) return;
        var p = await GetProviderByIdAsync(t.SourceId);
        if (p is null) { SyncStatus = "无法连接云盘，下载取消"; return; }

        var dir = string.IsNullOrWhiteSpace(_settings.Settings.DownloadPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MusicPlayerCache")
            : _settings.Settings.DownloadPath;
        Directory.CreateDirectory(dir);
        var ext = string.IsNullOrEmpty(Path.GetExtension(t.FilePath)) ? ".mp3" : Path.GetExtension(t.FilePath);
        var name = string.Join("_", (t.Title ?? "track").Split(Path.GetInvalidFileNameChars()));
        var dest = Path.Combine(dir, name + ext);
        try { await p.DownloadAsync(t.FilePath, dest, null, CancellationToken.None); }
        catch (Exception ex) { SyncStatus = "下载失败：" + ex.Message; return; }

        t.SourceType = TrackSourceType.Local;
        t.FilePath = dest;
        t.SourceId = null;
        await _tracks.UpsertAsync(t);
        SyncStatus = "已下载到本地：" + dest; // Track 同引用，徽标/按钮自动刷新
    }

    private async Task<ICloudSourceProvider?> GetCloudProviderAsync()
    {
        var cfg = _settings.Settings.CloudSources.FirstOrDefault();
        if (cfg is null) { SyncStatus = "尚未配置云盘来源"; return null; }
        var existing = _registry.Get(cfg.Id);
        if (existing is not null) return existing;
        var p = new WebDavCloudProvider(cfg);
        if (!await p.LoginAsync()) { SyncStatus = "云盘连接失败：" + p.LastError; return null; }
        _registry.Register(p);
        return p;
    }

    private async Task<ICloudSourceProvider?> GetProviderByIdAsync(string? sourceId)
    {
        if (sourceId is null) return await GetCloudProviderAsync();
        var existing = _registry.Get(sourceId);
        if (existing is not null) return existing;
        var cfg = _settings.Settings.CloudSources.FirstOrDefault(c => c.Id == sourceId);
        if (cfg is null) return await GetCloudProviderAsync();
        var p = new WebDavCloudProvider(cfg);
        if (!await p.LoginAsync()) return null;
        _registry.Register(p);
        return p;
    }
}
