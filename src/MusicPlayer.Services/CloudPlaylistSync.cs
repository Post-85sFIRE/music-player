using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services;

/// <summary>
/// 播放列表云同步快照（坚果云 WebDAV）。
/// 文件格式与鸿蒙端保持一致：云端 playlist.json 为根级 JSON 数组（camelCase），
/// 每项字段对齐鸿蒙 SongData（id/title/artist/duration/source/path/cloudRelPath 等）。
/// 跨设备以 (SourceType, FilePath) 作为同一首曲的稳定标识：本地曲用绝对路径，云曲用 WebDAV URL，
/// 二者在各自设备上稳定，因此可正确去重、合并。
/// 为兼容本程序早期版本写出的 {App,Version,Items} 包装格式，Pull 会先尝试根级数组，失败再回退到包装格式。
/// </summary>
public class CloudPlaylistSyncService
{
    /// <summary>云端存放播放列表的目录与文件名（相对 BaseUrl），固定为 yunMusicPlayer（与鸿蒙版本一致）。</summary>
    public const string RemoteFolder = CloudPaths.AppRoot;
    public const string RemoteFile = CloudPaths.PlaylistFile;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CamelOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>把本地播放列表（含每首曲元数据）序列化为根级数组并上传到云端，覆盖旧快照。格式与鸿蒙端一致。</summary>
    public async Task PushAsync(ICloudSourceProvider provider, Playlist local, CancellationToken ct)
    {
        await provider.EnsureFolderAsync(RemoteFolder, ct);

        var arr = local.Items.Select(ToCloudSong).ToArray();
        var json = JsonSerializer.Serialize(arr, CamelOpts);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await provider.UploadAsync(RemoteFile, ms, ct);
    }

    /// <summary>从云端下载播放列表快照并合并到本地：按 (SourceType, FilePath) 去重，云端新增追加到本地末尾。不删除本地已有曲。</summary>
    public async Task<SyncResult> PullAsync(ICloudSourceProvider provider, IPlaylistManager playlist,
        ITrackRepository tracks, CancellationToken ct)
    {
        var text = await provider.GetTextAsync(RemoteFile, ct);
        if (string.IsNullOrWhiteSpace(text))
            return new SyncResult { Added = 0, Message = "云端暂无播放列表文件" };

        // 优先按鸿蒙根级数组格式解析；失败则兼容本程序早期写出的 {App,Version,Items} 包装格式。
        List<CloudPlaylistItemDto> items;
        try
        {
            var arr = JsonSerializer.Deserialize<CloudSongItem[]>(text, CamelOpts);
            items = arr is { Length: > 0 } ? arr.Select(s => ToItemDto(s, provider.Id)).ToList() : new List<CloudPlaylistItemDto>();
        }
        catch
        {
            var dto = JsonSerializer.Deserialize<CloudPlaylistDto>(text);
            items = dto?.Items ?? new List<CloudPlaylistItemDto>();
        }

        if (items.Count == 0)
            return new SyncResult { Added = 0, Message = "云端播放列表为空" };

        // 先收集本地已有的 TrackId，避免重复加入并准确统计新增数。
        var existingIds = new HashSet<long>(playlist.Current.Items.Select(i => i.TrackId));
        var toAdd = new List<long>();
        var added = 0;

        foreach (var item in items)
        {
            var track = await ResolveTrackAsync(item, provider.Id, tracks, ct);
            if (track is null) continue;
            if (existingIds.Contains(track.Id)) continue;
            existingIds.Add(track.Id);
            toAdd.Add(track.Id);
            added++;
        }

        if (toAdd.Count > 0)
            playlist.AddTracks(toAdd);

        return new SyncResult
        {
            Added = added,
            Message = added > 0
                ? $"已从云端合并 {added} 首，本地共 {playlist.Current.Items.Count} 首"
                : "云端播放列表已是最新，无新增"
        };
    }

