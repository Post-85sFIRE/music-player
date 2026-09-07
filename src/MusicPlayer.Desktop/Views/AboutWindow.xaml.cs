using System.Windows;

namespace MusicPlayer.Desktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OK_Click(object sender, RoutedEventArgs e) => Close();
}
