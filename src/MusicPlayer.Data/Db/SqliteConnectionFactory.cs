using Microsoft.Data.Sqlite;

namespace MusicPlayer.Data.Db;

public class AppDbFactory
{
    private readonly string _connString;

    public AppDbFactory(string dbPath)
    {
        _connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public SqliteConnection Open() => new SqliteConnection(_connString);
}