    private static async Task<Track?> ResolveTrackAsync(CloudPlaylistItemDto item, string providerId, ITrackRepository tracks, CancellationToken ct)
    {
        // 云曲与本地曲都按 FilePath 唯一（云曲 FilePath=WebDAV URL）。
        var existing = await tracks.GetByPathAsync(item.FilePath);
        if (existing is not null)
        {
            // 脏数据修复：历史版本曾把云盘文件相对路径误存进 SourceId（正确值应为 provider.Id）。
            // 命中已存在的云曲时，若 SourceId 与当前 provider 不一致，就地更正，否则播放时找不到云源。
            if (existing.SourceType == TrackSourceType.Cloud
                && !string.IsNullOrEmpty(providerId)
                && existing.SourceId != providerId)
            {
                existing.SourceId = providerId;
                await tracks.UpsertAsync(existing);
            }
            return existing;
        }

        var track = new Track
        {
            Title = item.Title,
            Artist = item.Artist,
            Album = item.Album,
            FilePath = item.FilePath,
            FileSize = item.FileSize,
            DurationSec = item.DurationSec,
            SourceType = item.SourceType,
            SourceId = item.SourceId,
            ReplayGainTrackGain = item.RgTrackGain,
            ReplayGainAlbumGain = item.RgAlbumGain,
            ReplayGainTrackPeak = item.RgTrackPeak,
            ReplayGainAlbumPeak = item.RgAlbumPeak,
            DateAddedUtc = DateTime.UtcNow
        };
        track.Id = await tracks.UpsertAsync(track);
        return track;
    }

    #region 鸿蒙根级数组 <-> 内部 DTO 映射
    /// <summary>鸿蒙端 SongData 单项（camelCase）。写入云端 playlist.json 即采用此结构，保证与鸿蒙一致、可双向互通。</summary>
    private class CloudSongItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Duration { get; set; } = "";   // "m:ss"
        public string Path { get; set; } = "";
        public string Source { get; set; } = "cloud";
        public bool Cached { get; set; }
        public string? CloudRelPath { get; set; }
        public string? LyricPath { get; set; }
        public double ReplayGain { get; set; }
        public string? LocalPath { get; set; }
    }

    private static CloudPlaylistItemDto ToItemDto(CloudSongItem s, string providerId) => new()
    {
        SourceType = s.Source == "local" ? TrackSourceType.Local : TrackSourceType.Cloud,
        SourceId = providerId,
        FilePath = s.Path,
        Title = s.Title,
        Artist = s.Artist,
        Album = "",
        DurationSec = ParseDuration(s.Duration),
        FileSize = 0,
        RgTrackGain = s.ReplayGain,
        RgAlbumGain = 0,
        RgTrackPeak = 0,
        RgAlbumPeak = 0
    };

    private static CloudSongItem ToCloudSong(PlaylistItem item)
    {
        var t = item.Track;
        return new CloudSongItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = t?.Title ?? "",
            Artist = t?.Artist ?? "",
            Duration = FormatDuration(t?.DurationSec ?? 0),
            Path = t?.FilePath ?? "",
            Source = t?.SourceType == TrackSourceType.Cloud ? "cloud" : "local",
            Cached = false,
            CloudRelPath = t?.SourceType == TrackSourceType.Cloud ? t.SourceId : null,
            LyricPath = null,
            ReplayGain = t?.ReplayGainTrackGain ?? 0,
            LocalPath = null
        };
    }

    /// <summary>解析 "m:ss" / "h:mm:ss" 为秒。</summary>
    private static double ParseDuration(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        double sec = 0;
        foreach (var part in s.Split(':'))
            if (double.TryParse(part, out var v)) sec = sec * 60 + v;
        return sec;
    }

    /// <summary>秒 -> "m:ss"。</summary>
    private static string FormatDuration(double sec)
    {
        var total = (int)Math.Round(sec);
        return $"{total / 60}:{total % 60:D2}";
    }
    #endregion

    #region 旧版 Windows 包装格式（仅 Pull 兼容，不再写出）
    /// <summary>播放列表快照 DTO（旧版 Windows 写出的 {App,Version,Items} 包装，仅用于读取兼容）。</summary>
    public class CloudPlaylistDto
    {
        public string App { get; set; } = "MusicPlayer";
        public int Version { get; set; } = 1;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<CloudPlaylistItemDto> Items { get; set; } = new();
    }
    #endregion
}

/// <summary>播放列表快照内部统一项（旧版包装格式的元素，亦作为根级数组解析后的归一化中间结构）。</summary>
public class CloudPlaylistItemDto
{
    public TrackSourceType SourceType { get; set; }
    public string? SourceId { get; set; }
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public double DurationSec { get; set; }
    public long FileSize { get; set; }
    public double RgTrackGain { get; set; }
    public double RgAlbumGain { get; set; }
    public double RgTrackPeak { get; set; }
    public double RgAlbumPeak { get; set; }
}

public class SyncResult
{
    public int Added { get; set; }
    public string Message { get; set; } = "";
}
