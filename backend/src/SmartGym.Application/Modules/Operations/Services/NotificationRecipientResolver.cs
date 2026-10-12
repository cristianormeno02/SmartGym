using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class NotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;

    public NotificationRecipientResolver(ISmartGymDbContext dbContext, IGymCalendar? gymCalendar = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new GymCalendar(Microsoft.Extensions.Options.Options.Create(new GymOptions()));
    }

    public async Task<IReadOnlyList<ResolvedNotificationRecipient>> ResolveAsync(
        Guid targetPersonId,
        NotificationCategory category,
        CancellationToken cancellationToken = default)
    {
        var target = await _dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == targetPersonId && p.Status == PersonStatus.Active, cancellationToken);

        if (target == null)
        {
            return Array.Empty<ResolvedNotificationRecipient>();
        }

        var recipients = new List<ResolvedNotificationRecipient>
        {
            new(
                RecipientPersonId: target.Id,
                FullName: target.FullName,
                Email: target.Email,
                IsGuardianCopy: false,
                SubjectPersonId: target.Id,
                SubjectFullName: target.FullName)
        };

        var nowUtc = _gymCalendar.UtcNow;
        if (target.IsUnderage(nowUtc) && category.IsTransactional())
        {
            var guardianContacts = await _dbContext.FamilyGroupMembers
                .AsNoTracking()
                .Include(m => m.FamilyGroup)
                    .ThenInclude(fg => fg.PrimaryContact)
                .Where(m =>
                    m.PersonId == target.Id &&
                    m.IsActive &&
                    m.FamilyGroup.IsActive &&
                    m.FamilyGroup.PrimaryContactId != target.Id)
                .Select(m => m.FamilyGroup.PrimaryContact)
                .ToListAsync(cancellationToken);

            var addedGuardianIds = new HashSet<Guid> { target.Id };
            foreach (var guardian in guardianContacts)
            {
                if (guardian != null &&
                    guardian.Status == PersonStatus.Active &&
                    !guardian.IsUnderage(nowUtc) &&
                    addedGuardianIds.Add(guardian.Id))
                {
                    recipients.Add(new ResolvedNotificationRecipient(
                        RecipientPersonId: guardian.Id,
                        FullName: guardian.FullName,
                        Email: guardian.Email,
                        IsGuardianCopy: true,
                        SubjectPersonId: target.Id,
                        SubjectFullName: target.FullName));
                }
            }
        }

        return recipients;
    }
}
