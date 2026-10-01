using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Identity;

public class Person : BaseEntity
{
    public const string MissingLastNamePlaceholder = "Sin apellido";

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string SearchName { get; private set; } = string.Empty;

    public PersonStatus Status { get; private set; } = PersonStatus.Active;
    public string? StatusReason { get; private set; }
    public DateTime? StatusChangedAtUtc { get; private set; }
    public Guid? StatusChangedByUserId { get; private set; }

    public IdentificationDocument? Document { get; private set; }
    public DateTime? BirthDate { get; private set; }
    public Gender? Gender { get; private set; }
    public string? Email { get; private set; }
    public string? PrimaryPhone { get; private set; }
    public string? SecondaryPhone { get; private set; }
    public Address? Address { get; private set; }
    public ProfileImage? ProfileImage { get; private set; }
    public string? ExternalAvatarUrl { get; set; }
    public EmergencyContact? EmergencyContact { get; private set; }

    // Concurrency token (PostgreSQL xmin)
    public uint Version { get; set; }

    // Certificados médicos (se conservan)
    public string? MedicalCertificateUrl { get; set; }
    public DateTime? MedicalCertificateExpiration { get; set; }
    public bool IsMedicalCertificateValid =>
        MedicalCertificateExpiration.HasValue && MedicalCertificateExpiration.Value >= DateTime.UtcNow.Date;

    // Relaciones
    public User? User { get; set; }
    public ICollection<PersonRole> PersonRoles { get; set; } = new List<PersonRole>();

    // Backward compatibility helpers
    public string? PhoneNumber
    {
        get => PrimaryPhone;
        set => PrimaryPhone = value?.Trim();
    }

    public string? PhotoUrl
    {
        get => ExternalAvatarUrl;
        set => ExternalAvatarUrl = value?.Trim();
    }

