using System;
using System.Collections.Generic;

namespace MusicPlayer.Services;

/// <summary>
/// 不重复随机：维护"待播池 + 已播队列"，播完一轮再洗牌。
/// </summary>
public class ShuffleStrategy
{
    private readonly List<long> _remaining = new();
    private readonly List<long> _played = new();
    private readonly Random _rng = new();

    public void Reset(IEnumerable<long> allIds)
    {
        _remaining.Clear();
        _played.Clear();
        _remaining.AddRange(allIds);
        Shuffle(_remaining);
    }

    public long? Next()
    {
        if (_remaining.Count == 0)
        {
            if (_played.Count == 0) return null;
            _remaining.AddRange(_played);
            _played.Clear();
            Shuffle(_remaining);
        }

        var id = _remaining[0];
        _remaining.RemoveAt(0);
        _played.Add(id);
        return id;
    }

    public void MarkPlayed(long id)
    {
        _remaining.Remove(id);
        if (!_played.Contains(id)) _played.Add(id);
    }

    public IReadOnlyList<long> Played => _played;

    private void Shuffle(List<long> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]);
        }
    }
}
