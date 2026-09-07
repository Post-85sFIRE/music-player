using System;
using System.IO;

namespace MusicPlayer.Audio;

/// <summary>
/// 极简诊断日志：把音频引擎初始化的关键步骤顺序写入 %LocalAppData%\MusicPlayer\diag.log。
/// 存在的意义：BASS_Init 在部分环境（无音频设备 / 无显示器 / 服务未启动）会**静默挂起或失败**，
/// 而 Console 输出在无显示器场景常被吞掉。逐步骤落盘后，用户报障时把日志发来即可定位卡在哪一步。
/// 所有写入都吞异常：诊断绝不能影响启动。
/// </summary>
public static class Diagnostics
{
    private static readonly object Sync = new();
    private static string? _path;

    /// <summary>日志文件路径：exe 同目录的 diag.log（真便携，随程序走）</summary>
    public static string LogPath
    {
        get
        {
            if (_path is null)
            {
                var dir = AppContext.BaseDirectory;
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "diag.log");
            }
            return _path;
        }
    }

    /// <summary>追加一行带时间戳的诊断记录。</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断失败无所谓
        }
    }

    /// <summary>清空日志（每次启动重新记录一轮）。</summary>
    public static void Reset()
    {
        try
        {
            lock (Sync)
            {
                File.WriteAllText(LogPath, $"=== MusicPlayer 诊断日志 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断失败无所谓
        }
    }
}