    public string? Dni
    {
        get => Document?.Type == DocumentType.Dni ? Document.Number : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                SetDocument(IdentificationDocument.Create(DocumentType.Dni, value));
            }
        }
    }

    public string? EmergencyContactName => EmergencyContact?.Name;
    public string? EmergencyContactPhone => EmergencyContact?.Phone;
    public string? EmergencyContactRelationship => EmergencyContact?.Relationship;
    public string FullName => $"{FirstName} {LastName}".Trim();

    // EF Core constructor
    public Person() { }

    public static Person Create(
        string firstName,
        string lastName,
        string? email = null,
        DateTime? birthDate = null,
        Gender? gender = null,
        IdentificationDocument? document = null,
        string? primaryPhone = null,
        string? secondaryPhone = null,
        Address? address = null,
        EmergencyContact? emergencyContact = null,
        string? externalAvatarUrl = null)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));

        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Status = PersonStatus.Active,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            BirthDate = birthDate,
            Gender = gender,
            Document = document,
            PrimaryPhone = primaryPhone?.Trim(),
            SecondaryPhone = secondaryPhone?.Trim(),
            Address = address,
            EmergencyContact = emergencyContact,
            ExternalAvatarUrl = externalAvatarUrl?.Trim()
        };

        person.SetEmail(email);
        person.RecalculateSearchName();

        if (person.IsUnderage() && !person.HasValidEmergencyContact())
        {
            throw new InvalidOperationException("Minors require a complete emergency contact.");
        }

        return person;
    }

    public void UpdatePersonalData(
        string firstName,
        string lastName,
        Gender? gender,
        DateTime? birthDate,
        EmergencyContact? emergencyContact = null)
    {
        EnsureNotDeceased();
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Gender = gender;
        BirthDate = birthDate;

        if (emergencyContact != null)
        {
            EmergencyContact = emergencyContact;
        }

        if (IsUnderage() && !HasValidEmergencyContact())
        {
            throw new InvalidOperationException("Minors require a complete emergency contact.");
        }

        RecalculateSearchName();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateContact(
        string? email,
        string? primaryPhone,
        string? secondaryPhone,
        Address? address,
        EmergencyContact? emergencyContact = null)
    {
        EnsureNotDeceased();

        SetEmail(email);
        PrimaryPhone = primaryPhone?.Trim();
        SecondaryPhone = secondaryPhone?.Trim();
        Address = address;

        if (emergencyContact != null)
        {
            EmergencyContact = emergencyContact;
            if (IsUnderage() && !HasValidEmergencyContact())
            {
                throw new InvalidOperationException("Minors require a complete emergency contact.");
            }
        }

        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetDocument(IdentificationDocument? document)
    {
        EnsureNotDeceased();
        Document = document;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetProfileImage(ProfileImage profileImage)
    {
        EnsureNotDeceased();
        ProfileImage = profileImage ?? throw new ArgumentNullException(nameof(profileImage));
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ClearProfileImage()
    {
        EnsureNotDeceased();
        ProfileImage = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeStatus(PersonStatus newStatus, string? reason, Guid? changedByUserId, bool isAdmin)
    {
        if (newStatus == Status)
        {
            throw new InvalidOperationException("Cannot change status to the current status.");
        }

        var trimmedReason = reason?.Trim();

        if (Status == PersonStatus.Deceased)
        {
            if (newStatus != PersonStatus.Active)
            {
                throw new InvalidOperationException("Deceased person can only be reverted to Active.");
            }

            if (!isAdmin)
            {
                throw new UnauthorizedAccessException("Only administrators can revert deceased status.");
            }

            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                throw new ArgumentException("Reason is mandatory when reverting deceased status.", nameof(reason));
            }
        }
        else if (newStatus == PersonStatus.Deceased)
        {
            if (!isAdmin)
            {
                throw new UnauthorizedAccessException("Only administrators can mark a person as deceased.");
            }

            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                throw new ArgumentException("Reason is mandatory when marking a person as deceased.", nameof(reason));
            }
        }
        else if (newStatus == PersonStatus.Blocked)
        {
            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                throw new ArgumentException("Reason is mandatory when blocking a person.", nameof(reason));
            }
        }

        Status = newStatus;
        StatusReason = string.IsNullOrWhiteSpace(trimmedReason) ? null : trimmedReason;
        StatusChangedAtUtc = DateTime.UtcNow;
        StatusChangedByUserId = changedByUserId;
        IsActive = (newStatus == PersonStatus.Active);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateMedicalCertificate(string url, DateTime expirationDate)
    {
        EnsureNotDeceased();
        MedicalCertificateUrl = url;
        MedicalCertificateExpiration = expirationDate;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool IsUnderage(DateTime? onDate = null)
    {
        if (!BirthDate.HasValue) return false;
        var date = onDate ?? DateTime.UtcNow;
        var age = date.Year - BirthDate.Value.Year;
        if (BirthDate.Value > date.AddYears(-age)) age--;
        return age < 18;
    }

    public bool HasValidEmergencyContact()
    {
        return EmergencyContact != null && EmergencyContact.IsComplete;
    }

    public void AddRole(Role role)
    {
        EnsureNotDeceased();
        if (!PersonRoles.Any(pr => pr.RoleId == role.Id && pr.IsActive))
        {
            PersonRoles.Add(new PersonRole
            {
                PersonId = Id,
                Person = this,
                RoleId = role.Id,
                Role = role,
                AssignedAtUtc = DateTime.UtcNow,
                IsActive = true
            });
        }
    }

    public void RemoveRole(int roleId)
    {
        EnsureNotDeceased();
        var existing = PersonRoles.FirstOrDefault(pr => pr.RoleId == roleId && pr.IsActive);
        if (existing != null)
        {
            existing.IsActive = false;
        }
    }

    public bool HasRole(RoleType roleType)
    {
        return PersonRoles.Any(pr => pr.RoleId == (int)roleType && pr.IsActive);
    }

    public void SetUser(User user)
    {
        EnsureNotDeceased();
        User = user;
        user.PersonId = Id;
        user.Person = this;
    }

    private void EnsureNotDeceased()
    {
        if (Status == PersonStatus.Deceased)
        {
            throw new InvalidOperationException("Cannot modify a deceased person.");
        }
    }

    private void SetEmail(string? email)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private void RecalculateSearchName()
    {
        SearchName = TextNormalizer.NormalizeForSearch($"{FirstName} {LastName}");
    }

    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name cannot be empty.", paramName);
        }
    }
}
