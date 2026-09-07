using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using MusicPlayer.Data.Db;

namespace MusicPlayer.Data.Repositories;

public class PlaylistRepository : IPlaylistRepository
{
    private readonly AppDbFactory _factory;
    private const string DefaultName = "默认列表";

    public PlaylistRepository(AppDbFactory factory) => _factory = factory;

    public async Task<Playlist> GetOrCreateDefaultAsync()
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();

        var id = await conn.ExecuteScalarAsync<long?>("SELECT Id FROM Playlists WHERE Name = @Name", new { Name = DefaultName });
        if (id is null)
        {
            id = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO Playlists (Name, CreatedUtc) VALUES (@Name, @CreatedUtc); SELECT last_insert_rowid();",
                new { Name = DefaultName, CreatedUtc = DateTime.UtcNow.ToString("o") });
        }

        var playlist = new Playlist { Id = id.Value, Name = DefaultName };

        var rows = await conn.QueryAsync(@"
SELECT pi.Id AS ItemId, pi.PlaylistId, pi.TrackId, pi.[Order] AS ItemOrder,
       t.Title, t.Artist, t.Album, t.AlbumArtist, t.DurationSec, t.Codec, t.FilePath,
       t.CoverHash, t.SourceType, t.SourceId
FROM PlaylistItems pi
JOIN Tracks t ON t.Id = pi.TrackId
WHERE pi.PlaylistId = @PlaylistId
ORDER BY pi.[Order]", new { PlaylistId = id.Value });

        foreach (var r in rows) playlist.Items.Add(MapItem(r));

        return playlist;
    }

    public async Task SaveAsync(Playlist playlist)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();

        if (playlist.Id == 0)
        {
            playlist.Id = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO Playlists (Name, CreatedUtc) VALUES (@Name, @CreatedUtc); SELECT last_insert_rowid();",
                new { playlist.Name, CreatedUtc = DateTime.UtcNow.ToString("o") });
        }

        await conn.ExecuteAsync("DELETE FROM PlaylistItems WHERE PlaylistId = @PlaylistId",
            new { PlaylistId = playlist.Id });

        for (var i = 0; i < playlist.Items.Count; i++)
        {
            var it = playlist.Items[i];
            it.Order = i;
            it.PlaylistId = playlist.Id;
            it.Id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO PlaylistItems (PlaylistId, TrackId, [Order]) VALUES (@PlaylistId, @TrackId, @Order);
SELECT last_insert_rowid();", new { it.PlaylistId, it.TrackId, it.Order });
        }
    }

    public async Task<PlaylistItem?> GetItemAsync(long itemId)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var r = await conn.QueryFirstOrDefaultAsync(@"
SELECT pi.Id AS ItemId, pi.PlaylistId, pi.TrackId, pi.[Order] AS ItemOrder,
       t.Title, t.Artist, t.Album, t.AlbumArtist, t.DurationSec, t.Codec, t.FilePath,
       t.CoverHash, t.SourceType, t.SourceId
FROM PlaylistItems pi JOIN Tracks t ON t.Id = pi.TrackId WHERE pi.Id = @ItemId",
            new { ItemId = itemId });
        return r is null ? null : MapItem(r);
    }

    public async Task RemoveItemAsync(long itemId)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM PlaylistItems WHERE Id = @ItemId", new { ItemId = itemId });
    }

    public async Task ReorderAsync(long itemId, int order)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE PlaylistItems SET [Order] = @Order WHERE Id = @ItemId",
            new { ItemId = itemId, Order = order });
    }

    public async Task AddFolderAsync(LibraryFolder folder)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
INSERT INTO LibraryFolders (Path, Recursive, AddedUtc, LastScanUtc)
VALUES (@Path, @Recursive, @AddedUtc, @LastScanUtc)
ON CONFLICT(Path) DO UPDATE SET Recursive=@Recursive;",
            new { folder.Path, Recursive = folder.Recursive ? 1 : 0, AddedUtc = folder.AddedUtc.ToString("o"), LastScanUtc = folder.LastScanUtc.ToString("o") });
    }

    public async Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync()
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync("SELECT * FROM LibraryFolders ORDER BY Path");
        var list = new List<LibraryFolder>();
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object>)r;
            list.Add(new LibraryFolder
            {
                Id = Convert.ToInt64(d["Id"]),
                Path = d["Path"]?.ToString() ?? "",
                Recursive = Convert.ToInt32(d["Recursive"]) != 0,
                AddedUtc = ParseUtc(d["AddedUtc"]),
                LastScanUtc = ParseUtc(d["LastScanUtc"])
            });
        }
        return list;
    }

    private static PlaylistItem MapItem(dynamic r)
    {
        var d = (IDictionary<string, object>)r;
        string Str(object? v) => v?.ToString() ?? "";
        return new PlaylistItem
        {
            Id = Convert.ToInt64(d["ItemId"]),
            PlaylistId = Convert.ToInt64(d["PlaylistId"]),
            TrackId = Convert.ToInt64(d["TrackId"]),
            Order = Convert.ToInt32(d["ItemOrder"]),
            Track = new Track
            {
                Id = Convert.ToInt64(d["TrackId"]),
                Title = Str(d["Title"]),
                Artist = Str(d["Artist"]),
                Album = Str(d["Album"]),
                AlbumArtist = Str(d["AlbumArtist"]),
                DurationSec = d.TryGetValue("DurationSec", out var dur) ? Convert.ToDouble(dur) : 0,
                Codec = Str(d["Codec"]),
                FilePath = Str(d["FilePath"]),
                CoverHash = d["CoverHash"] == null ? null : Str(d["CoverHash"]),
                SourceType = d.TryGetValue("SourceType", out var st) ? (MusicPlayer.Core.Enums.TrackSourceType)Convert.ToInt32(st) : MusicPlayer.Core.Enums.TrackSourceType.Local,
                SourceId = d["SourceId"] == null ? null : Str(d["SourceId"])
            }
        };
    }

    private static DateTime ParseUtc(object? v) =>
        DateTime.TryParse(v?.ToString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : DateTime.UtcNow;
}
