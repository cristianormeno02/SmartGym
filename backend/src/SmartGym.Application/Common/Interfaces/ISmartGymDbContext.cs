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
    DbSet<ActivityMedia> ActivityMedias { get; }
    DbSet<RecurringSchedule> RecurringSchedules { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<ClassSessionStaffChange> ClassSessionStaffChanges { get; }
    DbSet<MembershipPlan> MembershipPlans { get; }
    DbSet<MembershipPlanActivity> MembershipPlanActivities { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<MembershipAllowedActivity> MembershipAllowedActivities { get; }
    DbSet<MembershipStatusChange> MembershipStatusChanges { get; }
    DbSet<MembershipSuspension> MembershipSuspensions { get; }
    DbSet<MembershipCreditMovement> MembershipCreditMovements { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<ReservationStatusChange> ReservationStatusChanges { get; }
    DbSet<FamilyGroup> FamilyGroups { get; }
    DbSet<FamilyGroupMember> FamilyGroupMembers { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<PromotionRedemption> PromotionRedemptions { get; }
    DbSet<SmartGym.Domain.Entities.Operations.Payment> Payments { get; }
    DbSet<SmartGym.Domain.Entities.Operations.PaymentRefund> PaymentRefunds { get; }
    DbSet<SmartGym.Domain.Entities.Operations.NotificationMessage> NotificationMessages { get; }
    DbSet<SmartGym.Domain.Entities.Operations.NotificationDelivery> NotificationDeliveries { get; }
    DbSet<SmartGym.Domain.Entities.Operations.NotificationPreference> NotificationPreferences { get; }
    DbSet<SmartGym.Domain.Entities.Operations.NotificationTemplate> NotificationTemplates { get; }
    DbSet<SmartGym.Domain.Entities.Operations.AuditLog> AuditLogs { get; }

    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Bloquea la fila de la clase hasta el fin de la transacción actual, serializando las operaciones
    /// concurrentes que modifican su cupo. Debe invocarse dentro de una transacción y antes de leer la clase.
    /// </summary>
    Task LockClassSessionForUpdateAsync(Guid classSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloquea la fila de la persona hasta el fin de la transacción actual para serializar contrataciones
    /// o renovaciones concurrentes del mismo alumno.
    /// </summary>
    Task LockPersonForUpdateAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloquea la fila de la membresía hasta el fin de la transacción actual para serializar cambios de estado
    /// o consumos concurrentes.
    /// </summary>
    Task LockMembershipForUpdateAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloquea todas las membresías del alumno hasta el fin de la transacción actual. Debe invocarse antes de
    /// leerlas cuando se va a elegir una y consumir su saldo, para decidir sobre datos actualizados.
    /// </summary>
    Task LockStudentMembershipsForUpdateAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloquea la fila de la promoción hasta el fin de la transacción actual para serializar los canjes y
    /// anulaciones que verifican y actualizan sus topes de uso. Es el último bloqueo del orden común
    /// (clases, membresías, promoción): no debe tomarse ningún otro después.
    /// </summary>
    Task LockPromotionForUpdateAsync(Guid promotionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bloquea la fila del pago hasta el fin de la transacción actual para serializar su confirmación, rechazo,
    /// cancelación, aplicación y devoluciones. Se toma después de la persona, la clase y la membresía, y el estado
    /// del pago debe leerse después de bloquearlo. Las operaciones de pagos no toman el bloqueo de promoción.
    /// </summary>
    Task LockPaymentForUpdateAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Difiere las exclusiones de solapamiento de sala e instructor hasta el fin de la transacción actual.
    /// Necesario cuando un mismo guardado inserta una clase y libera el horario de otra (reprogramación).
    /// </summary>
    Task DeferClassSessionOverlapConstraintsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica de inmediato las restricciones diferidas, para que una violación surja antes del COMMIT.
    /// </summary>
    Task EnforceDeferredConstraintsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclama un lote de entregas de notificaciones pendientes listas para envío utilizando FOR UPDATE SKIP LOCKED
    /// en PostgreSQL y marcándolas en estado Processing de forma atómica.
    /// </summary>
    Task<List<SmartGym.Domain.Entities.Operations.NotificationDelivery>> ClaimPendingNotificationDeliveriesAsync(
        int batchSize,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
