using System.Windows;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Desktop;

public class MessageBoxNotificationService : INotificationService
{
    public void Notify(string message, string? title = null)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.MessageBox.Show(message, title ?? "提示", MessageBoxButton.OK));
    }
}
