using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.People.Services;

public class PeopleService : IPeopleService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IProfileImageProcessor _profileImageProcessor;
    private readonly ILogger<PeopleService> _logger;
    private readonly IValidator<CreatePersonRequest> _createValidator;
    private readonly IValidator<UpdatePersonRequest> _updateValidator;
    private readonly IValidator<ChangePersonStatusRequest> _statusValidator;
    private readonly IValidator<UpdateOwnContactRequest> _ownContactValidator;

    public PeopleService(
        ISmartGymDbContext dbContext,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorageService,
        IProfileImageProcessor profileImageProcessor,
        ILogger<PeopleService> logger,
        IValidator<CreatePersonRequest> createValidator,
        IValidator<UpdatePersonRequest> updateValidator,
        IValidator<ChangePersonStatusRequest> statusValidator,
        IValidator<UpdateOwnContactRequest> ownContactValidator)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _fileStorageService = fileStorageService;
        _profileImageProcessor = profileImageProcessor;
        _logger = logger;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
        _ownContactValidator = ownContactValidator;
    }

    public async Task<PagedResult<PersonSummaryDto>> SearchAsync(
        string? search,
        PersonStatus? status,
        DocumentType? documentType,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.People.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (documentType.HasValue)
        {
            query = query.Where(p => p.Document != null && p.Document.Type == documentType.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedTerm = TextNormalizer.NormalizeForSearch(search);
            var words = normalizedTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var digitsOnly = string.Concat(search.Where(char.IsDigit));
            var alphanumericCount = search.Count(char.IsLetterOrDigit);
            bool isDocumentCandidate = digitsOnly.Length > 0 && alphanumericCount >= 5;

            string? genericDoc = null;
            string? dniDoc = null;

            if (isDocumentCandidate)
            {
                genericDoc = Regex.Replace(search.Trim(), "[^a-zA-Z0-9]", string.Empty).ToUpperInvariant();
                dniDoc = digitsOnly.TrimStart('0');
            }

            var predicate = BuildSearchPredicate(words, isDocumentCandidate, genericDoc, dniDoc);
            query = query.Where(predicate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var people = await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var personIds = people.Select(p => p.Id).ToList();
        var userPersonIds = await _dbContext.Users
            .AsNoTracking()
            .Where(u => personIds.Contains(u.PersonId))
            .Select(u => u.PersonId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var userPersonSet = new HashSet<Guid>(userPersonIds);

        var summaryItems = new List<PersonSummaryDto>(people.Count);
        foreach (var person in people)
        {
            var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
            summaryItems.Add(new PersonSummaryDto(
                person.Id,
                person.FirstName,
                person.LastName,
                MapDocumentDto(person.Document),
                person.Email,
                person.PrimaryPhone,
                photoUrl,
                person.Status,
                userPersonSet.Contains(person.Id)
            ));
        }

        return new PagedResult<PersonSummaryDto>(summaryItems, totalCount, pageNumber, pageSize);
    }

    public async Task<PersonDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == id, cancellationToken);
        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);

        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new SmartGym.Application.Common.Exceptions.ValidationException(
                validationResult.ToDictionary());
        }

        // Email uniqueness check
        string? normalizedEmail = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var emailExists = await _dbContext.People
                .AnyAsync(p => p.Email != null && p.Email.ToLower() == normalizedEmail, cancellationToken);

            if (emailExists)
            {
                throw new ConflictException("Ya existe una persona registrada con ese correo electrónico.");
            }
        }

        // Document uniqueness check
        IdentificationDocument? doc = null;
        if (request.DocumentType.HasValue && !string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            doc = IdentificationDocument.Create(request.DocumentType.Value, request.DocumentNumber, request.DocumentIssuingCountry);

            var docExists = await _dbContext.People.AnyAsync(p =>
                p.Document != null &&
                p.Document.Type == doc.Type &&
                p.Document.IssuingCountry == doc.IssuingCountry &&
                p.Document.NormalizedNumber == doc.NormalizedNumber,
                cancellationToken);

            if (docExists)
            {
                throw new ConflictException("Ya existe una persona registrada con ese documento.");
            }
        }

        Address? address = null;
        if (request.Address != null)
        {
            address = Address.Create(
                request.Address.Street,
                request.Address.Number,
                request.Address.Floor,
                request.Address.Apartment,
                request.Address.PostalCode,
                request.Address.City,
                request.Address.State,
                request.Address.Country ?? "AR");
        }

        EmergencyContact? emergencyContact = null;
        if (request.EmergencyContact != null &&
            (!string.IsNullOrWhiteSpace(request.EmergencyContact.Name) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Phone) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Relationship)))
        {
            emergencyContact = EmergencyContact.Create(
                request.EmergencyContact.Name,
                request.EmergencyContact.Phone,
                request.EmergencyContact.Relationship);
        }

        DateTime? birthDateTime = request.BirthDate.HasValue
            ? request.BirthDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;

        var person = Person.Create(
            request.FirstName,
            request.LastName,
            normalizedEmail,
            birthDateTime,
            request.Gender,
            doc,
            request.PrimaryPhone,
            request.SecondaryPhone,
            address,
            emergencyContact);

        _dbContext.People.Add(person);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            HandleDbUpdateException(ex);
            throw;
        }

        return MapDetailDto(person, null, false);
    }

    public async Task<PersonDetailDto> UpdateAsync(Guid id, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new SmartGym.Application.Common.Exceptions.ValidationException(
                validationResult.ToDictionary());
        }

        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden modificar los datos de una persona en estado FALLECIDA.");
        }

        // Concurrency check
        if (person.Version != request.Version)
        {
            throw new ConflictException("La ficha de la persona fue modificada concurrentemente. Por favor, recargue la información e intente nuevamente.");
        }

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == id, cancellationToken);

        // Email update restrictions
        string? newNormalizedEmail = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            newNormalizedEmail = request.Email.Trim().ToLowerInvariant();
        }

        var currentNormalizedEmail = person.Email?.Trim().ToLowerInvariant();
        if (currentNormalizedEmail != newNormalizedEmail)
        {
            if (hasUser)
            {
                throw new ConflictException("No se puede modificar el correo electrónico porque está vinculado a una cuenta de usuario.");
            }

            if (!string.IsNullOrWhiteSpace(newNormalizedEmail))
            {
                var emailExists = await _dbContext.People
                    .AnyAsync(p => p.Id != id && p.Email != null && p.Email.ToLower() == newNormalizedEmail, cancellationToken);

                if (emailExists)
                {
                    throw new ConflictException("Ya existe una persona registrada con ese correo electrónico.");
                }
            }
        }

        // Document update & uniqueness
        IdentificationDocument? doc = null;
        if (request.DocumentType.HasValue && !string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            doc = IdentificationDocument.Create(request.DocumentType.Value, request.DocumentNumber, request.DocumentIssuingCountry);

            var docExists = await _dbContext.People.AnyAsync(p =>
                p.Id != id &&
                p.Document != null &&
                p.Document.Type == doc.Type &&
                p.Document.IssuingCountry == doc.IssuingCountry &&
                p.Document.NormalizedNumber == doc.NormalizedNumber,
                cancellationToken);

            if (docExists)
            {
                throw new ConflictException("Ya existe una persona registrada con ese documento.");
            }
        }

        Address? address = null;
        if (request.Address != null)
        {
            address = Address.Create(
                request.Address.Street,
                request.Address.Number,
                request.Address.Floor,
                request.Address.Apartment,
                request.Address.PostalCode,
                request.Address.City,
                request.Address.State,
                request.Address.Country ?? "AR");
        }

        EmergencyContact? emergencyContact = null;
        if (request.EmergencyContact != null &&
            (!string.IsNullOrWhiteSpace(request.EmergencyContact.Name) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Phone) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Relationship)))
        {
            emergencyContact = EmergencyContact.Create(
                request.EmergencyContact.Name,
                request.EmergencyContact.Phone,
                request.EmergencyContact.Relationship);
        }

        DateTime? birthDateTime = request.BirthDate.HasValue
            ? request.BirthDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;

        person.UpdatePersonalData(request.FirstName, request.LastName, request.Gender, birthDateTime, emergencyContact);
        person.SetDocument(doc);
        person.UpdateContact(newNormalizedEmail, request.PrimaryPhone, request.SecondaryPhone, address, emergencyContact);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("La ficha de la persona fue modificada concurrentemente. Por favor, recargue la información e intente nuevamente.");
        }
        catch (DbUpdateException ex)
        {
            HandleDbUpdateException(ex);
            throw;
        }

        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> ChangeStatusAsync(Guid id, ChangePersonStatusRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _statusValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new SmartGym.Application.Common.Exceptions.ValidationException(
                validationResult.ToDictionary());
        }

        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        bool isManagingDeceased = request.TargetStatus == PersonStatus.Deceased || person.Status == PersonStatus.Deceased;
        bool isAdmin = _currentUserService.IsInRole(Roles.Administrator);

        if (isManagingDeceased && !isAdmin)
        {
            throw new ForbiddenException("Solo un administrador puede gestionar el estado de fallecimiento.");
        }

        var currentUserId = _currentUserService.UserId ?? Guid.Empty;

        try
        {
            person.ChangeStatus(request.TargetStatus, request.Reason, currentUserId, isAdmin);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == id, cancellationToken);
        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> UploadPhotoAsync(
        Guid id,
        Stream fileStream,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden realizar cambios en una persona en estado FALLECIDA.");
        }

        var processed = await _profileImageProcessor.ProcessProfileImageAsync(
            fileStream,
            contentType,
            contentLength,
            cancellationToken);

        var oldStorageKey = person.ProfileImage?.Key;

        var newKey = await _fileStorageService.UploadFileAsync(
            processed.Stream,
            "avatar.webp",
            processed.ContentType,
            $"avatars/{person.Id}",
            cancellationToken);

        person.SetProfileImage(ProfileImage.Create(newKey, processed.ContentType, processed.SizeInBytes));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Compensation: delete the newly uploaded object if DB save fails
            try
            {
                await _fileStorageService.DeleteFileAsync(newKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run compensation delete for {NewKey}", newKey);
            }
            throw;
        }

        // Best-effort delete of the previous image
        if (!string.IsNullOrWhiteSpace(oldStorageKey))
        {
            try
            {
                await _fileStorageService.DeleteFileAsync(oldStorageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Orphan storage object detected: Failed to delete previous avatar {OldKey} for person {PersonId}", oldStorageKey, person.Id);
            }
        }

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == id, cancellationToken);
        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> DeletePhotoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden realizar cambios en una persona en estado FALLECIDA.");
        }

        var oldStorageKey = person.ProfileImage?.Key;
        person.ClearProfileImage();

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(oldStorageKey))
        {
            try
            {
                await _fileStorageService.DeleteFileAsync(oldStorageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Orphan storage object detected: Failed to delete avatar {OldKey} for person {PersonId}", oldStorageKey, person.Id);
            }
        }

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == id, cancellationToken);
        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> GetOwnAsync(CancellationToken cancellationToken = default)
    {
        var personId = GetCurrentPersonId();
        return await GetByIdAsync(personId, cancellationToken);
    }

    public async Task<PersonDetailDto> UpdateOwnContactAsync(UpdateOwnContactRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _ownContactValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new SmartGym.Application.Common.Exceptions.ValidationException(
                validationResult.ToDictionary());
        }

        var personId = GetCurrentPersonId();
        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person == null)
        {
            throw new NotFoundException("Persona no encontrada.");
        }

        if (person.Status == PersonStatus.Deceased)
        {
            throw new ConflictException("No se pueden realizar cambios en una persona en estado FALLECIDA.");
        }

        Address? address = null;
        if (request.Address != null)
        {
            address = Address.Create(
                request.Address.Street,
                request.Address.Number,
                request.Address.Floor,
                request.Address.Apartment,
                request.Address.PostalCode,
                request.Address.City,
                request.Address.State,
                request.Address.Country ?? "AR");
        }

        EmergencyContact? emergencyContact = null;
        if (request.EmergencyContact != null &&
            (!string.IsNullOrWhiteSpace(request.EmergencyContact.Name) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Phone) ||
             !string.IsNullOrWhiteSpace(request.EmergencyContact.Relationship)))
        {
            emergencyContact = EmergencyContact.Create(
                request.EmergencyContact.Name,
                request.EmergencyContact.Phone,
                request.EmergencyContact.Relationship);
        }

        // Only updates phone, address, and emergency contact. Protected fields remain unchanged.
        person.UpdateContact(person.Email, request.PrimaryPhone, request.SecondaryPhone, address, emergencyContact);

        await _dbContext.SaveChangesAsync(cancellationToken);

        bool hasUser = await _dbContext.Users.AnyAsync(u => u.PersonId == personId, cancellationToken);
        var photoUrl = await ResolvePhotoUrlAsync(person, cancellationToken);
        return MapDetailDto(person, photoUrl, hasUser);
    }

    public async Task<PersonDetailDto> UploadOwnPhotoAsync(
        Stream fileStream,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var personId = GetCurrentPersonId();
        return await UploadPhotoAsync(personId, fileStream, contentType, contentLength, cancellationToken);
    }

    public async Task<PersonDetailDto> DeleteOwnPhotoAsync(CancellationToken cancellationToken = default)
    {
        var personId = GetCurrentPersonId();
        return await DeletePhotoAsync(personId, cancellationToken);
    }

    private Guid GetCurrentPersonId()
    {
        if (!_currentUserService.PersonId.HasValue || _currentUserService.PersonId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("El usuario no tiene una persona asociada.");
        }

        return _currentUserService.PersonId.Value;
    }

    private async Task<string?> ResolvePhotoUrlAsync(Person person, CancellationToken cancellationToken)
    {
        if (person.ProfileImage != null && !string.IsNullOrWhiteSpace(person.ProfileImage.Key))
        {
            return await _fileStorageService.GetAccessUrlAsync(person.ProfileImage.Key, TimeSpan.FromMinutes(15), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(person.ExternalAvatarUrl))
        {
            return person.ExternalAvatarUrl;
        }

        return null;
    }

    private static IdentificationDocumentDto? MapDocumentDto(IdentificationDocument? doc)
    {
        if (doc == null) return null;
        return new IdentificationDocumentDto(doc.Type, doc.Number, doc.IssuingCountry, doc.NormalizedNumber);
    }

    private static PersonDetailDto MapDetailDto(Person person, string? photoUrl, bool hasUser)
    {
        AddressDto? addressDto = null;
        if (person.Address != null)
        {
            addressDto = new AddressDto(
                person.Address.Street,
                person.Address.Number,
                person.Address.Floor,
                person.Address.Apartment,
                person.Address.City,
                person.Address.StateProvince,
                person.Address.PostalCode,
                person.Address.CountryCode);
        }

        EmergencyContactDto? ecDto = null;
        if (person.EmergencyContact != null)
        {
            ecDto = new EmergencyContactDto(
                person.EmergencyContact.Name ?? string.Empty,
                person.EmergencyContact.Phone ?? string.Empty,
                person.EmergencyContact.Relationship ?? string.Empty);
        }

        return new PersonDetailDto(
            person.Id,
            person.FirstName,
            person.LastName,
            MapDocumentDto(person.Document),
            person.BirthDate.HasValue ? DateOnly.FromDateTime(person.BirthDate.Value) : null,
            person.Gender,
            person.Email,
            person.PrimaryPhone,
            person.SecondaryPhone,
            addressDto,
            ecDto,
            photoUrl,
            person.Status,
            person.StatusReason,
            person.StatusChangedAtUtc,
            person.StatusChangedByUserId,
            person.CreatedAtUtc,
            person.Version,
            hasUser
        );
    }

    private static Expression<Func<Person, bool>> BuildSearchPredicate(
        string[] words,
        bool isDoc,
        string? genericDoc,
        string? dniDoc)
    {
        var parameter = Expression.Parameter(typeof(Person), "p");
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var searchNameProp = Expression.Property(parameter, nameof(Person.SearchName));

        Expression? nameCondition = null;
        foreach (var word in words)
        {
            var wordConst = Expression.Constant(word, typeof(string));
            var callContains = Expression.Call(searchNameProp, containsMethod, wordConst);

            nameCondition = nameCondition == null
                ? callContains
                : Expression.AndAlso(nameCondition, callContains);
        }

        if (!isDoc)
        {
            return Expression.Lambda<Func<Person, bool>>(nameCondition ?? Expression.Constant(true), parameter);
        }

        var docProp = Expression.Property(parameter, nameof(Person.Document));
        var docNotNull = Expression.NotEqual(docProp, Expression.Constant(null, typeof(IdentificationDocument)));
        var normNumberProp = Expression.Property(docProp, nameof(IdentificationDocument.NormalizedNumber));

        Expression? docMatch = null;
        if (!string.IsNullOrEmpty(genericDoc))
        {
            var genericConst = Expression.Constant(genericDoc, typeof(string));
            docMatch = Expression.Equal(normNumberProp, genericConst);
        }

        if (!string.IsNullOrEmpty(dniDoc))
        {
            var dniConst = Expression.Constant(dniDoc, typeof(string));
            var dniMatch = Expression.Equal(normNumberProp, dniConst);
            docMatch = docMatch == null ? dniMatch : Expression.OrElse(docMatch, dniMatch);
        }

        var fullDocCondition = Expression.AndAlso(docNotNull, docMatch ?? Expression.Constant(false));

        var combined = nameCondition != null
            ? Expression.OrElse(nameCondition, fullDocCondition)
            : fullDocCondition;

        return Expression.Lambda<Func<Person, bool>>(combined, parameter);
    }

    private static void HandleDbUpdateException(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        if (message.Contains("IX_People_Document_Unique", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("23505", StringComparison.OrdinalIgnoreCase) && message.Contains("Document", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Ya existe una persona registrada con ese documento.", ex);
        }

        if (message.Contains("IX_People_Email_Unique", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("23505", StringComparison.OrdinalIgnoreCase) && message.Contains("Email", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Ya existe una persona registrada con ese correo electrónico.", ex);
        }
    }
}
