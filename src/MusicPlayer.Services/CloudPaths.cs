namespace MusicPlayer.Services;

/// <summary>
/// 云盘工作目录约定（与鸿蒙版本保持一致）：
/// 应用唯一云目录固定为 <c>yunMusicPlayer</c>，歌词单独放在其下的 <c>Lyric</c> 子目录；
/// 该路径不可由用户修改（相对 WebDAV 根 <see cref="MusicPlayer.Core.Models.CloudSourceConfig.BaseUrl"/> 始终追加此子路径）。
/// </summary>
public static class CloudPaths
{
    /// <summary>应用唯一的云盘根目录（相对 WebDAV 根）。</summary>
    public const string AppRoot = "yunMusicPlayer";

    /// <summary>歌词子目录（相对 WebDAV 根）。</summary>
    public const string LyricDir = "yunMusicPlayer/Lyric";

    /// <summary>播放列表快照文件名（相对 WebDAV 根）。</summary>
    public const string PlaylistFile = "yunMusicPlayer/playlist.json";

    /// <summary>音乐文件上传路径：yunMusicPlayer/&lt;文件名&gt;。</summary>
    public static string MusicPath(string fileName) => AppRoot + "/" + fileName.TrimStart('/');

    /// <summary>歌词文件上传路径：yunMusicPlayer/Lyric/&lt;文件名&gt;。</summary>
    public static string LyricPath(string fileName) => LyricDir + "/" + fileName.TrimStart('/');
}
