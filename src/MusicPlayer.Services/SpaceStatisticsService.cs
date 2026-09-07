using System;
using System.IO;
using System.Threading.Tasks;

namespace MusicPlayer.Services;

/// <summary>
/// 后台统计目录占用（第一期即可用，服务第二期缓存占用展示）。
/// 结果缓存 5 分钟，避免大目录频繁遍历卡 UI。
/// </summary>
public class SpaceStatisticsService
{
    private (long bytes, int files)? _cache;
    private DateTime _cacheTime;
    private string? _cachedPath;

    public async Task<(long bytes, int files)> ComputeAsync(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && path == _cachedPath && _cache.HasValue &&
            DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(5))
        {
            return _cache.Value;
        }

        long bytes = 0;
        int files = 0;

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            await Task.Run(() =>
            {
                var di = new DirectoryInfo(path);
                try
                {
                    foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { bytes += fi.Length; files++; }
                        catch { /* 跳过无权限文件 */ }
                    }
                }
                catch { /* 目录不可访问 */ }
            });
        }

        _cache = (bytes, files);
        _cacheTime = DateTime.UtcNow;
        _cachedPath = path;
        return _cache.Value;
    }
}
