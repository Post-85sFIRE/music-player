namespace MusicPlayer.Desktop;

/// <summary>当前应用版本号（与 RELEASE_NOTES / 发布包保持一致）。</summary>
public static class AppVersion
{
    public const string Current = "0.1.0";

    /// <summary>默认更新检查地址（GitHub Releases 最新版 API；可在设置里替换为自定义 JSON 端点）。</summary>
    public const string DefaultUpdateUrl = "https://api.github.com/repos/Post-85sFIRE/music-player/releases/latest";
}
