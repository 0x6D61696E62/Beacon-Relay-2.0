using Microsoft.Data.Sqlite;

namespace BeaconRelay.LpdReceiver.Data;

public static class SqliteDatabasePath
{
    public static string? NormalizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource?.Trim();
        if (!string.IsNullOrWhiteSpace(dataSource) && !string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            builder.DataSource = dataSource
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
        }

        return builder.ToString();
    }

    public static string? GetDirectoryPath(string? connectionString)
    {
        var normalizedConnectionString = NormalizeConnectionString(connectionString);
        if (string.IsNullOrWhiteSpace(normalizedConnectionString))
        {
            return null;
        }

        var builder = new SqliteConnectionStringBuilder(normalizedConnectionString);
        var dataSource = builder.DataSource?.Trim();
        if (string.IsNullOrWhiteSpace(dataSource) || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(dataSource);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    public static void EnsureDirectoryExists(string? connectionString)
    {
        var directory = GetDirectoryPath(connectionString);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
