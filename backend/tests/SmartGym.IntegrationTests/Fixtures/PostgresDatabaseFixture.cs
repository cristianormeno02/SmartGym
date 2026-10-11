using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartGym.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartGym.IntegrationTests.Fixtures;

[CollectionDefinition("PostgresIntegration")]
public class PostgresIntegrationCollection : ICollectionFixture<PostgresDatabaseFixture>
{
}

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private readonly List<string> _createdDatabases = new();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable("SMARTGYM_TEST_POSTGRES_CONNECTION");
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            _adminConnectionString = externalConnectionString;
            ConnectionString = await CreateIsolatedDatabaseAsync(applyAllMigrations: true);
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("smartgym_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
            ConnectionString = _adminConnectionString;

            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
        }
        catch (Exception ex) when (TryGetFallbackAdminConnectionString(out var fallbackConn))
        {
            _container = null;
            _adminConnectionString = fallbackConn;
            try
            {
                ConnectionString = await CreateIsolatedDatabaseAsync(applyAllMigrations: true);
            }
            catch (Exception innerEx)
            {
                throw new InvalidOperationException(
                    $"Could not start Testcontainers PostgreSQL ({ex.Message}) nor create an isolated test database on fallback PostgreSQL ({innerEx.Message}).",
                    innerEx);
            }
        }
    }

    public SmartGymDbContext CreateDbContext(IServiceProvider? serviceProvider = null, string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString)
            .Options;

        return new SmartGymDbContext(options, serviceProvider);
    }

    /// <summary>
    /// Creates a fresh isolated database on the active PostgreSQL server, optionally applying all migrations.
    /// Useful for migration tests that need to migrate only up to a specific prior migration first.
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync(bool applyAllMigrations = false)
    {
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            throw new InvalidOperationException("PostgreSQL fixture is not initialized.");
        }

        var dbName = $"smartgym_it_{Guid.NewGuid():N}";
        await using (var adminConn = new NpgsqlConnection(_adminConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(dbName);

        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = dbName
        };
        var isolatedConnString = builder.ConnectionString;

        if (applyAllMigrations)
        {
            await using var dbContext = CreateDbContext(connectionString: isolatedConnString);
            await dbContext.Database.MigrateAsync();
        }

        return isolatedConnString;
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_adminConnectionString) && _createdDatabases.Count > 0)
        {
            try
            {
                await using var adminConn = new NpgsqlConnection(_adminConnectionString);
                await adminConn.OpenAsync();
                foreach (var dbName in _createdDatabases)
                {
                    await using var terminateCmd = adminConn.CreateCommand();
                    terminateCmd.CommandText = $@"
                        SELECT pg_terminate_backend(pid)
                        FROM pg_stat_activity
                        WHERE datname = '{dbName}' AND pid <> pg_backend_pid();";
                    await terminateCmd.ExecuteNonQueryAsync();

                    await using var dropCmd = adminConn.CreateCommand();
                    dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\"";
                    await dropCmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Ignore cleanup errors on dispose
            }
        }
    }

    private static bool TryGetFallbackAdminConnectionString(out string connectionString)
    {
        connectionString = string.Empty;
        try
        {
            var secretsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets",
                "3ec3ec2d-090d-497a-823e-612abd3ec18f",
                "secrets.json");

            if (!File.Exists(secretsPath))
            {
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(secretsPath));
            if (doc.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var prop) &&
                !string.IsNullOrWhiteSpace(prop.GetString()))
            {
                connectionString = prop.GetString()!;
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }
}
