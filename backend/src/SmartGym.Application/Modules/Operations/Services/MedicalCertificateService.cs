using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public class MedicalCertificateService : IMedicalCertificateService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;

    public MedicalCertificateService(
        ISmartGymDbContext dbContext,
        IFileStorageService fileStorageService)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
    }

    public async Task<MedicalCertificateStatusDto?> GetStatusAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == personId && p.IsActive, cancellationToken);

        if (person == null) return null;

        return MapToDto(person);
    }

    public async Task<MedicalCertificateStatusDto> UploadCertificateAsync(
        Guid personId,
        Stream fileStream,
        string fileName,
        string contentType,
        DateTime expirationDate,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == personId && p.IsActive, cancellationToken);

        if (person == null)
        {
            throw new ArgumentException("La persona especificada no existe o no está activa.");
        }

        if (expirationDate <= DateTime.UtcNow)
        {
            throw new ArgumentException("La fecha de vencimiento del certificado médico debe ser futura.");
        }

        var uploadedUrl = await _fileStorageService.UploadFileAsync(
            fileStream,
            fileName,
            contentType,
            $"medical-certificates/{personId}",
            cancellationToken
        );

        person.UpdateMedicalCertificate(uploadedUrl, expirationDate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(person);
    }

    public async Task<bool> CanPersonAttendClassAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == personId && p.IsActive, cancellationToken);

        if (person == null) return false;

        // Regla 1: Certificado médico vigente
        if (!person.IsMedicalCertificateValid)
        {
            return false;
        }

        // Regla 2: Si es menor de edad, debe contar con contacto de emergencia
        if (person.IsUnderage() && !person.HasValidEmergencyContact())
        {
            return false;
        }

        return true;
    }

    private static MedicalCertificateStatusDto MapToDto(SmartGym.Domain.Entities.Identity.Person p) =>
        new(
            p.Id,
            p.FullName,
            !string.IsNullOrEmpty(p.MedicalCertificateUrl),
            p.MedicalCertificateUrl,
            p.MedicalCertificateExpiration,
            p.IsMedicalCertificateValid,
            p.IsUnderage(),
            p.HasValidEmergencyContact(),
            p.EmergencyContactName,
            p.EmergencyContactPhone
        );
}
