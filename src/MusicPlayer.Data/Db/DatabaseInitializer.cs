using Dapper;
using Microsoft.Data.Sqlite;

namespace MusicPlayer.Data.Db;

public static class DatabaseInitializer
{
    public static void EnsureCreated(AppDbFactory factory)
    {
        using var conn = factory.Open();
        conn.Open();
        conn.Execute(@"
CREATE TABLE IF NOT EXISTS Tracks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL DEFAULT '',
    Artist TEXT NOT NULL DEFAULT '',
    Album TEXT NOT NULL DEFAULT '',
    AlbumArtist TEXT NOT NULL DEFAULT '',
    Year INTEGER NOT NULL DEFAULT 0,
    TrackNumber INTEGER NOT NULL DEFAULT 0,
    Genre TEXT NOT NULL DEFAULT '',
    DurationSec REAL NOT NULL DEFAULT 0,
    BitRate INTEGER NOT NULL DEFAULT 0,
    SampleRate INTEGER NOT NULL DEFAULT 0,
    BitsPerSample INTEGER NOT NULL DEFAULT 0,
    Codec TEXT NOT NULL DEFAULT '',
    FilePath TEXT NOT NULL UNIQUE,
    FileSize INTEGER NOT NULL DEFAULT 0,
    LastModifiedUtc TEXT NOT NULL DEFAULT '',
    CoverHash TEXT,
    SourceType INTEGER NOT NULL DEFAULT 0,
    SourceId TEXT,
    ReplayGainTrackGain REAL NOT NULL DEFAULT 0,
    ReplayGainAlbumGain REAL NOT NULL DEFAULT 0,
    ReplayGainTrackPeak REAL NOT NULL DEFAULT 0,
    ReplayGainAlbumPeak REAL NOT NULL DEFAULT 0,
    DateAddedUtc TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS Playlists (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL DEFAULT '默认列表',
    CreatedUtc TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS PlaylistItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlaylistId INTEGER NOT NULL,
    TrackId INTEGER NOT NULL,
    [Order] INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
    FOREIGN KEY (TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS LibraryFolders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL UNIQUE,
    Recursive INTEGER NOT NULL DEFAULT 1,
    AddedUtc TEXT NOT NULL DEFAULT '',
    LastScanUtc TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS Settings (
    [Key] TEXT PRIMARY KEY,
    Value TEXT NOT NULL DEFAULT ''
);
");

        // 兼容旧库：早期版本建表漏了 TrackNumber 列，存在则跳过、缺失则补齐。
        var hasTrackNumber = conn.ExecuteScalar<int?>(
            "SELECT 1 FROM pragma_table_info('Tracks') WHERE name='TrackNumber'");
        if (hasTrackNumber is null)
        {
            conn.Execute("ALTER TABLE Tracks ADD COLUMN TrackNumber INTEGER NOT NULL DEFAULT 0");
        }

        // 兼容旧库：补齐 ReplayGain 四列（早期库无 RG 字段）。
        foreach (var col in new[] { "ReplayGainTrackGain", "ReplayGainAlbumGain", "ReplayGainTrackPeak", "ReplayGainAlbumPeak" })
        {
            var has = conn.ExecuteScalar<int?>($"SELECT 1 FROM pragma_table_info('Tracks') WHERE name='{col}'");
            if (has is null)
                conn.Execute($"ALTER TABLE Tracks ADD COLUMN {col} REAL NOT NULL DEFAULT 0");
        }
    }
}
