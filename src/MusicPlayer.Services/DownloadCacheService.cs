using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Services;

/// <summary>
/// 下载/缓存服务：把云文件落地到本地下载（缓存）目录——即设置里的"下载音乐存储路径"，
/// 作为可播放的源文件（满足"在线播放=缓存为源文件"）。已缓存则直接命中；否则流式下载（带进度）。
/// CacheUsageBytesAsync 统计该目录占用，供"读取该路径下文件占的总空间"展示。
/// </summary>
public class DownloadCacheService : IDownloadCacheService
{
    private readonly string _root;

    public DownloadCacheService(string cacheRoot)
    {
        _root = cacheRoot;
        Directory.CreateDirectory(_root);
    }

    /// <summary>确保云文件已缓存到本地，返回本地路径供 BASS 加载。</summary>
    public async Task<string> CacheToLocalAsync(string sourceId, Func<Task<Stream>> remoteOpener, IProgress<double>? progress, CancellationToken ct)
    {
        var path = Path.Combine(_root, Sanitize(sourceId));

        // 命中：直接复用稳定缓存文件，并顺手清理旧版遗留的重复文件。
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            CleanupLegacyDuplicates(path, sourceId);
            return path;
        }

        await using var remote = await remoteOpener();
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long total = remote.CanSeek ? remote.Length : 0;
        long read = 0;
        int n;
        while ((n = await remote.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }

        // 缓存完成后清理旧版重复文件（旧版用 string.GetHashCode，每进程重启都变，会留下多个副本）。
        CleanupLegacyDuplicates(path, sourceId);
        // 落盘成功 → 通知 UI 刷新"已缓存"标记（参数即 sourceId，便于定位列表条目）。
        RaiseCached(sourceId);
        return path;
    }

    /// <summary>判断某 sourceId 是否已在缓存目录存在有效文件（长度 > 0）。</summary>
    public bool IsCached(string sourceId)
    {
        try
        {
            var path = Path.Combine(_root, Sanitize(sourceId));
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch { return false; }
    }

    /// <summary>某 sourceId 完成缓存后触发，供 UI 实时刷新"已缓存"标记。</summary>
    public event Action<string>? Cached;

    private void RaiseCached(string sourceId)
    {
        try { Cached?.Invoke(sourceId); }
        catch { /* 订阅方异常绝不应影响缓存/播放链路 */ }
    }

    public async Task<Stream> OpenOrCacheAsync(string sourceId, Func<Task<Stream>> remoteOpener, CancellationToken ct)
    {
        var path = await CacheToLocalAsync(sourceId, remoteOpener, null, ct);
        return File.OpenRead(path);
    }

    public Task<long> CacheUsageBytesAsync()
    {
        long bytes = 0;
        if (Directory.Exists(_root))
        {
            foreach (var fi in new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { bytes += fi.Length; }
                catch { /* 跳过无权限文件 */ }
            }
        }
        return Task.FromResult(bytes);
    }

    /// <summary>
    /// 把 sourceId（如 "providerId|/dav/path/光良 - 童话.flac"）映射为稳定的本地缓存文件名。
    /// 旧实现用 <see cref="string.GetHashCode"/>，但 .NET Core/5+ 的字符串哈希**按进程随机化**，
    /// 导致同一首歌每次重启 App 都生成不同 .cache，出现大量重复文件。新版改用 SHA256 稳定哈希。
    /// </summary>
    private static string Sanitize(string sourceId)
    {
        var clean = GetCleanName(sourceId);
        var hash = StableHash(sourceId);
        return $"{clean}.{hash}.cache";
    }

    private static string GetCleanName(string sourceId)
    {
        var name = Path.GetFileName(sourceId.TrimEnd('/'));
        return string.IsNullOrEmpty(name)
            ? "file"
            : new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>对 sourceId 做跨进程稳定的 32 位哈希，输出 8 位小写十六进制。</summary>
    private static string StableHash(string sourceId)
    {
        var bytes = Encoding.UTF8.GetBytes(sourceId);
        var hash = SHA256.HashData(bytes);
        // 取前 4 字节 → 8 位十六进制，长度与旧 GetHashCode 一致但跨进程稳定
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// <summary>
    /// 清理旧版遗留的同名重复缓存。旧版用 string.GetHashCode() 导致同一 sourceId 生成多个文件名；
    /// 新版以"同名 + 同大小"作为重复判定（几乎一定是同一首歌的旧副本），避免误删不同目录下同名歌曲。
    /// </summary>
    private void CleanupLegacyDuplicates(string keepPath, string sourceId)
    {
        var clean = GetCleanName(sourceId);
        long keepSize;
        try { keepSize = new FileInfo(keepPath).Length; }
        catch { return; }

        try
        {
            foreach (var fi in new DirectoryInfo(_root).EnumerateFiles($"{clean}.*.cache"))
            {
                if (string.Equals(fi.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (fi.Length == keepSize)
                {
                    try { fi.Delete(); }
                    catch { /* 可能正被占用，跳过 */ }
                }
            }
        }
        catch { }
    }
}
