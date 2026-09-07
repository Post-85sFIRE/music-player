using System.ComponentModel;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Desktop.ViewModels;

/// <summary>
/// 播放列表行包装器：把 PlaylistItem 与稳定的 1-based 显示序号绑定在一起。
/// 序号在 Refresh 时按行位置计算，连续无负值（替代原先 ListBox.AlternationIndex 在筛选/虚拟化下返回 -1 的坑）。
/// 新增 IsCached：仅用于云源曲目，标记是否已在本地缓存（不持久化）。
/// </summary>
public class PlaylistRow : INotifyPropertyChanged
{
    public PlaylistItem Item { get; }
    public int Index { get; }

    private bool _isCached;
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

    public PlaylistRow(PlaylistItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
