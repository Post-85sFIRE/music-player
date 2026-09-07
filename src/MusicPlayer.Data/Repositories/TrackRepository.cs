using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;
using MusicPlayer.Data.Db;

namespace MusicPlayer.Data.Repositories;

public class TrackRepository : ITrackRepository
{
    private readonly AppDbFactory _factory;

    public TrackRepository(AppDbFactory factory) => _factory = factory;

    public async Task<long> UpsertAsync(Track t)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();

        var existing = await conn.ExecuteScalarAsync<long?>(
            "SELECT Id FROM Tracks WHERE FilePath = @FilePath", new { t.FilePath });

        if (existing is not null)
        {
            t.Id = existing.Value;
            await conn.ExecuteAsync(@"
UPDATE Tracks SET
    Title=@Title, Artist=@Artist, Album=@Album, AlbumArtist=@AlbumArtist, Year=@Year,
    Genre=@Genre, DurationSec=@DurationSec, BitRate=@BitRate, SampleRate=@SampleRate,
    BitsPerSample=@BitsPerSample, Codec=@Codec, FileSize=@FileSize, LastModifiedUtc=@LastModifiedUtc,
    CoverHash=@CoverHash, SourceType=@SourceType, SourceId=@SourceId,
    ReplayGainTrackGain=@ReplayGainTrackGain, ReplayGainAlbumGain=@ReplayGainAlbumGain,
    ReplayGainTrackPeak=@ReplayGainTrackPeak, ReplayGainAlbumPeak=@ReplayGainAlbumPeak
WHERE Id=@Id", t);
            return t.Id;
        }

        t.Id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO Tracks (Title, Artist, Album, AlbumArtist, Year, Genre, DurationSec, BitRate,
    SampleRate, BitsPerSample, Codec, FilePath, FileSize, LastModifiedUtc, CoverHash, SourceType, SourceId,
    ReplayGainTrackGain, ReplayGainAlbumGain, ReplayGainTrackPeak, ReplayGainAlbumPeak, DateAddedUtc)
VALUES (@Title, @Artist, @Album, @AlbumArtist, @Year, @Genre, @DurationSec, @BitRate,
    @SampleRate, @BitsPerSample, @Codec, @FilePath, @FileSize, @LastModifiedUtc, @CoverHash, @SourceType, @SourceId,
    @ReplayGainTrackGain, @ReplayGainAlbumGain, @ReplayGainTrackPeak, @ReplayGainAlbumPeak, @DateAddedUtc);
SELECT last_insert_rowid();", t);
        return t.Id;
    }

    public async Task<Track?> GetByIdAsync(long id)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM Tracks WHERE Id = @id", new { id });
        return row is null ? null : Map(row);
    }

    public async Task<Track?> GetByPathAsync(string filePath)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM Tracks WHERE FilePath = @FilePath",
            new { FilePath = filePath });
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Track>> GetAllAsync()
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync("SELECT * FROM Tracks ORDER BY AlbumArtist, Album, TrackNumber");
        var list = new List<Track>();
        foreach (var r in rows) list.Add(Map(r));
        return list;
    }

    public async Task<IReadOnlyList<Track>> SearchAsync(string query)
    {
        var q = $"%{query}%";
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync(
            "SELECT * FROM Tracks WHERE Title LIKE @q OR Artist LIKE @q OR Album LIKE @q ORDER BY AlbumArtist, Album",
            new { q });
        var list = new List<Track>();
        foreach (var r in rows) list.Add(Map(r));
        return list;
    }

    public async Task DeleteByPathAsync(string filePath)
    {
        await using var conn = _factory.Open();
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM Tracks WHERE FilePath = @FilePath", new { FilePath = filePath });
    }

    public async Task DeleteMissingAsync(IEnumerable<string> existingPaths)
    {
        var paths = existingPaths.ToList();
        if (paths.Count == 0)
        {
            await using var c = _factory.Open();
            await c.OpenAsync();
            await c.ExecuteAsync("DELETE FROM Tracks");
            return;
        }

        await using var conn = _factory.Open();
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM Tracks WHERE FilePath NOT IN @paths", new { paths });
    }

    private static Track Map(dynamic r)
    {
        var d = (IDictionary<string, object>)r;
        string Str(object? v) => v?.ToString() ?? "";
        DateTime Dt(object? v) =>
            DateTime.TryParse(Str(v), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : DateTime.UtcNow;

        return new Track
        {
            Id = Convert.ToInt64(d["Id"]),
            Title = Str(d["Title"]),
            Artist = Str(d["Artist"]),
            Album = Str(d["Album"]),
            AlbumArtist = Str(d["AlbumArtist"]),
            Year = d.TryGetValue("Year", out var y) ? Convert.ToInt32(y) : 0,
            Genre = Str(d["Genre"]),
            DurationSec = d.TryGetValue("DurationSec", out var dur) ? Convert.ToDouble(dur) : 0,
            BitRate = d.TryGetValue("BitRate", out var br) ? Convert.ToInt32(br) : 0,
            SampleRate = d.TryGetValue("SampleRate", out var sr) ? Convert.ToInt32(sr) : 0,
            BitsPerSample = d.TryGetValue("BitsPerSample", out var bps) ? Convert.ToInt32(bps) : 0,
            Codec = Str(d["Codec"]),
            FilePath = Str(d["FilePath"]),
            FileSize = d.TryGetValue("FileSize", out var fs) ? Convert.ToInt64(fs) : 0,
            LastModifiedUtc = Dt(d["LastModifiedUtc"]),
            CoverHash = d["CoverHash"] == null ? null : Str(d["CoverHash"]),
            SourceType = d.TryGetValue("SourceType", out var st) ? (MusicPlayer.Core.Enums.TrackSourceType)Convert.ToInt32(st) : MusicPlayer.Core.Enums.TrackSourceType.Local,
            SourceId = d["SourceId"] == null ? null : Str(d["SourceId"]),
            ReplayGainTrackGain = d.TryGetValue("ReplayGainTrackGain", out var rgt) ? Convert.ToDouble(rgt) : 0,
            ReplayGainAlbumGain = d.TryGetValue("ReplayGainAlbumGain", out var rga) ? Convert.ToDouble(rga) : 0,
            ReplayGainTrackPeak = d.TryGetValue("ReplayGainTrackPeak", out var rgp) ? Convert.ToDouble(rgp) : 0,
            ReplayGainAlbumPeak = d.TryGetValue("ReplayGainAlbumPeak", out var rgap) ? Convert.ToDouble(rgap) : 0,
            DateAddedUtc = Dt(d["DateAddedUtc"])
        };
    }
}
