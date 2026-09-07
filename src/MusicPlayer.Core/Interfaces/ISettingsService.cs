using MusicPlayer.Core.Models;

namespace MusicPlayer.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Load();
    void Save();
}
