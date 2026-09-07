using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services.Lyrics;

/// <summary>云盘同目录 .lrc 来源：复用 WebDAV 的 GetTextAsync（文件不存在返回 null）。</summary>
public class CloudLrcProvider : ILyricsProvider
{
    public string Name => "Cloud";

    public async Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        if (ctx.Cloud is null || string.IsNullOrEmpty(ctx.CloudFileId)) return null;

        // 把音频相对路径的扩展名换成 .lrc，即同目录歌词文件。
        var lrcId = Path.ChangeExtension(ctx.CloudFileId, ".lrc");
        var text = await ctx.Cloud.GetTextAsync(lrcId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = LrcParser.Parse(text);
        if (lines.Length == 0) return null;

        return new LyricsResult { Lines = lines, Raw = text, Source = "Cloud" };
    }
}
