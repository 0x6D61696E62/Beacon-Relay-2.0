using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Options;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class SqliteDatabasePathTests
{
    [Fact]
    public void NormalizeConnectionString_NormalizesWindowsSeparators()
    {
        var connectionString = SqliteDatabasePath.NormalizeConnectionString("Data Source=db\\nested\\beacon-relay.db");

        Assert.Equal($"Data Source=db{Path.DirectorySeparatorChar}nested{Path.DirectorySeparatorChar}beacon-relay.db", connectionString);
    }

    [Fact]
    public void GetDirectoryPath_NormalizesWindowsSeparators()
    {
        var directory = SqliteDatabasePath.GetDirectoryPath("Data Source=db\\nested\\beacon-relay.db");

        Assert.Equal(Path.Combine("db", "nested"), directory);
    }

    [Fact]
    public void GetDirectoryPath_ReturnsNullForInMemoryDatabase()
    {
        var directory = SqliteDatabasePath.GetDirectoryPath("Data Source=:memory:");

        Assert.Null(directory);
    }

    [Fact]
    public void DatabaseOptions_UsesPortableDefaultConnectionString()
    {
        var options = new DatabaseOptions();

        Assert.Equal("Data Source=db/beacon-relay.db", options.ConnectionString);
    }
}
