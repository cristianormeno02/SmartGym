using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Identity;

public class Person : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Dni { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? PhotoUrl { get; set; }

    // Certificados médicos
    public string? MedicalCertificateUrl { get; set; }
    public DateTime? MedicalCertificateExpiration { get; set; }
    public bool IsMedicalCertificateValid =>
        MedicalCertificateExpiration.HasValue && MedicalCertificateExpiration.Value >= DateTime.UtcNow.Date;

    public void UpdateMedicalCertificate(string url, DateTime expirationDate)
    {
        MedicalCertificateUrl = url;
        MedicalCertificateExpiration = expirationDate;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // Contacto de emergencia / tutor de menores
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

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
        return !string.IsNullOrWhiteSpace(EmergencyContactName) &&
               !string.IsNullOrWhiteSpace(EmergencyContactPhone) &&
               !string.IsNullOrWhiteSpace(EmergencyContactRelationship);
    }

    // Relaciones
    public User? User { get; set; }
    public ICollection<PersonRole> PersonRoles { get; set; } = new List<PersonRole>();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public void AddRole(Role role)
    {
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
        User = user;
        user.PersonId = Id;
        user.Person = this;
    }
}
