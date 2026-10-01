using SmartGym.Domain.Common;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class MigrationVerificationTests
{
    [Theory]
    [InlineData("José Pérez", "jose perez")]
    [InlineData("María del Carmen", "maria del carmen")]
    [InlineData("Ángel Núñez", "angel nunez")]
    [InlineData("Agustín Peña", "agustin pena")]
    [InlineData("Raúl Güemes", "raul guemes")]
    [InlineData("  Juan   Carlos  ", "juan carlos")]
    public void TextNormalizer_ProducesExpectedSearchNameForSpanishNames(string input, string expected)
    {
        var result = TextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DocumentNormalizer_DniNormalized_MatchesSqlRegexBehavior()
    {
        // SQL: ltrim(regexp_replace("Dni", '\D', '', 'g'), '0')
        var formattedDni = "0012.345-678";
        var (normalized, country) = DocumentNormalizer.Normalize(Domain.Enums.DocumentType.Dni, formattedDni);

        Assert.Equal("12345678", normalized);
        Assert.Equal("AR", country);
    }
}
