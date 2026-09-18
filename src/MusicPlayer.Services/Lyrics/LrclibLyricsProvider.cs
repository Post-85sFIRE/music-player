using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services.Lyrics;

/// <summary>
/// LRCLIB (https://lrclib.net) 歌词来源：公开、免钥、带时间轴，中文/英文覆盖均较好。
/// 自动加载：先按 歌名+歌手 查，无果再按 歌名 兜底查（满足"用歌名+歌手搜，没有则再用歌名搜"）。
/// 手动搜索：走 /api/search 返回多组候选，由上层决定是否让用户选择。
/// </summary>
public class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _http = new();

    public string Name => "Lrclib";

    public async Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        var (artist, title) = ExtractQuery(ctx);
        if (string.IsNullOrWhiteSpace(title)) return null;

        // 1) 歌名 + 歌手
        var r1 = await GetOneAsync(title, artist, ctx.Track.Album, ct);
        if (r1 is not null) return r1;

        // 2) 兜底：仅歌名（用户明确要求"没有则再用歌名搜"）
        if (!string.IsNullOrWhiteSpace(artist))
            return await GetOneAsync(title, null, null, ct);

        return null;
    }

    private async Task<LyricsResult?> GetOneAsync(string title, string? artist, string? album, CancellationToken ct)
    {
        try
        {
            var query = $"track_name={Uri.EscapeDataString(title.Trim())}";
            if (!string.IsNullOrWhiteSpace(artist))
                query += $"&artist_name={Uri.EscapeDataString(artist.Trim())}";
            if (!string.IsNullOrWhiteSpace(album))
                query += $"&album_name={Uri.EscapeDataString(album.Trim())}";

            var url = $"https://lrclib.net/api/get?{query}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            string? raw = null;
            if (root.TryGetProperty("syncedLyrics", out var syncEl) && syncEl.ValueKind == JsonValueKind.String)
                raw = syncEl.GetString();
            if (string.IsNullOrWhiteSpace(raw)
                && root.TryGetProperty("plainLyrics", out var plainEl) && plainEl.ValueKind == JsonValueKind.String)
                raw = plainEl.GetString();

            if (string.IsNullOrWhiteSpace(raw)) return null;

            var lines = LrcParser.Parse(raw);
            if (lines.Length == 0)
            {
                lines = raw.Split('\n')
                    .Select(t => new LyricLine { Time = TimeSpan.Zero, Text = t.TrimEnd('\r') })
                    .Where(l => l.Text.Length > 0)
                    .ToArray();
            }

            return new LyricsResult { Lines = lines, Raw = raw, Source = "Lrclib" };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>候选搜索：按 歌名+歌手 查 /api/search，无果再按 歌名 兜底；结果含可直接使用的同步/纯文本歌词。</summary>
    public async Task<LyricsCandidate[]> SearchCandidatesAsync(string query, Track track, CancellationToken ct)
    {
        var (artist, title) = ParseQuery(query, track);
        if (string.IsNullOrWhiteSpace(title)) return System.Array.Empty<LyricsCandidate>();

        var list = await SearchOnceAsync(title, artist, ct);
        if (list.Length == 0 && !string.IsNullOrWhiteSpace(artist))
            list = await SearchOnceAsync(title, null, ct);
        return list;
    }

    private async Task<LyricsCandidate[]> SearchOnceAsync(string title, string? artist, CancellationToken ct)
    {
        try
        {
            var query = $"track_name={Uri.EscapeDataString(title.Trim())}";
            if (!string.IsNullOrWhiteSpace(artist))
                query += $"&artist_name={Uri.EscapeDataString(artist.Trim())}";
            query += "&limit=10";

            var url = $"https://lrclib.net/api/search?{query}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return System.Array.Empty<LyricsCandidate>();

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return System.Array.Empty<LyricsCandidate>();

            var list = new System.Collections.Generic.List<LyricsCandidate>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                list.Add(new LyricsCandidate
                {
                    Id = el.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : "",
                    TrackName = el.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "",
                    ArtistName = el.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "",
                    AlbumName = el.TryGetProperty("albumName", out var al) ? al.GetString() ?? "" : "",
                    SyncedLyrics = el.TryGetProperty("syncedLyrics", out var sl) ? sl.GetString() : null,
                    PlainLyrics = el.TryGetProperty("plainLyrics", out var pl) ? pl.GetString() : null,
                });
            }
            return list.ToArray();
        }
        catch
        {
            return System.Array.Empty<LyricsCandidate>();
        }
    }

    /// <summary>从歌词上下文抽取 歌手/歌名（手动搜索用 SearchQuery，否则用 Track 元数据）。</summary>
    private static (string? Artist, string? Title) ExtractQuery(LyricsContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.SearchQuery))
            return ParseQuery(ctx.SearchQuery, ctx.Track);
        return (ctx.Track.Artist.Trim(), ctx.Track.Title.Trim());
    }

    /// <summary>解析用户输入（"歌手 - 歌名" 或纯歌名）；query 为空时回退到 Track 元数据。</summary>
    private static (string? Artist, string? Title) ParseQuery(string? query, Track track)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (track.Artist.Trim(), track.Title.Trim());
        var q = query.Trim();
        var idx = q.IndexOf(" - ", System.StringComparison.Ordinal);
        if (idx > 0)
            return (q.Substring(0, idx).Trim(), q.Substring(idx + 3).Trim());
        return (null, q);
    }
}
