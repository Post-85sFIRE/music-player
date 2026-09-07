using System;
using System.IO;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services;

public class SettingsService : ISettingsService
{
    private readonly Data.Repositories.SettingsRepository _repo;

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(Data.Repositories.SettingsRepository repo) => _repo = repo;

    public void Load()
    {
        Settings = _repo.LoadAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(Settings.DownloadPath))
            Settings.DownloadPath = DefaultDownloadPath();
    }

    public void Save() => _repo.SaveAsync(Settings).GetAwaiter().GetResult();

    public static string DefaultDownloadPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Path.Combine(root, "MusicPlayerCache");
    }
}
