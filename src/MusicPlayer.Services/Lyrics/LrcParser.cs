using System.Globalization;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services.Lyrics;

/// <summary>
/// LRC 歌词解析器。支持 [mm:ss.xx] / [mm:ss] 时间轴、一行多个时间戳、内嵌元数据标签跳过。
/// 解析失败或无时间轴时返回空数组（由调用方决定如何回退为纯文本展示）。
/// </summary>
public static class LrcParser
{
    public static LyricLine[] Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return System.Array.Empty<LyricLine>();

        var lines = new System.Collections.Generic.List<LyricLine>();
        foreach (var rawLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // 提取行首所有 [..] 时间戳（可能一行多个）
            var times = new System.Collections.Generic.List<TimeSpan>();
            var idx = 0;
            while (idx < line.Length && line[idx] == '[')
            {
                var end = line.IndexOf(']', idx);
                if (end < 0) break;
                var body = line.Substring(idx + 1, end - idx - 1);
                if (IsMetadata(body))
                {
                    idx = end + 1;
                    continue;
                }
                var ts = TryParseTime(body);
                if (ts.HasValue) times.Add(ts.Value);
                idx = end + 1;
            }

            var text = line.Substring(idx).Trim();
            foreach (var t in times)
                lines.Add(new LyricLine { Time = t, Text = text });
        }

        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines.ToArray();
    }

    /// <summary>判断 [..] 内是否为元数据标签（ti:/ar:/al:/offset: 等）而非时间轴。</summary>
    private static bool IsMetadata(string body)
    {
        var colon = body.IndexOf(':');
        if (colon <= 0) return false;
        var key = body.Substring(0, colon);
        return key is "ti" or "ar" or "al" or "by" or "offset" or "length" or "re" or "ve" or "au" or "mu" or "ap" or "kt" or "tool";
    }

    private static TimeSpan? TryParseTime(string body)
    {
        var colon = body.IndexOf(':');
        if (colon <= 0 || colon >= body.Length - 1) return null;
        if (!double.TryParse(body.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
            return null;
        var secPart = body.Substring(colon + 1);
        if (!double.TryParse(secPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
            return null;
        return TimeSpan.FromSeconds(min * 60 + sec);
    }
}
