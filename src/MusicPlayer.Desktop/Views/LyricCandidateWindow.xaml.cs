using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicPlayer.Core.Lyrics;

namespace MusicPlayer.Desktop.Views;

/// <summary>歌词多结果选择对话框：列出候选歌词（歌名/歌手），确定后返回选中的候选。</summary>
public partial class LyricCandidateWindow : Window
{
    public LyricsCandidate[] Candidates { get; }

    public LyricsCandidate? Selected { get; private set; }

    public LyricCandidateWindow(LyricsCandidate[] candidates)
    {
        InitializeComponent();
        Candidates = candidates;
        DataContext = this;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is LyricsCandidate c) { Selected = c; DialogResult = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is LyricsCandidate c) { Selected = c; DialogResult = true; }
    }
}
