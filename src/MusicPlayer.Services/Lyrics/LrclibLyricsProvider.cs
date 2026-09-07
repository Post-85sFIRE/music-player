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
/// 自动按 Track 元数据查询；若 LyricsContext.SearchQuery 存在，则按用户输入查询。
/// </summary>
public class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _http = new();

    public string Name => "Lrclib";

    public async Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        var (artist, title) = ExtractQuery(ctx);
        if (string.IsNullOrWhiteSpace(title)) return null;

        try
        {
            var query = $"track_name={Uri.EscapeDataString(title.Trim())}";
            if (!string.IsNullOrWhiteSpace(artist))
                query += $"&artist_name={Uri.EscapeDataString(artist.Trim())}";
            if (!string.IsNullOrWhiteSpace(ctx.Track.Album))
                query += $"&album_name={Uri.EscapeDataString(ctx.Track.Album.Trim())}";

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

    private static (string? Artist, string? Title) ExtractQuery(LyricsContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.SearchQuery))
        {
            var q = ctx.SearchQuery.Trim();
            var idx = q.IndexOf(" - ", System.StringComparison.Ordinal);
            if (idx > 0)
                return (q.Substring(0, idx).Trim(), q.Substring(idx + 3).Trim());
            return (null, q);
        }

        return (ctx.Track.Artist.Trim(), ctx.Track.Title.Trim());
    }
}
