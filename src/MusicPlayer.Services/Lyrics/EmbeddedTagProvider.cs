using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Core.Models;
using TagLib;

namespace MusicPlayer.Services.Lyrics;

/// <summary>音频内嵌歌词来源（ID3 USLT / Vorbis LYRICS 等），通过 TagLib 读取。</summary>
public class EmbeddedTagProvider : ILyricsProvider
{
    public string Name => "Embedded";

    public Task<LyricsResult?> GetAsync(LyricsContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.LocalAudioPath)) return Task.FromResult<LyricsResult?>(null);
        try
        {
            // 注意：ImplicitUsings 已隐含 System.IO，故此处必须显式写全 TagLib.File 以消除与 System.IO.File 的歧义。
            using var f = TagLib.File.Create(ctx.LocalAudioPath);
            var lyric = f.Tag.Lyrics;
            if (string.IsNullOrWhiteSpace(lyric)) return Task.FromResult<LyricsResult?>(null);

            // 内嵌歌词可能是 LRC 文本，也可能是纯文本。
            var lines = LrcParser.Parse(lyric);
            if (lines.Length == 0)
            {
                lines = lyric.Split('\n')
                    .Select(t => new LyricLine { Time = TimeSpan.Zero, Text = t.TrimEnd('\r') })
                    .Where(l => l.Text.Length > 0)
                    .ToArray();
            }

            return Task.FromResult<LyricsResult?>(new LyricsResult { Lines = lines, Raw = lyric, Source = "Embedded" });
        }
        catch
        {
            return Task.FromResult<LyricsResult?>(null);
        }
    }
}
