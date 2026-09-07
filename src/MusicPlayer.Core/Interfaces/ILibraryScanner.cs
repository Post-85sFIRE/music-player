using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public class ScanProgress
{
    public int FilesScanned { get; set; }
    public int TracksAdded { get; set; }
    public string CurrentFile { get; set; } = "";
    public bool Finished { get; set; }
    public string? Error { get; set; }
}

public interface ILibraryScanner
{
    Task ScanFolderAsync(LibraryFolder folder, IProgress<ScanProgress>? progress, CancellationToken ct);
    Task ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken ct);
    void StartWatching();
    void StopWatching();
}
