using Microsoft.EntityFrameworkCore;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;

namespace SmartGym.Application.Common.Interfaces;

public interface ISmartGymDbContext
{
    DbSet<Person> People { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<PersonRole> PersonRoles { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Activity> Activities { get; }
    DbSet<RecurringSchedule> RecurringSchedules { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<MembershipPlan> MembershipPlans { get; }
    DbSet<MembershipPlanActivity> MembershipPlanActivities { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<MembershipCreditMovement> MembershipCreditMovements { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<FamilyGroup> FamilyGroups { get; }
    DbSet<FamilyGroupMember> FamilyGroupMembers { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<PromotionRedemption> PromotionRedemptions { get; }
    DbSet<SmartGym.Domain.Entities.Operations.Payment> Payments { get; }
    DbSet<SmartGym.Domain.Entities.Operations.NotificationMessage> NotificationMessages { get; }
    DbSet<SmartGym.Domain.Entities.Operations.AuditLog> AuditLogs { get; }

    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
