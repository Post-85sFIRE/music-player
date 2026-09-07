using System.Windows;
using MusicPlayer.Desktop.ViewModels;

namespace MusicPlayer.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => vm.RefreshSpaceCommand.Execute(null);
    }
}
