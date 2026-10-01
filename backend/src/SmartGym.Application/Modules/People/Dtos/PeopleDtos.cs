using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.People.Dtos;

public record AddressDto(
    string? Street = null,
    string? Number = null,
    string? Floor = null,
    string? Apartment = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Country = null
);

public record EmergencyContactDto(
    string Name = "",
    string Phone = "",
    string Relationship = ""
);

public record IdentificationDocumentDto(
    DocumentType Type,
    string Number,
    string IssuingCountry,
    string NumberNormalized
);

public record CreatePersonRequest(
    string FirstName,
    string LastName,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null,
    string? DocumentIssuingCountry = null,
    DateOnly? BirthDate = null,
    Gender? Gender = null,
    string? Email = null,
    string? PrimaryPhone = null,
    string? SecondaryPhone = null,
    AddressDto? Address = null,
    EmergencyContactDto? EmergencyContact = null
);

public record UpdatePersonRequest(
    string FirstName,
    string LastName,
    uint Version,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null,
    string? DocumentIssuingCountry = null,
    DateOnly? BirthDate = null,
    Gender? Gender = null,
    string? Email = null,
    string? PrimaryPhone = null,
    string? SecondaryPhone = null,
    AddressDto? Address = null,
    EmergencyContactDto? EmergencyContact = null
);

public record UpdateOwnContactRequest(
    string? PrimaryPhone = null,
    string? SecondaryPhone = null,
    AddressDto? Address = null,
    EmergencyContactDto? EmergencyContact = null
);

public record ChangePersonStatusRequest(
    PersonStatus TargetStatus,
    string? Reason = null
);

public record PersonDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    IdentificationDocumentDto? Document,
    DateOnly? BirthDate,
    Gender? Gender,
    string? Email,
    string? PrimaryPhone,
    string? SecondaryPhone,
    AddressDto? Address,
    EmergencyContactDto? EmergencyContact,
    string? PhotoUrl,
    PersonStatus Status,
    string? StatusReason,
    DateTime? StatusChangedAtUtc,
    Guid? StatusChangedByUserId,
    DateTime CreatedAtUtc,
    uint Version,
    bool HasUserAccount
);

public record PersonSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    IdentificationDocumentDto? Document,
    string? Email,
    string? PrimaryPhone,
    string? PhotoUrl,
    PersonStatus Status,
    bool HasUserAccount
);
