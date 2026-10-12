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

        if (TryGetFallbackAdminConnectionString(out var localConn))
        {
            _adminConnectionString = localConn;
            ConnectionString = localConn;
            await ClearStaleMigrationLocksAsync(localConn);
            await using var fallbackContext = CreateDbContext();
            await fallbackContext.Database.MigrateAsync();
            return;
        }

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

    public SmartGymDbContext CreateDbContext(IServiceProvider? serviceProvider = null, string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
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

        var adminBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Timeout = 15,
            CommandTimeout = 30,
            Pooling = false
        };

        var dbName = $"smartgym_it_{Guid.NewGuid():N}";
        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using (var cleanupCmd = adminConn.CreateCommand())
            {
                cleanupCmd.CommandText = @"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND (
                          state = 'idle in transaction'
                          OR pid IN (SELECT pid FROM pg_locks WHERE locktype = 'advisory')
                      );";
                await cleanupCmd.ExecuteNonQueryAsync();
            }

            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(dbName);

        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = dbName,
            Timeout = 15,
            CommandTimeout = 30
        };
        var isolatedConnString = builder.ConnectionString;

        if (applyAllMigrations)
        {
            await using var dbContext = CreateDbContext(connectionString: isolatedConnString);
            await dbContext.Database.MigrateAsync();
        }

        return isolatedConnString;
    }

    private static async Task ClearStaleMigrationLocksAsync(string connString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connString)
            {
                Timeout = 15,
                CommandTimeout = 15,
                Pooling = false
            };

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            await using (var termCmd = conn.CreateCommand())
            {
                termCmd.CommandText = @"
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND (
                          state = 'idle in transaction'
                          OR pid IN (SELECT pid FROM pg_locks WHERE locktype = 'advisory')
                      );";
                await termCmd.ExecuteNonQueryAsync();
            }

            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.CommandText = @"
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = '__EFMigrationsLock') THEN
                            DELETE FROM ""__EFMigrationsLock"";
                        END IF;
                    END $$;";
                await lockCmd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            // Ignore lock cleanup errors
        }
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
                NpgsqlConnection.ClearAllPools();
                await using var adminConn = new NpgsqlConnection(_adminConnectionString);
                await adminConn.OpenAsync();
                foreach (var dbName in _createdDatabases)
                {
                    await using var dropCmd = adminConn.CreateCommand();
                    dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)";
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
