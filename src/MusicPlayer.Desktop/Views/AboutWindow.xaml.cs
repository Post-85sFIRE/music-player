using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Desktop;

namespace MusicPlayer.Desktop.Views;

public partial class AboutWindow : Window
{
    private readonly ISettingsService _settings;

    public AboutWindow()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
    }

    private void OK_Click(object sender, RoutedEventArgs e) => Close();

    private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        => _ = VersionChecker.CheckAsync(_settings, manual: true);
}
