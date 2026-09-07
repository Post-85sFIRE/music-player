using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MusicPlayer.Core.Interfaces;

/// <summary>
/// 下载/缓存服务契约（第二期实装：边播边缓存、断点续传）。
/// 第一期提供本地文件直通实现（直接返回 File.OpenRead）。
/// </summary>
public interface IDownloadCacheService
{
    /// <summary>确保云文件已缓存到本地，返回可播放的本地路径（供 BASS 加载）。已缓存则直接命中。</summary>
    Task<string> CacheToLocalAsync(string sourceId, Func<Task<Stream>> remoteOpener, IProgress<double>? progress, CancellationToken ct);

    /// <summary>打开音源；云源时同时落盘到缓存目录，进度可显示"已缓存比例"。</summary>
    Task<Stream> OpenOrCacheAsync(string sourceId, Func<Task<Stream>> remoteOpener, CancellationToken ct);

    Task<long> CacheUsageBytesAsync();

    /// <summary>判断某 sourceId 是否已在本地下载目录存在有效缓存文件（用于云盘列表"已缓存"标记）。</summary>
    bool IsCached(string sourceId);

    /// <summary>某 sourceId 完成落盘缓存后触发，参数为 sourceId，供 UI 实时刷新"已缓存"标记。</summary>
    event Action<string>? Cached;
}
