namespace SmartGym.Domain.Entities.Identity;

public class Address
{
    public string? Street { get; private set; }
    public string? Number { get; private set; }
    public string? Floor { get; private set; }
    public string? Apartment { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }
    public string? StateProvince { get; private set; }
    public string? CountryCode { get; private set; }

    private Address() { }

    private Address(
        string? street,
        string? number,
        string? floor,
        string? apartment,
        string? postalCode,
        string? city,
        string? stateProvince,
        string? countryCode)
    {
        Street = street?.Trim();
        Number = number?.Trim();
        Floor = floor?.Trim();
        Apartment = apartment?.Trim();
        PostalCode = postalCode?.Trim();
        City = city?.Trim();
        StateProvince = stateProvince?.Trim();
        CountryCode = string.IsNullOrWhiteSpace(countryCode) ? "AR" : countryCode.Trim().ToUpperInvariant();
    }

    public static Address Create(
        string? street,
        string? number,
        string? floor,
        string? apartment,
        string? postalCode,
        string? city,
        string? stateProvince,
        string? countryCode = "AR")
    {
        if (!string.IsNullOrWhiteSpace(countryCode) &&
            (countryCode.Trim().Length != 2 || !countryCode.Trim().All(char.IsAsciiLetter)))
        {
            throw new ArgumentException("Country must be a 2-letter ISO 3166-1 code.", nameof(countryCode));
        }

        return new Address(street, number, floor, apartment, postalCode, city, stateProvince, countryCode);
    }
}
