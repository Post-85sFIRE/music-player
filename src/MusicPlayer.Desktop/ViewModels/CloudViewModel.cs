using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using MusicPlayer.Data.Repositories;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop.ViewModels;

[ObservableObject]
public partial class CloudViewModel
{
    private readonly ISettingsService _settings;
    private readonly CloudSourceRegistry _registry;
    private readonly IPlaylistManager _playlist;
    private readonly TrackRepository _tracks;
    private readonly SpaceStatisticsService _space;
    private readonly IDownloadCacheService _cache;
    private readonly CloudPlaylistSyncService _sync = new();

    [ObservableProperty] private ObservableCollection<CloudSourceConfig> _sources = new();
    [ObservableProperty] private ObservableCollection<CloudEntry> _entries = new();
    [ObservableProperty] private CloudSourceConfig? _selectedSource;
    [ObservableProperty] private CloudEntry? _selectedEntry;
    [ObservableProperty] private string _currentFolder = "";
    [ObservableProperty] private string _currentFolderDisplay = "根目录";
    [ObservableProperty] private string _statusMessage = "未连接云盘";
    [ObservableProperty] private string _cacheUsage = "—";
    [ObservableProperty] private string _syncStatus = "";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newUrl = "";
    [ObservableProperty] private string _newUser = "";
    [ObservableProperty] private string _newPassword = "";

    public CloudViewModel(ISettingsService settings, CloudSourceRegistry registry, IPlaylistManager playlist,
        TrackRepository tracks, SpaceStatisticsService space, IDownloadCacheService cache)
    {
        _settings = settings;
        _registry = registry;
        _playlist = playlist;
        _tracks = tracks;
        _space = space;
        _cache = cache;
        _cache.Cached += OnCloudCached;
        NormalizeAndDedupSavedSources();
        foreach (var c in _settings.Settings.CloudSources) Sources.Add(c);
        _ = RefreshCacheAsync();
        _ = ReconnectSavedAsync();
    }

    /// <summary>启动时自动用本地保存的凭据重新登录已配置云源，避免每次打开都要手动填写。</summary>
    private async Task ReconnectSavedAsync()
    {
        foreach (var cfg in _settings.Settings.CloudSources)
        {
            // 组合根已在启动时把所有已存来源注册进 CloudSourceRegistry（含持久化凭据），
            // 这里即使已注册也主动做一次 LoginAsync 校验，确保连接状态真实可见（而非仅"已构造"）。
            var provider = _registry.Get(cfg.Id);
            bool ok;
            if (provider is not null)
            {
                ok = await provider.LoginAsync();
            }
            else
            {
                provider = new WebDavCloudProvider(cfg);
                ok = await provider.LoginAsync();
                if (ok) _registry.Register(provider);
            }

            if (ok)
            {
                SelectedSource ??= cfg;
                StatusMessage = $"[{cfg.Name}] 已自动连接";
            }
            else
            {
                StatusMessage = $"[{cfg.Name}] 自动重连失败：{provider.LastError}";
            }
        }
    }

