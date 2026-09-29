using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface IMedicalCertificateService
{
    Task<MedicalCertificateStatusDto?> GetStatusAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<MedicalCertificateStatusDto> UploadCertificateAsync(Guid personId, Stream fileStream, string fileName, string contentType, DateTime expirationDate, CancellationToken cancellationToken = default);
    Task<bool> CanPersonAttendClassAsync(Guid personId, CancellationToken cancellationToken = default);
}
