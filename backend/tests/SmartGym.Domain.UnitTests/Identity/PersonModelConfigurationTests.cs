using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class PersonModelConfigurationTests
{
    [Fact]
    public void DniIndex_ShouldBeUniqueOnlyForInformedValues()
    {
        // Se construye el modelo para PostgreSQL sin abrir conexión.
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        var personType = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Person))!;

        var dniProperty = personType.FindProperty(nameof(Person.Dni))!;
        Assert.True(dniProperty.IsNullable);

        var dniIndex = Assert.Single(personType.GetIndexes(), i => i.Properties.Single().Name == nameof(Person.Dni));
        Assert.True(dniIndex.IsUnique);
        Assert.Equal("\"Dni\" IS NOT NULL", dniIndex.GetFilter());
    }
}
