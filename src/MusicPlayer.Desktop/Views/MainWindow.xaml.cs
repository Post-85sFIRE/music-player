using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Core.Enums;
using MusicPlayer.Core.Models;
using MusicPlayer.Core.Lyrics;
using MusicPlayer.Desktop.ViewModels;
using MusicPlayer.Desktop.Views;
using System.Windows.Forms;
using System.ComponentModel;

namespace MusicPlayer.Desktop.Views;

public partial class MainWindow : Window
{
    private NotifyIcon? _notify;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        InitTrayIcon();
    }

    private void InitTrayIcon()
    {
        try
        {
            _notify = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "音乐播放器",
                Visible = false
            };
            // 左键单击 / 双击还原窗口；右键由 ContextMenuStrip 自动弹出菜单，不还原窗口（符合"右键只显示菜单，左键/双击才弹窗"）。
            _notify.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
            _notify.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };

            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("显示", null, (_, _) => RestoreFromTray());
            trayMenu.Items.Add("退出", null, (_, _) => ExitApp());
            _notify.ContextMenuStrip = trayMenu;
        }
        catch
        {
            // 托盘初始化失败不应影响主窗口。
            _notify = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));
        PlaylistList.ContextMenuOpening += PlaylistList_ContextMenuOpening;
        if (DataContext is MainViewModel vm)
            vm.Playback.LyricsCandidatesRequested += OnLyricsCandidatesRequested;
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

    private void PlaylistOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is not PlaylistRow row || DataContext is not MainViewModel vm) return;
        var t = row.Item.Track;
        if (t is null) return;
        if (t.SourceType == TrackSourceType.Local && !string.IsNullOrEmpty(t.FilePath))
        {
            var dir = Path.GetDirectoryName(t.FilePath);
            if (dir is not null) OpenInExplorer(dir, t.FilePath);
        }
        else if (t.SourceType == TrackSourceType.Cloud && row.IsCached)
        {
            OpenInExplorer(vm.Playlist.CacheRoot, null);
        }
    }

    private void PlaylistUpload_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistRow row && DataContext is MainViewModel vm)
            vm.Playlist.UploadToCloudCommand.Execute(row);
    }

    private void PlaylistDownloadAs_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistRow row && DataContext is MainViewModel vm)
            vm.Playlist.DownloadAsCommand.Execute(row);
    }

    /// <summary>右键菜单打开前，把鼠标下的行设为选中项，确保"打开文件夹/上传/下载"作用于右键目标。</summary>
    private void PlaylistList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var pt = Mouse.GetPosition(PlaylistList);
        var hit = VisualTreeHelper.HitTest(PlaylistList, pt)?.VisualHit;
        while (hit != null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        if (hit is ListBoxItem lbi && lbi.DataContext is PlaylistRow row) PlaylistList.SelectedItem = row;
    }

    /// <summary>右键菜单弹出时，按曲目类型/缓存状态调整各项可见性。</summary>
    private void PlaylistContextMenu_Opened(object? sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is not PlaylistRow row) return;
        var t = row.Item.Track;
        var isLocal = t?.SourceType == TrackSourceType.Local;
        var isCloud = t?.SourceType == TrackSourceType.Cloud;
        MiOpenFolder.Visibility = (isLocal || (isCloud && row.IsCached)) ? Visibility.Visible : Visibility.Collapsed;
        MiUpload.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
        MiDownload.Visibility = isCloud ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OpenInExplorer(string dir, string? file)
    {
        try
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            else if (Directory.Exists(dir))
                Process.Start("explorer.exe", dir);
        }
        catch
        {
            // 资源管理器启动失败忽略。
        }
    }

    private void OnLyricsCandidatesRequested(object? sender, LyricsCandidatesRequestedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dlg = new LyricCandidateWindow(e.Candidates) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Selected is { } c && DataContext is MainViewModel vm)
                vm.Playback.ChooseCandidate(c);
        });
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

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

    private void ExitApp()
    {
        _forceClose = true;
        Close();
    }

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

    #region 最小化到状态栏 + 托盘
    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            if (_notify is not null) _notify.Visible = true;
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 未显式退出时，关闭按钮改为最小化到托盘，避免误关丢失播放。
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            if (_notify is not null) _notify.Visible = true;
            return;
        }
        _notify?.Dispose();
        base.OnClosing(e);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_notify is not null) _notify.Visible = false;
    }
    #endregion

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
