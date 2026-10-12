using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationRecipientAndPreferenceTests
{
    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options);
    }

    private static IGymCalendar CreateCalendar() =>
        new GymCalendar(Options.Create(new GymOptions()));

    [Fact]
    public async Task ResolveAsync_MinorInActiveFamilyGroup_AddsAdultPrimaryContactForTransactionalCategory()
    {
        await using var db = CreateDbContext();
        var guardian = Person.Create(
            firstName: "Roberto",
            lastName: "Gomez",
            email: "roberto.guardian@test.com",
            birthDate: new DateTime(1982, 5, 10, 0, 0, 0, DateTimeKind.Utc));

        var minor = Person.Create(
            firstName: "Mateo",
            lastName: "Gomez",
            email: "mateo.minor@test.com",
            birthDate: DateTime.UtcNow.Date.AddYears(-14),
            emergencyContact: EmergencyContact.Create("Roberto Gomez", "1144556677", "Padre"));

        db.People.AddRange(guardian, minor);

        var familyGroup = new FamilyGroup
        {
            Id = Guid.NewGuid(),
            Name = "Familia Gomez",
            PrimaryContactId = guardian.Id,
            PrimaryContact = guardian,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        familyGroup.AddMember(minor.Id, "Hijo");
        db.FamilyGroups.Add(familyGroup);
        await db.SaveChangesAsync();

        var resolver = new NotificationRecipientResolver(db, CreateCalendar());
        var recipients = await resolver.ResolveAsync(minor.Id, NotificationCategory.Reservations);

        Assert.Equal(2, recipients.Count);

        var primaryRecipient = Assert.Single(recipients, r => !r.IsGuardianCopy);
        Assert.Equal(minor.Id, primaryRecipient.RecipientPersonId);
        Assert.Equal(minor.Id, primaryRecipient.SubjectPersonId);

        var guardianRecipient = Assert.Single(recipients, r => r.IsGuardianCopy);
        Assert.Equal(guardian.Id, guardianRecipient.RecipientPersonId);
        Assert.Equal("roberto.guardian@test.com", guardianRecipient.Email);
        Assert.Equal(minor.Id, guardianRecipient.SubjectPersonId);
        Assert.Equal("Mateo Gomez", guardianRecipient.SubjectFullName);
    }

    [Fact]
    public async Task ResolveAsync_MinorWithoutFamilyGroup_ReturnsOnlyMinorWithoutFailing()
    {
        await using var db = CreateDbContext();
        var minor = Person.Create(
            firstName: "Sofia",
            lastName: "Lopez",
            email: null,
            birthDate: DateTime.UtcNow.Date.AddYears(-15),
            emergencyContact: EmergencyContact.Create("Laura Lopez", "1199887766", "Madre"));

        db.People.Add(minor);
        await db.SaveChangesAsync();

        var resolver = new NotificationRecipientResolver(db, CreateCalendar());
        var recipients = await resolver.ResolveAsync(minor.Id, NotificationCategory.ClassChanges);

        var single = Assert.Single(recipients);
        Assert.Equal(minor.Id, single.RecipientPersonId);
        Assert.False(single.IsGuardianCopy);
    }

    [Fact]
    public async Task GetPreferencesAsync_AppliesDefaults_TransactionalOptOutAndPromotionalEmailOptIn()
    {
        await using var db = CreateDbContext();
        var person = Person.Create("Lucia", "Fernandez", "lucia@test.com", new DateTime(1995, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(person);
        await db.SaveChangesAsync();

        var service = new NotificationService(db, CreateCalendar());
        var prefs = await service.GetPreferencesAsync(person.Id);

        var resInApp = Assert.Single(prefs, p => p.Category == NotificationCategory.Reservations && p.Channel == NotificationChannel.InApp);
        Assert.True(resInApp.IsEnabled);
        Assert.True(resInApp.IsLocked);
        Assert.True(resInApp.IsTransactional);

        var resEmail = Assert.Single(prefs, p => p.Category == NotificationCategory.Reservations && p.Channel == NotificationChannel.Email);
        Assert.True(resEmail.IsEnabled);
        Assert.False(resEmail.IsLocked);

        var promoInApp = Assert.Single(prefs, p => p.Category == NotificationCategory.Promotions && p.Channel == NotificationChannel.InApp);
        Assert.True(promoInApp.IsEnabled);
        Assert.False(promoInApp.IsLocked);
        Assert.False(promoInApp.IsTransactional);

        var promoEmail = Assert.Single(prefs, p => p.Category == NotificationCategory.Promotions && p.Channel == NotificationChannel.Email);
        Assert.False(promoEmail.IsEnabled);
        Assert.False(promoEmail.IsLocked);

        var secEmail = Assert.Single(prefs, p => p.Category == NotificationCategory.Security && p.Channel == NotificationChannel.Email);
        Assert.True(secEmail.IsEnabled);
        Assert.True(secEmail.IsLocked);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_RejectsDisablingMandatoryTransactionalInAppOrSecurity_WithLockedErrorCode()
    {
        await using var db = CreateDbContext();
        var person = Person.Create("Diego", "Paz", "diego@test.com", new DateTime(1990, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(person);
        await db.SaveChangesAsync();

        var service = new NotificationService(db, CreateCalendar());

        var ex1 = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdatePreferencesAsync(
                person.Id,
                new UpdateNotificationPreferencesRequest(
                [
                    new UpdateNotificationPreferenceEntry(NotificationCategory.Reservations, NotificationChannel.InApp, false)
                ])));
        Assert.Equal(NotificationErrorCodes.PreferenceMandatoryChannelLocked, ex1.Code);

        var ex2 = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdatePreferencesAsync(
                person.Id,
                new UpdateNotificationPreferencesRequest(
                [
                    new UpdateNotificationPreferenceEntry(NotificationCategory.Security, NotificationChannel.Email, false)
                ])));
        Assert.Equal(NotificationErrorCodes.PreferenceMandatoryChannelLocked, ex2.Code);

        // Allowing transactional Email opt-out and Promotional Email opt-in
        var updated = await service.UpdatePreferencesAsync(
            person.Id,
            new UpdateNotificationPreferencesRequest(
            [
                new UpdateNotificationPreferenceEntry(NotificationCategory.Reservations, NotificationChannel.Email, false),
                new UpdateNotificationPreferenceEntry(NotificationCategory.Promotions, NotificationChannel.Email, true)
            ]));

        Assert.False(Assert.Single(updated, p => p.Category == NotificationCategory.Reservations && p.Channel == NotificationChannel.Email).IsEnabled);
        Assert.True(Assert.Single(updated, p => p.Category == NotificationCategory.Promotions && p.Channel == NotificationChannel.Email).IsEnabled);
    }
}
