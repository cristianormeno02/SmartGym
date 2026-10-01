using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.People.Services;

public interface IPeopleService
{
    Task<PagedResult<PersonSummaryDto>> SearchAsync(
        string? search,
        PersonStatus? status,
        DocumentType? documentType,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<PersonDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> UpdateAsync(Guid id, UpdatePersonRequest request, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> ChangeStatusAsync(Guid id, ChangePersonStatusRequest request, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> UploadPhotoAsync(
        Guid id,
        Stream fileStream,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task<PersonDetailDto> DeletePhotoAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> GetOwnAsync(CancellationToken cancellationToken = default);

    Task<PersonDetailDto> UpdateOwnContactAsync(UpdateOwnContactRequest request, CancellationToken cancellationToken = default);

    Task<PersonDetailDto> UploadOwnPhotoAsync(
        Stream fileStream,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task<PersonDetailDto> DeleteOwnPhotoAsync(CancellationToken cancellationToken = default);
}
