using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Entities.Promotions;

namespace SmartGym.Application.Modules.Promotions.Services;

public class FamilyGroupService : IFamilyGroupService
{
    private readonly ISmartGymDbContext _dbContext;

    public FamilyGroupService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<FamilyGroupDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.FamilyGroups
            .Include(g => g.PrimaryContact)
            .Include(g => g.Members.Where(m => m.IsActive))
                .ThenInclude(m => m.Person)
            .AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<FamilyGroupDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.FamilyGroups
            .Include(g => g.PrimaryContact)
            .Include(g => g.Members.Where(m => m.IsActive))
                .ThenInclude(m => m.Person)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && g.IsActive, cancellationToken);

        return group == null ? null : MapToDto(group);
    }

    public async Task<FamilyGroupDto?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.FamilyGroups
            .Include(g => g.PrimaryContact)
            .Include(g => g.Members.Where(m => m.IsActive))
                .ThenInclude(m => m.Person)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.IsActive && g.Members.Any(m => m.PersonId == personId && m.IsActive), cancellationToken);

        return group == null ? null : MapToDto(group);
    }

    public async Task<FamilyGroupDto> CreateAsync(CreateFamilyGroupRequest request, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.PrimaryContactId && p.IsActive, cancellationToken);
        if (contact == null)
        {
            throw new ArgumentException("El contacto principal especificado no existe o no está activo.");
        }

        var group = new FamilyGroup
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            PrimaryContactId = request.PrimaryContactId,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        // Agregar al contacto titular como miembro
        group.AddMember(request.PrimaryContactId, request.PrimaryContactRelationship ?? "Titular");

        _dbContext.FamilyGroups.Add(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(group.Id, cancellationToken) ?? MapToDto(group);
    }

    public async Task<FamilyGroupDto?> AddMemberAsync(Guid groupId, AddFamilyMemberRequest request, CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.FamilyGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken);

        if (group == null) return null;

        var person = await _dbContext.People.FirstOrDefaultAsync(p => p.Id == request.PersonId && p.IsActive, cancellationToken);
        if (person == null)
        {
            throw new ArgumentException("La persona a incorporar no existe o no está activa.");
        }

        group.AddMember(request.PersonId, request.Relationship);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(groupId, cancellationToken);
    }

    public async Task<bool> RemoveMemberAsync(Guid groupId, Guid personId, CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.FamilyGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken);

        if (group == null) return false;

        group.RemoveMember(personId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static FamilyGroupDto MapToDto(FamilyGroup g) =>
        new(
            g.Id,
            g.Name,
            g.PrimaryContactId,
            g.PrimaryContact?.FullName ?? string.Empty,
            g.PrimaryContact?.Email ?? string.Empty,
            g.Members.Where(m => m.IsActive).Select(m => new FamilyGroupMemberDto(
                m.PersonId,
                m.Person?.FullName ?? string.Empty,
                m.Person?.Dni ?? string.Empty,
                m.Relationship,
                m.CreatedAtUtc,
                m.Person?.Document?.Type,
                m.Person?.Document?.Number ?? m.Person?.Dni
            )).ToList(),
            g.IsActive
        );
}
