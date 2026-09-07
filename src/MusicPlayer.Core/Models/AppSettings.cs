using System.Collections.Generic;
using MusicPlayer.Core.Enums;

namespace MusicPlayer.Core.Models;

public class AppSettings
{
    /// <summary>输出设备标识；空字符串表示默认设备。</summary>
    public string OutputDeviceId { get; set; } = "";

    /// <summary>是否尝试 WASAPI 独占输出（失败自动回退共享/默认）。</summary>
    /// <remarks>
    /// 默认 false：独占会抢占音频设备，在设备被其它程序占用 / 虚拟声卡不支持独占的环境下，
    /// BASS_WASAPI_Init 可能长时间挂起，直接把整个启动链路卡死（表现为"双击没反应/界面不出来"）。
    /// 安全优先默认走共享输出；用户可在设置里手动开启，且初始化已带 3 秒超时保护。
    /// </remarks>
    public bool ExclusiveWasapi { get; set; } = false;

    /// <summary>已配置的云盘来源（M2）；随设置 JSON 持久化。</summary>
    public List<CloudSourceConfig> CloudSources { get; set; } = new();

    public PlaybackMode DefaultMode { get; set; } = PlaybackMode.Sequential;

    /// <summary>应用内音量，线性 0..1。</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>下载/缓存目录（第一期即预留入口与空间统计）。</summary>
    public string DownloadPath { get; set; } = "";

    public bool FadeEnabled { get; set; } = true;
    public ReplayGainMode ReplayGainMode { get; set; } = ReplayGainMode.Track;
    public string EqPreset { get; set; } = "None";

    /// <summary>额外的本地歌词目录（.lrc）。优先级低于音频同目录的 .lrc；为空则仅用音频同目录。</summary>
    public string LyricsPath { get; set; } = "";

    /// <summary>是否允许在没有本地/云盘歌词时从网络 API 获取（默认开启：无本地/云盘歌词时自动联网找；仍遵循 本地→云盘→网络 的优先级）。</summary>
    public bool OnlineLyricsEnabled { get; set; } = true;

    /// <summary>云盘歌曲：若从网络/内嵌拿到歌词，是否自动上传回云盘同目录 .lrc（让歌词跟着歌走）。</summary>
    public bool AutoUploadCloudLyrics { get; set; } = true;
}
