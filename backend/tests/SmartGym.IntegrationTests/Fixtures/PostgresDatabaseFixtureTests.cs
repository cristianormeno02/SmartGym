using Npgsql;
using Xunit;

namespace SmartGym.IntegrationTests.Fixtures;

[Collection("PostgresIntegration")]
public class PostgresDatabaseFixtureTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public PostgresDatabaseFixtureTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SharedDatabase_ShouldBeDisposableTestDatabase_NeverTheDevelopmentDatabase()
    {
        var database = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString).Database!;

        Assert.True(
            database == "smartgym_test" || database.StartsWith("smartgym_it_", StringComparison.Ordinal),
            $"La base compartida de pruebas debe ser el contenedor 'smartgym_test' o una base aislada 'smartgym_it_*', no '{database}'.");
    }

    [Fact]
    public async Task CreateIsolatedDatabaseAsync_ShouldCreateSmartgymItDatabase()
    {
        var connectionString = await _fixture.CreateIsolatedDatabaseAsync();

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        Assert.StartsWith("smartgym_it_", database);
        Assert.NotEqual(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString).Database, database);
    }
}
