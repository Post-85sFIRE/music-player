using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services.Lyrics;

/// <summary>
/// 本地 .lrc 文件来源。匹配策略：
/// 1. 精确/近精确候选（与音频同名、仅标题、艺术家 - 标题）。
/// 2. 扫描音频目录与 LyricsPath，解析文件名中的 Artist/Title 做模糊匹配，
///    覆盖「BEYOND - 灰色轨迹 - hash.krc」这类酷狗缓存命名。
/// 注意：.krc 是酷狗加密二进制，无法直接解析，若按 LRC 解析为空则自然跳过。
/// </summary>
public class LocalLrcProvider : ILyricsProvider
{
    private static readonly string[] LyricExtensions = { ".lrc", ".krc", ".txt" };

    public string Name => "Local";

    public async Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.LocalAudioPath)) return null;

        var baseName = Path.GetFileNameWithoutExtension(ctx.LocalAudioPath);
        var dir = Path.GetDirectoryName(ctx.LocalAudioPath);

        // 阶段1：精确/近精确候选。
        var candidates = BuildDirectCandidates(dir, ctx.LyricsPath, baseName, ctx.Track);
        foreach (var path in candidates)
        {
            var r = await TryReadAsync(path, ct);
            if (r is not null) return r;
        }

        // 阶段2：按 Track 元数据扫描文件名做模糊匹配。
        var best = FindBestMetadataMatch(dir, ctx.Track);
        if (best is null && !string.IsNullOrWhiteSpace(ctx.LyricsPath))
            best = FindBestMetadataMatch(ctx.LyricsPath, ctx.Track);

        if (best is not null)
        {
            var r = await TryReadAsync(best, ct);
            if (r is not null) return r;
        }

        return null;
    }

    private static HashSet<string> BuildDirectCandidates(string? dir, string? lyricsPath, string baseName, Track track)
    {
        var candidates = new HashSet<string>();
        if (!string.IsNullOrEmpty(dir))
        {
            foreach (var ext in LyricExtensions)
            {
                candidates.Add(Path.Combine(dir, baseName + ext));
                if (!string.IsNullOrWhiteSpace(track.Title))
                {
                    var title = track.Title.Trim();
                    candidates.Add(Path.Combine(dir, title + ext));
                    if (!string.IsNullOrWhiteSpace(track.Artist))
                        candidates.Add(Path.Combine(dir, track.Artist.Trim() + " - " + title + ext));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(lyricsPath))
        {
            foreach (var ext in LyricExtensions)
            {
                candidates.Add(Path.Combine(lyricsPath, baseName + ext));
                if (!string.IsNullOrWhiteSpace(track.Title))
                    candidates.Add(Path.Combine(lyricsPath, track.Title.Trim() + ext));
            }
        }

        return candidates;
    }

    private static async Task<LyricsResult?> TryReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, ct);
        var lines = LrcParser.Parse(text);
        if (lines.Length == 0) return null;
        return new LyricsResult { Lines = lines, Raw = text, Source = "Local" };
    }

    /// <summary>扫描目录内所有歌词文件，按文件名解析出的 Artist/Title 与 Track 元数据打分，返回最佳匹配路径。</summary>
    private static string? FindBestMetadataMatch(string? dir, Track track)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        if (string.IsNullOrWhiteSpace(track.Title) && string.IsNullOrWhiteSpace(track.Artist)) return null;

        var files = Directory.EnumerateFiles(dir)
            .Where(f => LyricExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        string? bestPath = null;
        var bestScore = 0;
        foreach (var file in files)
        {
            if (!TryParseArtistTitle(Path.GetFileNameWithoutExtension(file), out var artist, out var title)) continue;
            var score = ScoreMatch(track, artist, title);
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = file;
            }
        }

        return bestScore > 0 ? bestPath : null;
    }

    /// <summary>
    /// 解析文件名中的 Artist/Title。
    /// 模式："Artist - Title - hash" / "Artist - Title" / "Title - hash" / "Title"。
    /// 三段式时取前两段；两段式且第二段像 hash（无中文、长度>12）时视为 Title-only。
    /// </summary>
    private static bool TryParseArtistTitle(string nameWithoutExt, out string? artist, out string? title)
    {
        artist = null;
        title = null;

        var parts = nameWithoutExt.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(p => p.Trim())
                                  .Where(p => p.Length > 0)
                                  .ToArray();

        if (parts.Length >= 2)
        {
            var candidateArtist = parts[0];
            var candidateTitle = parts[1];

            // 两段式时，若第二段明显是 hash 串（无中文、长度>12），降级为 Title-only。
            if (parts.Length == 2 && candidateTitle.Length > 12 && !ContainsCjk(candidateTitle))
            {
                title = parts[0];
                return true;
            }

            artist = candidateArtist;
            title = candidateTitle;
            return true;
        }

        if (parts.Length == 1)
        {
            title = parts[0];
            return true;
        }

        return false;
    }

    private static bool ContainsCjk(string s) => s.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    private static bool IsMatch(string? trackValue, string? candidateValue)
    {
        if (string.IsNullOrWhiteSpace(trackValue) || string.IsNullOrWhiteSpace(candidateValue)) return false;
        var t = trackValue.Trim();
        var c = candidateValue.Trim();
        return t.Contains(c, StringComparison.OrdinalIgnoreCase)
            || c.Contains(t, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreMatch(Track track, string? artist, string? title)
    {
        var score = 0;
        if (IsMatch(track.Title, title)) score += 2;
        if (!string.IsNullOrWhiteSpace(artist) && IsMatch(track.Artist, artist)) score += 1;
        return score;
    }
}
