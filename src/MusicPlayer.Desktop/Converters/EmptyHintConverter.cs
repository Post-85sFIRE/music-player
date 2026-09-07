using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicPlayer.Desktop.Converters;

/// <summary>
/// 把条目数量转成可见性：0 项 → 可见（显示空态提示），否则折叠。
/// </summary>
public class EmptyHintConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
