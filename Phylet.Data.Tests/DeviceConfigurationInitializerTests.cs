using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Phylet.Data.Configuration;
using Xunit;

namespace Phylet.Data.Tests;

public sealed class DeviceConfigurationInitializerTests
{
    [Fact]
    public async Task InitializeAsync_SeedsMissingDeviceConfiguration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        var provider = new RuntimeDeviceConfigurationProvider();
        var initializer = new DeviceConfigurationInitializer(
            fixture.DbContext,
            provider,
            NullLogger<DeviceConfigurationInitializer>.Instance);

        await initializer.InitializeAsync(cancellationToken);

        var entries = await fixture.DbContext.DeviceConfigurations
            .ToListAsync(cancellationToken);
        Assert.Equal(2, entries.Count);

        var entryMap = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        Assert.Matches("^uuid:[0-9a-fA-F\\-]{36}$", entryMap[DeviceConfigurationDefaults.DeviceUuidKey]);
        Assert.Equal(DeviceConfigurationDefaults.FriendlyName, entryMap[DeviceConfigurationDefaults.FriendlyNameKey]);

        Assert.Equal(DeviceConfigurationDefaults.FriendlyName, provider.Current.FriendlyName);
        Assert.Matches("^uuid:[0-9a-fA-F\\-]{36}$", provider.Current.DeviceUuid);
    }

    [Fact]
    public async Task InitializeAsync_ReusesExistingValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        fixture.DbContext.DeviceConfigurations.AddRange(
            new DeviceConfigurationEntry
            {
                Key = DeviceConfigurationDefaults.DeviceUuidKey,
                Value = "uuid:11111111-1111-1111-1111-111111111111"
            },
            new DeviceConfigurationEntry
            {
                Key = DeviceConfigurationDefaults.FriendlyNameKey,
                Value = "Living Room"
            });
        await fixture.DbContext.SaveChangesAsync(cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        var initializer = new DeviceConfigurationInitializer(
            fixture.DbContext,
            provider,
            NullLogger<DeviceConfigurationInitializer>.Instance);

        await initializer.InitializeAsync(cancellationToken);

        Assert.Equal("uuid:11111111-1111-1111-1111-111111111111", provider.Current.DeviceUuid);
        Assert.Equal("Living Room", provider.Current.FriendlyName);
    }

    private sealed class SqliteInMemoryDbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteInMemoryDbFixture(SqliteConnection connection, PhyletDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public PhyletDbContext DbContext { get; }

        public static async Task<SqliteInMemoryDbFixture> CreateAsync()
        {
            var databaseName = $"phylet-data-tests-{Guid.NewGuid():N}";
            var connection = new SqliteConnection($"Data Source=file:{databaseName}?mode=memory&cache=shared");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PhyletDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PhyletDbContext(
                options,
                new RuntimeOptions("Phylet", "Phylet Music Server", 1800));

            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.MigrateAsync();

            return new SqliteInMemoryDbFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
