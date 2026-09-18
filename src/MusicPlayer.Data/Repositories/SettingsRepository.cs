using System.Text.Json;
using System.Text.Json.Nodes;
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
        try
        {
            // 读取时解密云盘密码（DPAPI），内存中保持明文供连接使用；解密失败（旧版明文）原样保留。
            var node = JsonNode.Parse(json);
            DecryptPasswords(node);
            return node.Deserialize<AppSettings>() ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        // 序列化后加密云盘密码再落库（DPAPI，设备级），避免明文存储凭据。
        var json = JsonSerializer.Serialize(settings);
        var node = JsonNode.Parse(json);
        EncryptPasswords(node);
        var final = node.ToJsonString();
        await conn.ExecuteAsync(@"
INSERT INTO Settings ([Key], Value) VALUES (@Key, @Value)
ON CONFLICT([Key]) DO UPDATE SET Value = @Value;", new { Key, Value = final });
    }

    private static void DecryptPasswords(JsonNode? node)
    {
        if (node?["CloudSources"] is not JsonArray arr) return;
        foreach (var item in arr)
        {
            if (item?["Password"] is JsonNode pw && pw is JsonValue)
                item["Password"] = JsonValue.Create(CloudCredentialProtector.Unprotect(pw.GetValue<string>()));
        }
    }

    private static void EncryptPasswords(JsonNode? node)
    {
        if (node?["CloudSources"] is not JsonArray arr) return;
        foreach (var item in arr)
        {
            if (item?["Password"] is JsonNode pw && pw is JsonValue)
                item["Password"] = JsonValue.Create(CloudCredentialProtector.Protect(pw.GetValue<string>()));
        }
    }
}
