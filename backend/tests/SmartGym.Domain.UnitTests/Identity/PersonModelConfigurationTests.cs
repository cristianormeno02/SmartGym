using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class PersonModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void DocumentIndex_ShouldBeUniqueAndFiltered()
    {
        var model = BuildModel();
        var personType = model.FindEntityType(typeof(Person))!;
        var docNavigation = personType.FindNavigation(nameof(Person.Document))!;
        var docEntityType = docNavigation.TargetEntityType;

        var docIndex = Assert.Single(docEntityType.GetIndexes());

        Assert.True(docIndex.IsUnique);
        Assert.Contains("DocumentNumberNormalized", docIndex.GetFilter() ?? "");
        Assert.Equal("IX_People_Document_Unique", docIndex.GetDatabaseName());
    }

    [Fact]
    public void EmailIndex_ShouldBeUniqueAndFilteredForNonNull()
    {
        var model = BuildModel();
        var personType = model.FindEntityType(typeof(Person))!;

        var emailProp = personType.FindProperty(nameof(Person.Email))!;
        Assert.True(emailProp.IsNullable);

        var emailIndex = Assert.Single(personType.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(Person.Email));
        Assert.True(emailIndex.IsUnique);
        Assert.Equal("\"Email\" IS NOT NULL", emailIndex.GetFilter());
        Assert.Equal("IX_People_Email_Unique", emailIndex.GetDatabaseName());
    }

    [Fact]
    public void Enums_ShouldHaveStringConversion()
    {
        var model = BuildModel();
        var personType = model.FindEntityType(typeof(Person))!;

        var statusProp = personType.FindProperty(nameof(Person.Status))!;
        Assert.Equal(typeof(string), statusProp.GetProviderClrType());

        var genderProp = personType.FindProperty(nameof(Person.Gender))!;
        Assert.Equal(typeof(string), genderProp.GetProviderClrType());
    }

    [Fact]
    public void NameBTreeIndex_ShouldExist()
    {
        var model = BuildModel();
        var personType = model.FindEntityType(typeof(Person))!;

        var nameIndex = Assert.Single(personType.GetIndexes(), i =>
            i.Properties.Count == 2 &&
            i.Properties[0].Name == nameof(Person.LastName) &&
            i.Properties[1].Name == nameof(Person.FirstName));
        Assert.False(nameIndex.IsUnique);
    }
}
