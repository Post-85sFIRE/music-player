using MusicPlayer.Core.Enums;

namespace MusicPlayer.Core.Models;

/// <summary>
/// 统一音轨模型：本地与云源通用。云源通过 SourceType + SourceId 区分，UI 只认 Track。
/// </summary>
public class Track
{
    public long Id { get; set; }

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public int Year { get; set; }
    public string Genre { get; set; } = "";

    public double DurationSec { get; set; }
    public int BitRate { get; set; }
    public int SampleRate { get; set; }
    public int BitsPerSample { get; set; }
    public string Codec { get; set; } = "";

    /// <summary>本地为绝对路径；云源为 fileId（第二期）。</summary>
    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>封面缓存哈希（按 TrackId 命名），无封面时为 null。</summary>
    public string? CoverHash { get; set; }

    public TrackSourceType SourceType { get; set; } = TrackSourceType.Local;
    public string? SourceId { get; set; }

    /// <summary>ReplayGain 增益（dB）。0 表示该文件未写入相应标签。</summary>
    public double ReplayGainTrackGain { get; set; }
    public double ReplayGainAlbumGain { get; set; }
    public double ReplayGainTrackPeak { get; set; }
    public double ReplayGainAlbumPeak { get; set; }

    public DateTime DateAddedUtc { get; set; } = DateTime.UtcNow;
}
