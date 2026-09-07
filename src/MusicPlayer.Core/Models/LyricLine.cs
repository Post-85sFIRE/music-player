namespace MusicPlayer.Core.Models;

/// <summary>一行歌词：时间轴 + 文本。时间轴为 TimeSpan.Zero 表示纯文本（无时间信息）。</summary>
public class LyricLine
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>歌词加载完成事件参数：携带解析后的歌词行、来源标识与原始文本。</summary>
public class LyricsLoadedEventArgs : System.EventArgs
{
    /// <summary>解析后的歌词行（已按时间排序）。空数组表示无歌词。</summary>
    public LyricLine[] Lines { get; init; } = System.Array.Empty<LyricLine>();

    /// <summary>
    /// 来源标识：Local（音频同目录/歌词目录 .lrc）、Embedded（音频内嵌标签）、
    /// Cloud（云盘同目录 .lrc）、Online（网络 API）、null（未找到歌词）。
    /// </summary>
    public string? Source { get; init; }

    /// <summary>歌词原始文本（用于写回本地/云盘）。</summary>
    public string? Raw { get; init; }
}