    /// <summary>缓存服务落盘后回调：把对应云盘条目（sourceId = providerId|相对路径）的"已缓存"标记实时点亮。</summary>
    private void OnCloudCached(string sourceId)
    {
        var sep = sourceId.IndexOf('|');
        if (sep < 0) return;
        var pid = sourceId[..sep];
        var rel = sourceId[(sep + 1)..];
        if (SelectedSource is null || SelectedSource.Id != pid) return;
        var entry = Entries.FirstOrDefault(x => x.Id == rel && x.IsMusic);
        if (entry is null || entry.IsCached) return;
        // 缓存链路在后台线程触发，UI 集合/属性更新须回到 UI 线程。
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } disp)
            disp.Invoke(() => entry.IsCached = true);
        else
            entry.IsCached = true;
    }

    [RelayCommand]
    private async Task ConnectAsync(object? parameter)
    {
        NewPassword = parameter as string ?? NewPassword;

        if (string.IsNullOrWhiteSpace(NewUrl) || string.IsNullOrWhiteSpace(NewUser))
        {
            StatusMessage = "请填写 URL 与用户名（坚果云为应用密码）。";
            return;
        }

        var cfg = new CloudSourceConfig
        {
            ProviderType = "WebDav",
            Name = string.IsNullOrWhiteSpace(NewName) ? NewUrl : NewName,
            BaseUrl = NormalizeBaseUrl(NewUrl!),
            UserName = NewUser!,
            Password = NewPassword ?? ""
        };

        // 去重：同一规范 URL+用户名只保留一份。两侧都先 Normalize，避免已存旧 URL（含"我的坚果云"层）匹配不上。
        var dup = _settings.Settings.CloudSources.FirstOrDefault(c =>
            string.Equals(NormalizeBaseUrl(c.BaseUrl), cfg.BaseUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.UserName, cfg.UserName, StringComparison.OrdinalIgnoreCase));
        if (dup is not null)
        {
            _settings.Settings.CloudSources.Remove(dup);
            _registry.Unregister(dup.Id);
            Sources.Remove(dup);
        }

        var provider = new WebDavCloudProvider(cfg);
        StatusMessage = "正在连接 " + cfg.BaseUrl + " ...";
        var ok = await provider.LoginAsync();
        if (!ok)
        {
            StatusMessage = "连接失败：" + (provider.LastError ?? "请检查 URL / 用户名 / 应用密码，以及该目录是否存在。");
            return;
        }

        _settings.Settings.CloudSources.Add(cfg);
        _settings.Save();
        _registry.Register(provider);
        Sources.Add(cfg);
        SelectedSource = cfg;
        NewUrl = cfg.BaseUrl; // 把自动修正后的 URL 回填输入框，避免用户再看到带"我的坚果云"的旧值
        StatusMessage = "已连接：" + cfg.Name;
        // 选中来源后由 SelectionChanged 事件自动回填表单并浏览根目录，这里不再清空。
    }

    [RelayCommand]
    private async Task BrowseAsync(CloudEntry? entry)
    {
        if (SelectedSource is null) { StatusMessage = "请先连接一个云盘来源。"; return; }
        if (entry is null) { await BrowseRootAsync(); return; }
        if (!entry.IsFolder) return;
        SetFolder(entry.Id);
        await LoadEntriesAsync(entry.Id);
    }

    [RelayCommand]
    private async Task UpAsync()
    {
        if (SelectedSource is null) return;
        var parent = ParentOf(CurrentFolder);
        SetFolder(parent);
        await LoadEntriesAsync(parent);
    }

    [RelayCommand]
    private async Task AddToPlaylistAsync(CloudEntry? entry)
    {
        if (SelectedSource is null) { StatusMessage = "请先连接一个云盘来源。"; return; }
        if (entry is null) { StatusMessage = "未选中要加入的曲目。"; return; }
        if (entry.IsFolder) { StatusMessage = "文件夹不能加入播放列表，请进入文件夹后选择具体音乐文件。"; return; }
        try
        {
            var provider = await GetProviderAsync(SelectedSource);
            if (provider is null) return; // GetProviderAsync 已写入 StatusMessage
            var id = await EnsureTrackAsync(entry);
            var before = _playlist.Current.Items.Count;
            _playlist.AddTracks(new[] { id });
            var after = _playlist.Current.Items.Count;
            // 加入播放列表属于常规操作，用状态栏给出明确反馈（含序号），避免"点了没反应"的错觉。
            StatusMessage = after > before
                ? $"已加入播放列表：{entry.Name}（共 {after} 首）"
                : $"已在播放列表中：{entry.Name}（共 {after} 首）";
        }
        catch (Exception ex)
        {
            // AsyncRelayCommand 会吞掉异常、导致按钮"毫无反应"，这里把根因暴露到状态栏。
            StatusMessage = "加入失败：" + ex.Message;
        }
    }

    /// <summary>获取在线音乐：加入播放列表并立即播放（双击文件触发）。</summary>
    [RelayCommand]
    private async Task PlayEntryAsync(CloudEntry? entry)
    {
        if (SelectedSource is null) { StatusMessage = "请先连接一个云盘来源。"; return; }
        if (entry is null || entry.IsFolder) { StatusMessage = "请选择具体音乐文件。"; return; }
        try
        {
            var provider = await GetProviderAsync(SelectedSource);
            if (provider is null) return; // GetProviderAsync 已写入 StatusMessage
            var id = await EnsureTrackAsync(entry);
            _playlist.AddTracks(new[] { id });
            _playlist.MoveToPlay(id);
            StatusMessage = "正在播放：" + entry.Name;
        }
        catch (Exception ex)
        {
            StatusMessage = "播放失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 多选加入：遍历选中项——音乐直接加入；文件夹递归加入其下（含子目录）所有音乐。
    /// 命令参数来自 ListBox.SelectedItems（IList&lt;CloudEntry&gt;）。
    /// </summary>
    [RelayCommand]
    private async Task AddSelectedToPlaylistAsync(IList? selected)
    {
        if (SelectedSource is null) { StatusMessage = "请先连接一个云盘来源。"; return; }
        if (selected is null || selected.Count == 0) { StatusMessage = "未选中任何条目。"; return; }
        try
        {
            var provider = await GetProviderAsync(SelectedSource);
            if (provider is null) return; // GetProviderAsync 已写入 StatusMessage
            var added = 0;
            foreach (CloudEntry entry in selected)
            {
                if (entry.IsFolder) added += await AddFolderMusicToPlaylistAsync(provider, entry.Id);
                else if (entry.IsMusic)
                {
                    var id = await EnsureTrackAsync(entry);
                    _playlist.AddTracks(new[] { id });
                    added++;
                }
            }
            var total = _playlist.Current.Items.Count;
            StatusMessage = added > 0
                ? $"已加入 {added} 首（共 {total} 首）"
                : "选中没有可加入的音乐或文件夹。";
        }
        catch (Exception ex)
        {
            StatusMessage = "加入失败：" + ex.Message;
        }
    }

    /// <summary>右键菜单：把单个文件夹下（含子目录）的所有音乐加入播放列表。</summary>
    [RelayCommand]
    private async Task AddFolderMusicAsync(CloudEntry? folder)
    {
        if (SelectedSource is null) { StatusMessage = "请先连接一个云盘来源。"; return; }
        if (folder is null || !folder.IsFolder) { StatusMessage = "请选择文件夹。"; return; }
        try
        {
            var provider = await GetProviderAsync(SelectedSource);
            if (provider is null) return;
            var added = await AddFolderMusicToPlaylistAsync(provider, folder.Id);
            var total = _playlist.Current.Items.Count;
            StatusMessage = added > 0
                ? $"已加入文件夹下 {added} 首（共 {total} 首）"
                : "该文件夹下没有音乐文件。";
        }
        catch (Exception ex)
        {
            StatusMessage = "加入失败：" + ex.Message;
        }
    }

    /// <summary>从指定文件夹 BFS 递归收集其下（含子目录）所有音乐并加入播放列表，返回加入数量。</summary>
    private async Task<int> AddFolderMusicToPlaylistAsync(ICloudSourceProvider provider, string folderId)
    {
        var added = 0;
        var queue = new Queue<string>();
        queue.Enqueue(folderId);
        while (queue.Count > 0)
        {
            var fid = queue.Dequeue();
            var list = await provider.BrowseAsync(fid, CancellationToken.None);
            foreach (var e in list)
            {
                if (e.IsFolder) queue.Enqueue(e.Id);
                else if (e.IsMusic)
                {
                    var id = await EnsureTrackAsync(e);
                    _playlist.AddTracks(new[] { id });
                    added++;
                }
            }
        }
        return added;
    }

    /// <summary>把云条目转为本地 Track（云源 FilePath=fileId，按 FilePath 去重）。</summary>
    private async Task<long> EnsureTrackAsync(CloudEntry entry)
    {
        var track = new Track
        {
            Title = Path.GetFileNameWithoutExtension(entry.Name),
            FilePath = entry.Id,
            FileSize = entry.Size,
            SourceType = TrackSourceType.Cloud,
            SourceId = SelectedSource!.Id,
            DateAddedUtc = System.DateTime.UtcNow
        };
        return await _tracks.UpsertAsync(track);
    }

    [RelayCommand]
    private async Task RefreshCacheAsync()
    {
        var path = _settings.Settings.DownloadPath;
        var (bytes, files) = await _space.ComputeAsync(path);
        CacheUsage = string.IsNullOrWhiteSpace(path)
            ? $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB / {files} 个文件（默认缓存目录）"
            : $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB / {files} 个文件";
    }

    private async Task BrowseRootAsync()
    {
        SetFolder("");
        await LoadEntriesAsync("");
    }

    private void SetFolder(string folder)
    {
        CurrentFolder = folder;
        CurrentFolderDisplay = string.IsNullOrEmpty(folder) ? "根目录" : folder;
    }

    /// <summary>启动时对本地已保存来源做归一化 + 去重并回写，根治重连 404 与"我的音乐 ×2"。</summary>
    private void NormalizeAndDedupSavedSources()
    {
        if (_settings.Settings.CloudSources.Count == 0) return;

        // 1) 先归一化 URL，记录是否发生变化
        var changed = false;
        foreach (var c in _settings.Settings.CloudSources)
        {
            var norm = NormalizeBaseUrl(c.BaseUrl);
            if (!string.Equals(norm, c.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                c.BaseUrl = norm;
                changed = true;
            }
        }

        // 2) 按 规范化 BaseUrl(小写) + UserName(小写) 去重
        var deduped = _settings.Settings.CloudSources
            .GroupBy(c => (c.BaseUrl.ToLowerInvariant(), (c.UserName ?? "").ToLowerInvariant()))
            .Select(g => g.First())
            .ToList();
        if (deduped.Count != _settings.Settings.CloudSources.Count) changed = true;

        if (!changed) return; // 无变化，避免无谓写盘

        _settings.Settings.CloudSources.Clear();
        _settings.Settings.CloudSources.AddRange(deduped);
        _settings.Save();
    }

    private async Task LoadEntriesAsync(string folderId)
    {
        if (SelectedSource is null) return;
        var provider = await GetProviderAsync(SelectedSource);
        if (provider is null) return; // GetProviderAsync 已设置 StatusMessage
        try
        {
            // 云盘浏览器只显示文件夹和音乐文件，隐藏 .lrc/.txt/.jpg 等非音乐文件。
            var list = await provider.BrowseAsync(folderId, CancellationToken.None);
            var sorted = list
                .Where(e => e.IsFolder || e.IsMusic)
                .OrderBy(e => !e.IsFolder)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Entries.Clear();
            foreach (var e in sorted)
            {
                // 标记"已缓存"：命中本地缓存目录（与播放端同一缓存 key）的曲目标为已下载。
                if (!e.IsFolder)
                    e.IsCached = _cache.IsCached(provider.Id + "|" + e.Id);
                Entries.Add(e);
            }
            StatusMessage = $"已列出 {Entries.Count} 项";
        }
        catch (System.Exception ex)
        {
            StatusMessage = "浏览失败：" + ex.Message;
        }
    }

    private static string ParentOf(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return "";
        var trimmed = folder.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx <= 0 ? "" : trimmed[..idx];
    }

    /// <summary>
    /// 规范化用户输入的 WebDAV 根地址：去空白、反斜杠转斜杠、补尾斜杠；
    /// 同时处理坚果云客户端常见误区——"我的坚果云"在 WebDAV 中不是真实目录层，自动去掉。
    /// 以大小写不敏感的 "/dav/" 为根；其后若紧跟"我的坚果云/"显示名层则丢弃（兼容缺尾斜杠/后带子路径）。
    /// </summary>
    private static string NormalizeBaseUrl(string raw)
    {
        var url = (raw ?? "").Trim().Replace('\\', '/');
        const string marker = "/dav/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var prefix = url[..idx];                 // host 部分
            var rest = url[(idx + marker.Length)..];  // 去掉 "/dav/"
            var seg = rest.TrimStart('/');
            const string junk = "我的坚果云/";
            if (seg.StartsWith(junk, StringComparison.OrdinalIgnoreCase))
                seg = seg[junk.Length..];
            url = prefix + "/dav/" + seg;
        }
        if (!url.EndsWith('/')) url += "/";
        return url;
    }

    // ===== 播放列表云同步 =====

    /// <summary>把本地播放列表上传播到坚果云（覆盖云端快照）。</summary>
    [RelayCommand]
    private async Task SyncPushAsync()
    {
        var provider = await GetProviderAsync(SelectedSource);
        if (provider is null) return;
        try
        {
            SyncStatus = "正在上传播放列表...";
            await _sync.PushAsync(provider, _playlist.Current, CancellationToken.None);
            SyncStatus = $"已上传播放列表（{_playlist.Current.Items.Count} 首）到云端";
        }
        catch (System.Exception ex)
        {
            SyncStatus = "上传失败：" + ex.Message;
        }
    }

    /// <summary>从坚果云下载播放列表快照并合并到本地（云端新增追加，不删本地曲）。</summary>
    [RelayCommand]
    private async Task SyncPullAsync()
    {
        var provider = await GetProviderAsync(SelectedSource);
        if (provider is null) return;
        try
        {
            SyncStatus = "正在从云端下载播放列表...";
            var r = await _sync.PullAsync(provider, _playlist, _tracks, CancellationToken.None);
            SyncStatus = r.Message;
        }
        catch (System.Exception ex)
        {
            SyncStatus = "下载失败：" + ex.Message;
        }
    }

    /// <summary>按需取 provider：已连接则直接用；否则用保存的配置重建并登录（支持重启后同步）。</summary>
    private async Task<ICloudSourceProvider?> GetProviderAsync(CloudSourceConfig? cfg)
    {
        if (cfg is null) { StatusMessage = "请先连接一个云盘来源。"; return null; }
        var existing = _registry.Get(cfg.Id);
        if (existing is not null) return existing;

        var provider = new WebDavCloudProvider(cfg);
        if (!await provider.LoginAsync())
        {
            StatusMessage = "云盘连接已失效，请重新连接。";
            return null;
        }
        _registry.Register(provider);
        return provider;
    }
}
