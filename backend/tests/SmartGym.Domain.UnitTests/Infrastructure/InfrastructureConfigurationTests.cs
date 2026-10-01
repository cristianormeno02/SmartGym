using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Infrastructure;
using Xunit;

namespace SmartGym.Domain.UnitTests.Infrastructure;

public class InfrastructureConfigurationTests
{
    private const string ValidConnection = "Host=localhost;Database=smartgym_test;Username=test;Password=test";
    private const string ValidJwtKey = "test_signing_key_with_more_than_32_bytes_1234567890";

    private static IConfiguration BuildConfiguration(string? connectionString, string? jwtKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "SmartGym",
            ["Jwt:Audience"] = "SmartGymClient"
        };
        if (connectionString != null) values["ConnectionStrings:DefaultConnection"] = connectionString;
        if (jwtKey != null) values["Jwt:SecretKey"] = jwtKey;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AddInfrastructure_ShouldSucceed_WithRequiredSecretsConfigured()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(BuildConfiguration(ValidConnection, ValidJwtKey));
    }

    [Fact]
    public void AddInfrastructure_ShouldFail_WhenJwtSecretKeyIsMissing()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(BuildConfiguration(ValidConnection, jwtKey: null)));
        Assert.Contains("Jwt:SecretKey", ex.Message);
    }

    [Fact]
    public void AddInfrastructure_ShouldFail_WhenJwtSecretKeyIsTooShort()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(BuildConfiguration(ValidConnection, "short_key")));
        Assert.Contains("Jwt:SecretKey", ex.Message);
    }

    [Fact]
    public void AddInfrastructure_ShouldFail_WhenConnectionStringIsMissing()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(BuildConfiguration(connectionString: null, ValidJwtKey)));
        Assert.Contains("DefaultConnection", ex.Message);
    }
}
