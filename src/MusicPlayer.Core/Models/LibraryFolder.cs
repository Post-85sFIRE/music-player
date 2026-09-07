namespace MusicPlayer.Core.Models;

public class LibraryFolder
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public bool Recursive { get; set; } = true;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastScanUtc { get; set; }
}
