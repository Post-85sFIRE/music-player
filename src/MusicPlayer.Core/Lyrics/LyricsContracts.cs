using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Lyrics;

/// <summary>歌词加载上下文：一次歌词查询所需的全部输入。</summary>
public class LyricsContext
{
    /// <summary>当前曲目（含标题/艺术家/来源类型）。</summary>
    public Track Track { get; init; } = null!;

    /// <summary>本地可读取的音频文件路径（本地曲为原始路径；云曲为缓存落地后的路径）。云曲且无缓存时为 null。</summary>
    public string? LocalAudioPath { get; init; }

    /// <summary>云盘提供方（仅云曲非 null）。</summary>
    public ICloudSourceProvider? Cloud { get; init; }

    /// <summary>云盘相对路径（云曲即 Track.FilePath；用于推导同目录 .lrc 的 fileId）。</summary>
    public string? CloudFileId { get; init; }

    /// <summary>用户设置的额外本地歌词目录（音频同目录之外第二优先的本地 .lrc 位置）。</summary>
    public string? LyricsPath { get; init; }

    /// <summary>
    /// 手动搜索关键词（用户从右侧歌词区搜索框输入）。若不为空，LyricsService 会跳过本地/内嵌/云盘，
    /// 直接用在线源按此关键词查询；查询结果仍按当前 Track 写回本地/云盘。
    /// </summary>
    public string? SearchQuery { get; set; }
}

/// <summary>单个歌词来源的结果。</summary>
public class LyricsResult
{
    public LyricLine[] Lines { get; init; } = System.Array.Empty<LyricLine>();
    public string Raw { get; init; } = "";
    /// <summary>来源标识：Local / Embedded / Cloud / Online。</summary>
    public string Source { get; init; } = "";
}

/// <summary>歌词来源提供方契约（本地文件 / 内嵌标签 / 云盘 / 网络）。</summary>
public interface ILyricsProvider
{
    /// <summary>来源名称（用于优先级排序与诊断）。</summary>
    string Name { get; }

    /// <summary>尝试获取歌词；返回 null 表示此来源没有可用歌词。</summary>
    System.Threading.Tasks.Task<LyricsResult?> GetAsync(LyricsContext ctx, System.Threading.CancellationToken ct);
}
