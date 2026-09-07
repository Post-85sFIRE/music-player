using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MusicPlayer.Core.Models;
using MusicPlayer.Data.Db;

namespace MusicPlayer.Data.Repositories;

public class SettingsRepository
{
    private readonly AppDbFactory _factory;
    private const string Key = "AppSettings";

    public SettingsRepository(AppDbFactory factory) => _factory = factory;

    public async Task<AppSettings> LoadAsync()
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var json = await conn.ExecuteScalarAsync<string?>("SELECT Value FROM Settings WHERE [Key] = @Key", new { Key });
        if (string.IsNullOrWhiteSpace(json)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var json = JsonSerializer.Serialize(settings);
        await conn.ExecuteAsync(@"
INSERT INTO Settings ([Key], Value) VALUES (@Key, @Value)
ON CONFLICT([Key]) DO UPDATE SET Value = @Value;", new { Key, Value = json });
    }
}
