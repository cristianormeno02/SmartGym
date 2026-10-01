using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Identity;

public class IdentificationDocument
{
    public DocumentType Type { get; private set; }
    public string IssuingCountry { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public string NormalizedNumber { get; private set; } = string.Empty;

    // EF Core constructor
    private IdentificationDocument() { }

    private IdentificationDocument(DocumentType type, string issuingCountry, string number, string normalizedNumber)
    {
        Type = type;
        IssuingCountry = issuingCountry;
        Number = number;
        NormalizedNumber = normalizedNumber;
    }

    public static IdentificationDocument Create(DocumentType type, string number, string? issuingCountry = null)
    {
        var (normalized, country) = DocumentNormalizer.Normalize(type, number, issuingCountry);
        return new IdentificationDocument(type, country, number.Trim(), normalized);
    }
}
