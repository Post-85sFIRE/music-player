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
/// 跨设备以 (SourceType, FilePath) 作为同一首曲的稳定标识：本地曲用绝对路径，云曲用 WebDAV fileId，
/// 二者在各自设备上稳定，因此可正确去重、合并。
/// </summary>
public class CloudPlaylistSyncService
{
    /// <summary>云端存放播放列表的目录与文件名（相对 BaseUrl）。</summary>
    public const string RemoteFolder = "MusicPlayer";
    public const string RemoteFile = "MusicPlayer/playlist.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>把本地播放列表（含每首曲元数据）序列化并上传到云端，覆盖旧快照。</summary>
    public async Task PushAsync(ICloudSourceProvider provider, Playlist local, CancellationToken ct)
    {
        await provider.EnsureFolderAsync(RemoteFolder, ct);

        var dto = new CloudPlaylistDto
        {
            UpdatedUtc = DateTime.UtcNow,
            Items = local.Items.Select(ToDto).ToList()
        };

        var json = JsonSerializer.Serialize(dto, JsonOpts);
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

        var dto = JsonSerializer.Deserialize<CloudPlaylistDto>(text);
        if (dto?.Items is null || dto.Items.Count == 0)
            return new SyncResult { Added = 0, Message = "云端播放列表为空" };

        // 先收集本地已有的 TrackId，避免重复加入并准确统计新增数。
        var existingIds = new HashSet<long>(playlist.Current.Items.Select(i => i.TrackId));
        var toAdd = new List<long>();
        var added = 0;

        foreach (var item in dto.Items)
        {
            var track = await ResolveTrackAsync(item, tracks, ct);
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

    private static async Task<Track?> ResolveTrackAsync(CloudPlaylistItemDto item, ITrackRepository tracks, CancellationToken ct)
    {
        // 云曲与本地曲都按 FilePath 唯一（云曲 FilePath=fileId）。
        var existing = await tracks.GetByPathAsync(item.FilePath);
        if (existing is not null) return existing;

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

    private static CloudPlaylistItemDto ToDto(PlaylistItem item)
    {
        var t = item.Track;
        return new CloudPlaylistItemDto
        {
            SourceType = t?.SourceType ?? TrackSourceType.Local,
            SourceId = t?.SourceId,
            FilePath = t?.FilePath ?? "",
            Title = t?.Title ?? "",
            Artist = t?.Artist ?? "",
            Album = t?.Album ?? "",
            DurationSec = t?.DurationSec ?? 0,
            FileSize = t?.FileSize ?? 0,
            RgTrackGain = t?.ReplayGainTrackGain ?? 0,
            RgAlbumGain = t?.ReplayGainAlbumGain ?? 0,
            RgTrackPeak = t?.ReplayGainTrackPeak ?? 0,
            RgAlbumPeak = t?.ReplayGainAlbumPeak ?? 0
        };
    }
}

/// <summary>播放列表快照 DTO（落到云端 playlist.json）。</summary>
public class CloudPlaylistDto
{
    public string App { get; set; } = "MusicPlayer";
    public int Version { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<CloudPlaylistItemDto> Items { get; set; } = new();
}

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
