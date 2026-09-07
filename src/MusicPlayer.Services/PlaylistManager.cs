using System;
using System.Collections.Generic;
using System.Linq;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services;

public class PlaylistManager : IPlaylistManager
{
    private readonly IPlaylistRepository _repo;
    private readonly ITrackRepository _tracks;
    private readonly ShuffleStrategy _shuffle = new();
    private bool _shuffleSeeded;

    public Playlist Current { get; private set; } = new();

    public event Action<long>? PlayRequested;
    public event EventHandler? ItemsChanged;

    public PlaylistManager(IPlaylistRepository repo, ITrackRepository tracks)
    {
        _repo = repo;
        _tracks = tracks;
        Current = _repo.GetOrCreateDefaultAsync().GetAwaiter().GetResult();
        Hydrate(Current.Items);
        SeedShuffle();
    }

    public void AddTracks(IEnumerable<long> trackIds)
    {
        var existing = new HashSet<long>(Current.Items.Select(i => i.TrackId));
        var added = new List<PlaylistItem>();
        foreach (var id in trackIds)
        {
            if (existing.Contains(id)) continue;
            var item = new PlaylistItem { TrackId = id, Order = Current.Items.Count };
            Current.Items.Add(item);
            added.Add(item);
            existing.Add(id);
        }

        if (added.Count > 0)
        {
            Hydrate(added);
            _repo.SaveAsync(Current).GetAwaiter().GetResult();
            SeedShuffle();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveItem(long itemId)
    {
        var item = Current.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        Current.Items.Remove(item);
        _repo.RemoveItemAsync(itemId).GetAwaiter().GetResult();
        SeedShuffle();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Current.Items.Clear();
        _repo.SaveAsync(Current).GetAwaiter().GetResult();
        SeedShuffle();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reorder(long itemId, int newOrder)
    {
        var item = Current.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;
        Current.Items.Remove(item);
        newOrder = Math.Max(0, Math.Min(newOrder, Current.Items.Count));
        Current.Items.Insert(newOrder, item);
        _repo.SaveAsync(Current).GetAwaiter().GetResult();
        SeedShuffle();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveToPlay(long itemId) => PlayRequested?.Invoke(itemId);

    public IEnumerable<PlaylistItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Current.Items;
        var q = query.Trim().ToLowerInvariant();
        return Current.Items.Where(i =>
            (i.Track?.Title ?? "").ToLowerInvariant().Contains(q) ||
            (i.Track?.Artist ?? "").ToLowerInvariant().Contains(q) ||
            (i.Track?.Album ?? "").ToLowerInvariant().Contains(q));
    }

    public PlaylistItem? Next(PlaybackMode mode, long currentItemId)
    {
        if (Current.Items.Count == 0) return null;

        if (mode == PlaybackMode.Shuffle)
        {
            EnsureShuffleSeeded(currentItemId);
            var id = _shuffle.Next();
            return id is null ? null : Current.Items.FirstOrDefault(i => i.TrackId == id);
        }

        var idx = IndexOf(currentItemId);
        if (idx < 0) return Current.Items[0];

        return mode switch
        {
            PlaybackMode.RepeatOne => Current.Items[idx],
            PlaybackMode.RepeatAll => Current.Items[(idx + 1) % Current.Items.Count],
            _ => idx + 1 < Current.Items.Count ? Current.Items[idx + 1] : null
        };
    }

    public PlaylistItem? Previous(PlaybackMode mode, long currentItemId)
    {
        if (Current.Items.Count == 0) return null;

        if (mode == PlaybackMode.Shuffle)
        {
            var history = _shuffle.Played;
            if (history.Count == 0) return null;
            var prev = history[^1];
            return Current.Items.FirstOrDefault(i => i.TrackId == prev);
        }

        var idx = IndexOf(currentItemId);
        if (idx < 0) return Current.Items[0];

        return mode switch
        {
            PlaybackMode.RepeatOne => Current.Items[idx],
            PlaybackMode.RepeatAll => Current.Items[(idx - 1 + Current.Items.Count) % Current.Items.Count],
            _ => idx - 1 >= 0 ? Current.Items[idx - 1] : null
        };
    }

    public PlaylistItem? GetItem(long itemId) => Current.Items.FirstOrDefault(i => i.Id == itemId);

    private int IndexOf(long itemId)
    {
        for (var i = 0; i < Current.Items.Count; i++)
            if (Current.Items[i].Id == itemId) return i;
        return -1;
    }

    private void Hydrate(IEnumerable<PlaylistItem> items)
    {
        foreach (var item in items)
        {
            if (item.Track is not null) continue;
            item.Track = _tracks.GetByIdAsync(item.TrackId).GetAwaiter().GetResult();
        }
    }

    private void SeedShuffle()
    {
        _shuffle.Reset(Current.Items.Select(i => i.TrackId));
        _shuffleSeeded = true;
    }

    private void EnsureShuffleSeeded(long currentItemId)
    {
        if (!_shuffleSeeded)
        {
            SeedShuffle();
            return;
        }

        var item = GetItem(currentItemId);
        if (item is not null) _shuffle.MarkPlayed(item.TrackId);
    }
}
