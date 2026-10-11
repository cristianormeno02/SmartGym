using System.ComponentModel.DataAnnotations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Common;

public class ReservationsOptions
{
    public const string SectionName = "Reservations";

    [Range(0, 10080)]
    public int MinAdvanceBookingMinutes { get; set; } = 0;

    [Range(0, 365)]
    public int MaxAdvanceBookingDays { get; set; } = 30;

    [Range(0.0, 168.0)]
    public double FreeCancellationHoursBeforeStart { get; set; } = 2.0;

    public bool EnableWaitlist { get; set; } = true;

    [Range(1, 500)]
    public int MaxWaitlistSize { get; set; } = 20;

    public WaitlistPromotionMode WaitlistPromotionMode { get; set; } = WaitlistPromotionMode.Automatic;

    [Range(1, 1440)]
    public int WaitlistOfferExpirationMinutes { get; set; } = 30;

    [Range(0, 1440)]
    public int MinMinutesBeforeStartForWaitlistPromotion { get; set; } = 15;

    [Range(5, 3600)]
    public int WaitlistWorkerIntervalSeconds { get; set; } = 60;

    [Range(0, 1440)]
    public int CheckInOpensMinutesBeforeStart { get; set; } = 30;

    [Range(0, 720)]
    public int AttendanceRegistrationHoursAfterEnd { get; set; } = 24;

    [Range(1, 365)]
    public int AvailableClassesMaxRangeDays { get; set; } = 31;
}
