using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MusicPlayer.Core.Interfaces;

/// <summary>云盘文件/文件夹条目（第二期实现）。</summary>
public class CloudEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }

    /// <summary>是否为可播放的音乐文件（按扩展名判定）。仅音乐文件/文件夹可加入播放列表。</summary>
    public bool IsMusic { get; set; }

    private bool _isCached;
    /// <summary>是否已缓存到本地下载目录（UI 标记用，不持久化）。缓存成功后由 CloudViewModel 置位。</summary>
    public bool IsCached
    {
        get => _isCached;
        set
        {
            if (_isCached == value) return;
            _isCached = value;
            OnPropertyChanged(nameof(IsCached));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 云源提供方契约（实装：坚果云 WebDAV；通用 WebDAV 协议，可接任意 WebDAV 源）。
/// 第一期仅定义接口，UI 与播放内核不感知来源。
/// </summary>
public interface ICloudSourceProvider
{
    string Id { get; }

    /// <summary>最近一次 LoginAsync 失败的详细原因（含 HTTP 状态码），为空表示未失败。</summary>
    string? LastError { get; }

    Task<IEnumerable<CloudEntry>> BrowseAsync(string folderId, CancellationToken ct);
    Task<Stream> OpenStreamAsync(string fileId, long? fromByte, CancellationToken ct);
    Task<long> DownloadAsync(string fileId, string destPath, IProgress<double>? progress, CancellationToken ct);
    Task<bool> LoginAsync();

    /// <summary>上传文件内容到指定 fileId（PUT）。用于把播放列表快照同步到云端。</summary>
    Task UploadAsync(string fileId, Stream content, CancellationToken ct);

    /// <summary>读取指定 fileId 的文本（GET）。用于下载播放列表快照。文件不存在时返回 null。</summary>
    Task<string?> GetTextAsync(string fileId, CancellationToken ct);

    /// <summary>在指定 folderId 下创建集合（目录，MKCOL）。已存在则忽略。</summary>
    Task EnsureFolderAsync(string folderId, CancellationToken ct);
}
