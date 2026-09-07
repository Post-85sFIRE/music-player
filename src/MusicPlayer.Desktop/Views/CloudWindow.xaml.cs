using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Desktop.ViewModels;

namespace MusicPlayer.Desktop.Views;

public partial class CloudWindow : Window
{
    public CloudWindow(CloudViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Entry_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is CloudEntry entry && DataContext is CloudViewModel vm)
        {
            // Explorer 式：文件夹双击进入下一层；音乐文件双击加入播放列表（不自动播放）。
            if (entry.IsFolder) vm.BrowseCommand.Execute(entry);
            else vm.AddToPlaylistCommand.Execute(entry);
        }
    }

    /// <summary>
    /// PasswordBox.Password 不是依赖属性，MVVM 绑定传不过来，所以连接按钮走 Code-Behind 事件，
    /// 直接把密码交给 ViewModel 的 ConnectCommand。
    /// </summary>
    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CloudViewModel vm)
        {
            vm.ConnectCommand.Execute(PwdBox.Password);
        }
    }

    /// <summary>
    /// 选中已保存来源时：回填表单（含 PasswordBox），并自动浏览根目录。
    /// 启动时 ReconnectSavedAsync 设置 SelectedSource 也会触发此事件，实现打开窗口自动加载内容。
    /// </summary>
    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CloudViewModel vm) return;
        if (SourcesList.SelectedItem is not MusicPlayer.Core.Models.CloudSourceConfig cfg) return;

        vm.NewName = cfg.Name;
        vm.NewUrl = cfg.BaseUrl;
        vm.NewUser = cfg.UserName;
        vm.NewPassword = cfg.Password;
        PwdBox.Password = cfg.Password;

        if (vm.BrowseCommand.CanExecute(null))
            vm.BrowseCommand.Execute(null);
    }
}
