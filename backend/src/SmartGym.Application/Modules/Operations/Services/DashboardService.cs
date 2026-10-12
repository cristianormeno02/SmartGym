using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class DashboardService : IDashboardService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly SmartGym.Application.Common.IGymCalendar _gymCalendar;

    public DashboardService(ISmartGymDbContext dbContext, SmartGym.Application.Common.IGymCalendar gymCalendar)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
    }

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync(CancellationToken cancellationToken = default)
    {
        var today = _gymCalendar.Today;
        var (todayStartUtc, todayEndUtc) = _gymCalendar.LocalDayRange(today, today);
        var nowUtc = _gymCalendar.UtcNow;
        var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var in7Days = today.AddDays(7);

        // Alumnos activos con membresía vigente (estado efectivo Active)
        var totalActiveStudents = await _dbContext.Memberships
            .Where(m => m.IsActive && m.Status == MembershipStatus.Active && m.StartDate <= today && today <= m.EndDate)
            .Select(m => m.StudentId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Instructores activos
        var totalActiveInstructors = await _dbContext.PersonRoles
            .Where(pr => pr.IsActive && pr.Role.Name == RoleType.Instructor.ToString())
            .Select(pr => pr.PersonId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Clases programadas hoy
        var totalClassesToday = await _dbContext.ClassSessions
            .CountAsync(cs => cs.StartsAtUtc >= todayStartUtc && cs.StartsAtUtc < todayEndUtc && cs.IsActive, cancellationToken);

        // Reservas de hoy
        var totalReservationsToday = await _dbContext.Reservations
            .CountAsync(r => r.ClassSession.StartsAtUtc >= todayStartUtc && r.ClassSession.StartsAtUtc < todayEndUtc &&
                            (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Attended),
                        cancellationToken);

        // Asistencias registradas hoy
        var totalAttendanceToday = await _dbContext.Reservations
            .CountAsync(r => r.ClassSession.StartsAtUtc >= todayStartUtc && r.ClassSession.StartsAtUtc < todayEndUtc && r.Status == ReservationStatus.Attended, cancellationToken);

        var attendanceRate = totalReservationsToday > 0
            ? Math.Round(((double)totalAttendanceToday / totalReservationsToday) * 100.0, 1)
            : 0.0;

        // Facturación del mes actual
        // Cobros confirmados en el mes menos devoluciones registradas en el mes: una devolución no altera
        // los ingresos de meses anteriores.
        var monthlyConfirmed = await _dbContext.Payments
            .Where(p => p.IsActive && p.ConfirmedAtUtc >= currentMonthStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var monthlyRefunded = await _dbContext.PaymentRefunds
            .Where(r => r.IsActive && r.Status == PaymentRefundStatus.Completed && r.CompletedAtUtc >= currentMonthStart)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
        var monthlyRevenue = monthlyConfirmed - monthlyRefunded;

        // Membresías por vencer en los próximos 7 días
        var expiringMemberships = await _dbContext.Memberships
            .CountAsync(m => m.IsActive && m.Status == MembershipStatus.Active && m.EndDate >= today && m.EndDate <= in7Days, cancellationToken);

        // Certificados médicos pendientes o vencidos
        var pendingCertificates = await _dbContext.People
            .CountAsync(p => p.IsActive &&
                            p.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Student.ToString()) &&
                            (p.MedicalCertificateExpiration == null || p.MedicalCertificateExpiration.Value < DateTime.UtcNow.Date),
                        cancellationToken);

        return new DashboardMetricsDto(
            totalActiveStudents,
            totalActiveInstructors,
            totalClassesToday,
            totalReservationsToday,
            totalAttendanceToday,
            attendanceRate,
            monthlyRevenue,
            expiringMemberships,
            pendingCertificates
        );
    }
}
