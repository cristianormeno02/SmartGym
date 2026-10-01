using SmartGym.Domain.Common;
using Xunit;

namespace SmartGym.Domain.UnitTests.Common;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("José  Pérez", "jose perez")]
    [InlineData("  MARÍA   DEL   CARMEN  ", "maria del carmen")]
    [InlineData("á é í ó ú ü ñ", "a e i o u u n")]
    [InlineData("Á É Í Ó Ú Ü Ñ", "a e i o u u n")]
    [InlineData("Pingüino", "pinguino")]
    [InlineData("Niño Cañete", "nino canete")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeForSearch_NormalizesDiacriticsCaseAndWhitespace(string? input, string expected)
    {
        // Act
        var result = TextNormalizer.NormalizeForSearch(input);

        // Assert
        Assert.Equal(expected, result);
    }
}
