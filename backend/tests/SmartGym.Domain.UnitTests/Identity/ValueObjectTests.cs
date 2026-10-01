using SmartGym.Domain.Entities.Identity;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class ValueObjectTests
{
    [Fact]
    public void Address_Create_SetsPropertiesAndUppercasesCountry()
    {
        var address = Address.Create(
            "Av. Corrientes", "1234", "5", "B", "C1043", "Buenos Aires", "CABA", "ar");

        Assert.Equal("Av. Corrientes", address.Street);
        Assert.Equal("1234", address.Number);
        Assert.Equal("5", address.Floor);
        Assert.Equal("B", address.Apartment);
        Assert.Equal("C1043", address.PostalCode);
        Assert.Equal("Buenos Aires", address.City);
        Assert.Equal("CABA", address.StateProvince);
        Assert.Equal("AR", address.CountryCode);
    }

    [Fact]
    public void ProfileImage_Create_SetsPropertiesCorrectly()
    {
        var now = DateTime.UtcNow;
        var image = ProfileImage.Create("avatars/p1/pic.webp", "image/webp", 102400, now);

        Assert.Equal("avatars/p1/pic.webp", image.Key);
        Assert.Equal("image/webp", image.ContentType);
        Assert.Equal(102400, image.SizeBytes);
        Assert.Equal(now, image.UploadedAtUtc);
    }

    [Theory]
    [InlineData("", "image/webp", 100)]
    [InlineData("key", "", 100)]
    [InlineData("key", "image/webp", 0)]
    [InlineData("key", "image/webp", -5)]
    public void ProfileImage_Create_ValidatesInvariants(string key, string contentType, long size)
    {
        Assert.Throws<ArgumentException>(() => ProfileImage.Create(key, contentType, size));
    }

    [Fact]
    public void EmergencyContact_Create_ChecksCompleteness()
    {
        var complete = EmergencyContact.Create("Ana Perez", "+5491112345678", "Madre");
        Assert.True(complete.IsComplete);
        Assert.Equal("Ana Perez", complete.Name);
        Assert.Equal("+5491112345678", complete.Phone);
        Assert.Equal("Madre", complete.Relationship);

        var incomplete = EmergencyContact.Create("Ana Perez", "", "Madre");
        Assert.False(incomplete.IsComplete);
    }
}
