using System.Text.RegularExpressions;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Common;

public static partial class DocumentNormalizer
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnlyRegex();

    [GeneratedRegex(@"^[a-zA-Z]{2}$")]
    private static partial Regex TwoLetterCountryRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();

    public static (string NormalizedNumber, string IssuingCountry) Normalize(
        DocumentType type,
        string number,
        string? issuingCountry = null)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Document number cannot be empty.", nameof(number));
        }

        var trimmedNumber = number.Trim();

        if (type == DocumentType.Dni)
        {
            // Allowed characters for DNI input: digits, '.', '-', ' '
            foreach (var ch in trimmedNumber)
            {
                if (!char.IsDigit(ch) && ch != '.' && ch != '-' && ch != ' ')
                {
                    throw new ArgumentException("DNI cannot contain letters or unsupported characters.", nameof(number));
                }
            }

            var digitsOnly = string.Concat(trimmedNumber.Where(char.IsDigit));
            var withoutLeadingZeros = digitsOnly.TrimStart('0');

            if (string.IsNullOrEmpty(withoutLeadingZeros))
            {
                throw new ArgumentException("DNI cannot be zero or empty.", nameof(number));
            }

            if (withoutLeadingZeros.Length is < 1 or > 9)
            {
                throw new ArgumentException("DNI must be between 1 and 9 digits.", nameof(number));
            }

            return (withoutLeadingZeros, "AR");
        }

        // For Passport, IdentityCard, Other
        if (string.IsNullOrWhiteSpace(issuingCountry))
        {
            throw new ArgumentException("Issuing country is mandatory for non-DNI documents.", nameof(issuingCountry));
        }

        var countryTrimmed = issuingCountry.Trim().ToUpperInvariant();
        if (!TwoLetterCountryRegex().IsMatch(countryTrimmed))
        {
            throw new ArgumentException("Issuing country must be a valid 2-letter ISO code.", nameof(issuingCountry));
        }

        var cleaned = NonAlphaNumericRegex().Replace(trimmedNumber, string.Empty).ToUpperInvariant();

        if (cleaned.Length is < 3 or > 30)
        {
            throw new ArgumentException("Document number must be between 3 and 30 characters after normalization.", nameof(number));
        }

        return (cleaned, countryTrimmed);
    }

    public static bool TryNormalize(
        DocumentType type,
        string number,
        string? issuingCountry,
        out (string NormalizedNumber, string IssuingCountry) result,
        out string? errorMessage)
    {
        try
        {
            result = Normalize(type, number, issuingCountry);
            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            result = (string.Empty, string.Empty);
            errorMessage = ex.Message;
            return false;
        }
    }
}
