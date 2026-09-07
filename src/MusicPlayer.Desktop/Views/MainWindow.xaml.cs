using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Core.Models;
using MusicPlayer.Desktop.ViewModels;

namespace MusicPlayer.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));
    }

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Playback.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
    }

    private void PlaylistList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistRow row && DataContext is MainViewModel vm)
            vm.Playlist.PlayCommand.Execute(row);
    }

    private void PlaylistRemove_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Playlist.RemoveSelectedCommand.Execute(PlaylistList.SelectedItems);
    }

    private void PlaylistPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistRow row && DataContext is MainViewModel vm)
            vm.Playlist.PlayCommand.Execute(row);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            new SettingsWindow(vm.Settings) { Owner = this }.ShowDialog();
    }

    private void OpenCloud_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel)
            new CloudWindow(MusicPlayer.Desktop.App.Services.GetRequiredService<CloudViewModel>()) { Owner = this }.Show();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 窗口激活时按空格切换播放/暂停（隧道事件，先于所有子控件触发，保证窗口级生效）。
    /// 在文本框/密码框里按空格要正常输入，按钮/下拉框的空格有自身默认行为，均不拦截；
    /// 其余情况（列表、滑块、空白区等）拦截空格，避免触发滚动并直接切换播放状态。
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or System.Windows.Controls.Button or System.Windows.Controls.ComboBox)
            return;
        if (DataContext is MainViewModel vm && vm.Playback.PlayPauseCommand.CanExecute(null))
        {
            vm.Playback.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void DonateImage_Click(object sender, MouseButtonEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void LyricsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 点击歌词任意行 → 跳转到该行时间；选中后立即清除选择高亮，避免与"当前播放行"高亮冲突。
        // 注意：本项目启用了 UseWindowsForms，ListBox 需写全限定名以消除与 System.Windows.Forms.ListBox 的歧义。
        if (sender is not System.Windows.Controls.ListBox lb) return;
        if (lb.SelectedItem is LyricLineViewModel vm && DataContext is MainViewModel mvm)
        {
            mvm.Playback.Seek(vm.Time);
            lb.SelectedItem = null;
        }
    }

    #region 播放列表拖拽排序（WPF 原生，不依赖外部库）
    private PlaylistRow? _dragSource;

    private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox lb && e.OriginalSource is DependencyObject fe)
            _dragSource = FindListBoxItem(lb, fe)?.DataContext as PlaylistRow;
    }

    private void PlaylistList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource is null || sender is not System.Windows.Controls.ListBox lb) return;
        System.Windows.DragDrop.DoDragDrop(lb, new System.Windows.DataObject(_dragSource), System.Windows.DragDropEffects.Move);
        _dragSource = null;
    }

    private void PlaylistList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb || DataContext is not MainViewModel vm) return;
        if (e.Data.GetData(typeof(PlaylistRow)) is not PlaylistRow source) return;

        int index = vm.Playlist.Items.Count - 1;
        if (e.OriginalSource is DependencyObject fe && FindListBoxItem(lb, fe)?.DataContext is PlaylistRow target)
            index = vm.Playlist.Items.IndexOf(target);

        vm.Playlist.Reorder(source, index);
    }

    private static ListBoxItem? FindListBoxItem(System.Windows.Controls.ListBox lb, DependencyObject obj)
    {
        while (obj is not null and not System.Windows.Controls.ListBox)
        {
            if (obj is ListBoxItem lbi) return lbi;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
    #endregion
}
