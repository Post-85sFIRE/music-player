using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using TagLib;

namespace MusicPlayer.Services;

public class LibraryScanner : ILibraryScanner
{
    private readonly ITrackRepository _tracks;
    private readonly IPlaylistRepository _playlists;
    private readonly string _coverCacheDir;
    private readonly ILogger<LibraryScanner>? _logger;
    private readonly List<FileSystemWatcher> _watchers = new();

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ape", ".wav", ".ogg", ".oga", ".m4a", ".aac",
        ".opus", ".wma", ".dsf", ".dff", ".mpc", ".wv", ".alac"
    };

    public LibraryScanner(ITrackRepository tracks, IPlaylistRepository playlists, string coverCacheDir,
        ILogger<LibraryScanner>? logger = null)
    {
        _tracks = tracks;
        _playlists = playlists;
        _coverCacheDir = coverCacheDir;
        _logger = logger;
        Directory.CreateDirectory(_coverCacheDir);
    }

    public async Task ScanFolderAsync(LibraryFolder folder, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var dir = new DirectoryInfo(folder.Path);
        if (!dir.Exists)
        {
            progress?.Report(new ScanProgress { Error = $"目录不存在: {folder.Path}", Finished = true });
            return;
        }

        var files = folder.Recursive
            ? dir.EnumerateFiles("*", SearchOption.AllDirectories)
            : dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly);

        var scanned = 0;
        var added = 0;
        foreach (var fi in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!AudioExtensions.Contains(fi.Extension)) continue;

            scanned++;
            try
            {
                var track = BuildTrack(fi);
                await _tracks.UpsertAsync(track);
                added++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "跳过无法解析的文件: {File}", fi.FullName);
            }

            if (scanned % 50 == 0)
                progress?.Report(new ScanProgress { FilesScanned = scanned, TracksAdded = added, CurrentFile = fi.FullName });
        }

        folder.LastScanUtc = DateTime.UtcNow;
        await _playlists.AddFolderAsync(folder);

        progress?.Report(new ScanProgress { FilesScanned = scanned, TracksAdded = added, Finished = true });
    }

    public async Task ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var folders = await _playlists.GetFoldersAsync();
        foreach (var f in folders)
        {
            ct.ThrowIfCancellationRequested();
            await ScanFolderAsync(f, progress, ct);
        }
    }

    public void StartWatching()
    {
        StopWatching();
        var folders = _playlists.GetFoldersAsync().GetAwaiter().GetResult();
        foreach (var f in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(f.Path)
                {
                    IncludeSubdirectories = f.Recursive,
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                watcher.Created += (_, e) => OnFileChanged(e.FullPath);
                watcher.Changed += (_, e) => OnFileChanged(e.FullPath);
                watcher.Deleted += (_, e) => _tracks.DeleteByPathAsync(e.FullPath).GetAwaiter().GetResult();
                watcher.Renamed += (_, e) =>
                {
                    if (e.OldFullPath != null) _tracks.DeleteByPathAsync(e.OldFullPath).GetAwaiter().GetResult();
                    OnFileChanged(e.FullPath);
                };
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "无法监听目录: {Path}", f.Path);
            }
        }
    }

    public void StopWatching()
    {
        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
        _watchers.Clear();
    }

    private void OnFileChanged(string path)
    {
        if (!AudioExtensions.Contains(Path.GetExtension(path))) return;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists) _tracks.UpsertAsync(BuildTrack(fi)).GetAwaiter().GetResult();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "增量扫描失败: {File}", path); }
    }

    private Track BuildTrack(FileInfo fi)
    {
        using var file = TagLib.File.Create(fi.FullName);
        var tag = file.Tag;

        var track = new Track
        {
            Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(fi.Name) : tag.Title!,
            Artist = string.Join(", ", tag.Performers),
            Album = tag.Album ?? "",
            AlbumArtist = string.Join(", ", tag.AlbumArtists),
            Year = (int)tag.Year,
            Genre = string.Join(", ", tag.Genres),
            DurationSec = file.Properties.Duration.TotalSeconds,
            BitRate = file.Properties.AudioBitrate,
            SampleRate = file.Properties.AudioSampleRate,
            Codec = fi.Extension.TrimStart('.').ToUpperInvariant(),
            FilePath = fi.FullName,
            FileSize = fi.Length,
            LastModifiedUtc = fi.LastWriteTimeUtc
        };

        if (tag.Pictures.Length > 0)
        {
            var pic = tag.Pictures[0];
            var data = pic.Data.ToArray();
            var hash = HashBytes(data);
            track.CoverHash = hash;
            SaveCover(hash, data);
        }

        // ReplayGain：读取标签里的响度增益（dB），供播放时统一响度。
        var rg = ReadReplayGain(file);
        track.ReplayGainTrackGain = rg.TrackGain;
        track.ReplayGainAlbumGain = rg.AlbumGain;
        track.ReplayGainTrackPeak = rg.TrackPeak;
        track.ReplayGainAlbumPeak = rg.AlbumPeak;

        return track;
    }

    /// <summary>从 APEv2 / Vorbis 注释中读取 ReplayGain 字段（覆盖 FLAC/APE/OGG/OPUS/多数 MP3）。</summary>
    private static (double TrackGain, double AlbumGain, double TrackPeak, double AlbumPeak) ReadReplayGain(TagLib.File file)
    {
        string? Field(TagLib.TagTypes type, string key)
        {
            var tag = file.GetTag(type, false);
            if (tag is null) return null;
            // APEv2 标签用 GetValue；Vorbis 注释（FLAC/OGG/OPUS）用 GetField。用反射规避不同 TagLib 版本的 API 差异。
            var m = tag.GetType().GetMethod("GetValue") ?? tag.GetType().GetMethod("GetField");
            if (m is null) return null;
            if (m.Invoke(tag, new object[] { key }) is System.Collections.IEnumerable vals)
            {
                foreach (var v in vals) return (v as string) ?? v?.ToString();
            }
            return null;
        }

        double Db(string? s)
        {
            var v = (s ?? "").Replace("db", "", StringComparison.OrdinalIgnoreCase).Trim();
            return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        }
        double Peak(string? s) =>
            double.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

        return (
            Db(Field(TagLib.TagTypes.Ape, "REPLAYGAIN_TRACK_GAIN") ?? Field(TagLib.TagTypes.Xiph, "REPLAYGAIN_TRACK_GAIN")),
            Db(Field(TagLib.TagTypes.Ape, "REPLAYGAIN_ALBUM_GAIN") ?? Field(TagLib.TagTypes.Xiph, "REPLAYGAIN_ALBUM_GAIN")),
            Peak(Field(TagLib.TagTypes.Ape, "REPLAYGAIN_TRACK_PEAK") ?? Field(TagLib.TagTypes.Xiph, "REPLAYGAIN_TRACK_PEAK")),
            Peak(Field(TagLib.TagTypes.Ape, "REPLAYGAIN_ALBUM_PEAK") ?? Field(TagLib.TagTypes.Xiph, "REPLAYGAIN_ALBUM_PEAK"))
        );
    }

    private void SaveCover(string hash, byte[] data)
    {
        var target = Path.Combine(_coverCacheDir, hash + ".jpg");
        if (!System.IO.File.Exists(target))
        {
            try { System.IO.File.WriteAllBytes(target, data); }
            catch (Exception ex) { _logger?.LogWarning(ex, "封面写入失败: {Hash}", hash); }
        }
    }

    private static string HashBytes(byte[] data)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(data);
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString(0, 32);
    }
}
