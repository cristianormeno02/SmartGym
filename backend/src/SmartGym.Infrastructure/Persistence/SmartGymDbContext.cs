using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;

using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;

namespace SmartGym.Infrastructure.Persistence;

public class SmartGymDbContext : DbContext, ISmartGymDbContext
{
    public SmartGymDbContext(DbContextOptions<SmartGymDbContext> options)
        : base(options)
    {
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<PersonRole> PersonRoles => Set<PersonRole>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<RecurringSchedule> RecurringSchedules => Set<RecurringSchedule>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<MembershipPlanActivity> MembershipPlanActivities => Set<MembershipPlanActivity>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipCreditMovement> MembershipCreditMovements => Set<MembershipCreditMovement>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyGroupMember> FamilyGroupMembers => Set<FamilyGroupMember>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<NotificationMessage> NotificationMessages => Set<NotificationMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public virtual async Task LockClassSessionForUpdateAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        // Proveedores sin bloqueo de filas (p. ej. InMemory en pruebas) no aplican.
        if (!Database.IsNpgsql())
        {
            return;
        }

        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("El bloqueo de la clase requiere una transacción activa.");
        }

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"ClassSessions\" WHERE \"Id\" = {classSessionId} FOR UPDATE",
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartGymDbContext).Assembly);
    }
}
