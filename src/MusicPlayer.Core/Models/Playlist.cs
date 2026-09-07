using System.Collections.Generic;

namespace MusicPlayer.Core.Models;

public class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "默认列表";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<PlaylistItem> Items { get; set; } = new();
}
