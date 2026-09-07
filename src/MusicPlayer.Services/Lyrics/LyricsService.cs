using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;
using MusicPlayer.Services.Lyrics;

namespace MusicPlayer.Services;

/// <summary>
/// 歌词服务：编排三层来源（本地 .lrc / 内嵌标签 → 云盘同目录 .lrc → 网络 API），
/// 并把拿到的歌词写回（让歌词跟着歌走）：
///   - 本地曲：从云盘/网络/内嵌拿到歌词时，存一份 .lrc 到音频同目录（下次本地优先）。
///   - 云盘曲：从网络/内嵌拿到歌词时，自动上传 .lrc 到云盘同目录（受 AutoUploadCloudLyrics 开关控制）。
/// 触发 LyricsChanged 事件供 UI 订阅；写回在后台线程执行，失败静默不影响播放。
/// </summary>
public class LyricsService
{
    private readonly ISettingsService _settings;
    private readonly CloudSourceRegistry _cloud;
    private readonly ILyricsProvider[] _providers;

    public event EventHandler<LyricsLoadedEventArgs>? LyricsChanged;

    public LyricsService(ISettingsService settings, CloudSourceRegistry cloud)
    {
        _settings = settings;
        _cloud = cloud;
        // 顺序即优先级：本地/内嵌 → 云盘 → 网络。
        _providers = new ILyricsProvider[]
        {
            new EmbeddedTagProvider(),
            new LocalLrcProvider(),
            new CloudLrcProvider(),
            new LrclibLyricsProvider(),
            new OnlineLyricsProvider(),
        };
    }

    /// <summary>加载某曲歌词并在加载完成后触发 <see cref="LyricsChanged"/>；随后后台写回（如启用）。</summary>
    public async Task LoadAsync(Track track, string? localAudioPath, CancellationToken ct = default)
    {
        var ctx = BuildContext(track, localAudioPath);
        await RunProvidersAsync(ctx, ct, isManualSearch: false);
    }

    /// <summary>
    /// 手动搜索：用户从 UI 输入关键词，跳过本地/内嵌/云盘，直接走在线源。
    /// 查询到歌词后按当前 Track 写回本地/云盘，实现“与音乐文件绑定”。
    /// </summary>
    public async Task SearchAsync(string query, Track track, string? localAudioPath, CancellationToken ct = default)
    {
        var ctx = BuildContext(track, localAudioPath);
        ctx.SearchQuery = query;
        await RunProvidersAsync(ctx, ct, isManualSearch: true);
    }

    private LyricsContext BuildContext(Track track, string? localAudioPath)
    {
        ICloudSourceProvider? cloud = null;
        string? cloudFileId = null;
        if (track.SourceType == TrackSourceType.Cloud && !string.IsNullOrEmpty(track.SourceId))
        {
            cloud = _cloud.Get(track.SourceId);
            cloudFileId = track.FilePath;
        }

        return new LyricsContext
        {
            Track = track,
            LocalAudioPath = localAudioPath,
            Cloud = cloud,
            CloudFileId = cloudFileId,
            LyricsPath = _settings.Settings.LyricsPath,
        };
    }

    private async Task RunProvidersAsync(LyricsContext ctx, CancellationToken ct, bool isManualSearch)
    {
        LyricsResult? result = null;
        foreach (var p in _providers)
        {
            // 手动搜索跳过本地/内嵌/云盘。
            if (isManualSearch && p is not (LrclibLyricsProvider or OnlineLyricsProvider)) continue;

            // 在线源受开关控制。
            if (p is (LrclibLyricsProvider or OnlineLyricsProvider) && !_settings.Settings.OnlineLyricsEnabled) continue;

            try { result = await p.GetAsync(ctx, ct).ConfigureAwait(false); }
            catch { result = null; }
            if (result is not null) break;
        }

        if (result is null)
        {
            LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = Array.Empty<LyricLine>(), Source = null });
            return;
        }

        LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = result.Lines, Source = result.Source, Raw = result.Raw });

        // 写回：让歌词跟着歌走（后台执行，不阻塞 UI）。
        _ = Task.Run(() => WriteBackAsync(ctx, result, CancellationToken.None));
    }

    private async Task WriteBackAsync(LyricsContext ctx, LyricsResult result, CancellationToken ct)
    {
        try
        {
            if (ctx.Track.SourceType == TrackSourceType.Local
                && !string.IsNullOrEmpty(ctx.LocalAudioPath)
                && result.Source != "Local")
            {
                // 本地曲：把云盘/网络/内嵌歌词落一份 .lrc 到音频同目录。
                var dir = Path.GetDirectoryName(ctx.LocalAudioPath);
                var name = Path.GetFileNameWithoutExtension(ctx.LocalAudioPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var lrcPath = Path.Combine(dir, name + ".lrc");
                    if (!File.Exists(lrcPath))
                        await File.WriteAllTextAsync(lrcPath, result.Raw, Encoding.UTF8, ct).ConfigureAwait(false);
                }
            }
            else if (ctx.Track.SourceType == TrackSourceType.Cloud
                     && ctx.Cloud is not null
                     && !string.IsNullOrEmpty(ctx.CloudFileId)
                     && result.Source != "Cloud"
                     && _settings.Settings.AutoUploadCloudLyrics)
            {
                // 云盘曲：把网络/内嵌歌词上传回云盘同目录 .lrc（仅当云盘尚无该文件时）。
                var lrcId = Path.ChangeExtension(ctx.CloudFileId, ".lrc");
                var existing = await ctx.Cloud.GetTextAsync(lrcId, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(existing))
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(result.Raw));
                    await ctx.Cloud.UploadAsync(lrcId, ms, ct).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // 写回失败静默：歌词已加载展示，网络/磁盘异常不应打扰用户。
        }
    }
}
