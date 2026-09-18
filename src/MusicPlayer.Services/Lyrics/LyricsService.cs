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
///   - 云盘曲：从网络/内嵌拿到歌词时，自动上传 .lrc 到云盘 yunMusicPlayer/Lyric 目录（受 AutoUploadCloudLyrics 开关控制）。
/// 触发 LyricsChanged 事件供 UI 订阅；写回在后台线程执行，失败静默不影响播放。
/// </summary>
public class LyricsService
{
    private readonly ISettingsService _settings;
    private readonly CloudSourceRegistry _cloud;
    private readonly ILyricsProvider[] _providers;
    private readonly LrclibLyricsProvider _lrclib;

    public event EventHandler<LyricsLoadedEventArgs>? LyricsChanged;

    public event EventHandler<LyricsCandidatesRequestedEventArgs>? LyricsCandidatesRequested;

    public LyricsService(ISettingsService settings, CloudSourceRegistry cloud)
    {
        _settings = settings;
        _cloud = cloud;
        // 顺序即优先级：本地/内嵌 → 云盘 → 网络。
        _lrclib = new LrclibLyricsProvider();
        _providers = new ILyricsProvider[]
        {
            new EmbeddedTagProvider(),
            new LocalLrcProvider(),
            new CloudLrcProvider(),
            _lrclib,
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
    /// 手动搜索候选：按用户输入关键词查 LRCLIB，返回多组候选（含可直接使用的歌词）。
    /// 调用方在有多组结果时让用户选择，再调用 <see cref="LoadCandidateAsync"/> 加载选定项。
    /// </summary>
    public async Task<LyricsCandidate[]> SearchCandidatesAsync(string query, Track track, CancellationToken ct = default)
    {
        if (!_settings.Settings.OnlineLyricsEnabled) return Array.Empty<LyricsCandidate>();
        try { return await _lrclib.SearchCandidatesAsync(query, track, ct); }
        catch { return Array.Empty<LyricsCandidate>(); }
    }

    /// <summary>加载用户选定的歌词候选并写回绑定（与当前 Track 关联）。</summary>
    public async Task LoadCandidateAsync(LyricsCandidate candidate, Track track, string? localAudioPath, CancellationToken ct = default)
    {
        var raw = !string.IsNullOrWhiteSpace(candidate.SyncedLyrics)
            ? candidate.SyncedLyrics
            : candidate.PlainLyrics;
        if (string.IsNullOrWhiteSpace(raw)) return;

        var lines = LrcParser.Parse(raw);
        if (lines.Length == 0)
        {
            lines = raw.Split('\n')
                .Select(t => new LyricLine { Time = TimeSpan.Zero, Text = t.TrimEnd('\r') })
                .Where(l => l.Text.Length > 0)
                .ToArray();
        }

        var result = new LyricsResult { Lines = lines, Raw = raw, Source = "Lrclib" };
        var ctx = BuildContext(track, localAudioPath);
        Emit(result, ctx);
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
        if (isManualSearch)
        {
            // 手动搜索：仅走在线源（受开关控制），不进入候选多选流程。
            LyricsResult? result = null;
            foreach (var p in _providers)
            {
                if (p is not (LrclibLyricsProvider or OnlineLyricsProvider)) continue;
                if (!_settings.Settings.OnlineLyricsEnabled) continue;
                try { result = await p.GetAsync(ctx, ct).ConfigureAwait(false); }
                catch { result = null; }
                if (result is not null) break;
            }
            if (result is null)
                LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = Array.Empty<LyricLine>(), Source = null });
            else
                Emit(result, ctx);
            return;
        }

        // 自动加载：先走本地/内嵌/云盘；全部未命中再走网络候选（可能需用户选择）。
        foreach (var p in _providers)
        {
            if (p is (LrclibLyricsProvider or OnlineLyricsProvider)) continue; // 在线源交由候选流程处理
            try
            {
                var r = await p.GetAsync(ctx, ct).ConfigureAwait(false);
                if (r is not null) { Emit(r, ctx); return; }
            }
            catch { /* 单源失败忽略，继续下一个 */ }
        }
        await ResolveAutoCandidatesAsync(ctx, ct);
    }

    private async Task ResolveAutoCandidatesAsync(LyricsContext ctx, CancellationToken ct)
    {
        if (!_settings.Settings.OnlineLyricsEnabled)
        {
            LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = Array.Empty<LyricLine>(), Source = null });
            return;
        }

        LyricsCandidate[] cands;
        try { cands = await _lrclib.SearchCandidatesAsync("", ctx.Track, ct); }
        catch { cands = Array.Empty<LyricsCandidate>(); }

        // 自动加载：直接采用第一条候选并加载，不弹窗打断播放。
        // 只有"手动点击搜索"才通过 UI 弹窗让用户选择（见 PlaybackViewModel.ManualSearchAsync → LyricCandidateWindow）。
        if (cands.Length >= 1)
        {
            await LoadCandidateAsync(cands[0], ctx.Track, ctx.LocalAudioPath, ct);
            return;
        }

        // 0 个候选：回退到 OnlineLyricsProvider 单结果兜底（保持"尽力给歌词"）。
        foreach (var p in _providers)
        {
            if (p is not OnlineLyricsProvider) continue;
            try
            {
                var r = await p.GetAsync(ctx, ct).ConfigureAwait(false);
                if (r is not null) { Emit(r, ctx); return; }
            }
            catch { /* 忽略 */ }
        }
        LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = Array.Empty<LyricLine>(), Source = null });
    }

    /// <summary>触发歌词变更事件并后台写回（让歌词跟着歌走）。</summary>
    private void Emit(LyricsResult result, LyricsContext ctx)
    {
        LyricsChanged?.Invoke(this, new LyricsLoadedEventArgs { Lines = result.Lines, Source = result.Source, Raw = result.Raw });
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
                // 云盘曲：把网络/内嵌歌词上传回云盘 yunMusicPlayer/Lyric 目录（仅当云盘尚无该文件时）。
                var baseName = Path.GetFileNameWithoutExtension(ctx.CloudFileId);
                var lrcId = CloudPaths.LyricPath(baseName + ".lrc");
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
