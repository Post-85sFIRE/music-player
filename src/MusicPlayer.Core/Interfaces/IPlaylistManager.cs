using System;
using System.Collections.Generic;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public interface IPlaylistManager
{
    Playlist Current { get; }

    event Action<long>? PlayRequested;
    event EventHandler? ItemsChanged;

    void AddTracks(IEnumerable<long> trackIds);
    void RemoveItem(long itemId);
    void Clear();
    void Reorder(long itemId, int newOrder);
    void MoveToPlay(long itemId);

    IEnumerable<PlaylistItem> Search(string query);

    PlaylistItem? Next(PlaybackMode mode, long currentItemId);
    PlaylistItem? Previous(PlaybackMode mode, long currentItemId);
    PlaylistItem? GetItem(long itemId);
}
