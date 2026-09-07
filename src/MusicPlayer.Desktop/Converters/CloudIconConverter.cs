using System;
using System.Globalization;
using System.Windows.Data;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Desktop.Converters;

/// <summary>
/// 云盘条目图标：文件夹 📁 / 音乐 🎵 / 其它文件 📄。
/// 绑定整条 CloudEntry，按 IsFolder / IsMusic 选择。
/// </summary>
public class CloudIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CloudEntry e) return "📄";
        if (e.IsFolder) return "📁";
        return e.IsMusic ? "🎵" : "📄";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
