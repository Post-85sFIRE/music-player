using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Desktop.ViewModels;

/// <summary>歌词行视图模型：在 LyricLine 之上叠加 IsActive（当前播放行高亮），供右侧歌词区绑定。</summary>
public class LyricLineViewModel : INotifyPropertyChanged
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = "";

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static LyricLineViewModel From(LyricLine line) => new()
    {
        Time = line.Time,
        Text = line.Text,
    };
}
