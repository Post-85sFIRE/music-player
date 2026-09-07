using System;
using System.Globalization;
using System.Windows.Data;

namespace MusicPlayer.Desktop.Converters;

/// <summary>
/// 多值转换器：比较 [曲目 TrackId, 当前播放项 CurrentItemId] 是否相等，相等返回 true，
/// 用于播放列表「正在播放」行高亮。任一为 null 时均视为不相等。
/// </summary>
public class IsCurrentItemConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not { Length: >= 2 }) return false;
        var a = values[0]?.ToString();
        var b = values[1]?.ToString();
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
