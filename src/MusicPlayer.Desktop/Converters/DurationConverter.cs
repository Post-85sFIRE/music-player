using System;
using System.Globalization;
using System.Windows.Data;

namespace MusicPlayer.Desktop.Converters;

/// <summary>把秒数（double）格式化为 m:ss 的时长文本，0 或负数显示空字符串。</summary>
public class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double sec && sec > 0)
        {
            var ts = TimeSpan.FromSeconds(sec);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
