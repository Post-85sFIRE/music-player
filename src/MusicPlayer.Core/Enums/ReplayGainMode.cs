namespace MusicPlayer.Core.Enums;

/// <summary>
/// ReplayGain 应用模式。
/// Off = 不处理（原始音量）；
/// Track = 按曲目增益（曲间响度统一）；
/// Album = 按专辑增益（保留专辑内动态起伏，跨专辑统一）。
/// </summary>
public enum ReplayGainMode
{
    Off,
    Track,
    Album
}
