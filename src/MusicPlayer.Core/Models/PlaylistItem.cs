namespace MusicPlayer.Core.Models;

public class PlaylistItem
{
    public long Id { get; set; }
    public long PlaylistId { get; set; }
    public long TrackId { get; set; }
    public int Order { get; set; }

    public Track? Track { get; set; }
}
