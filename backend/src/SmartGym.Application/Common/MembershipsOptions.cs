using System;
using System.ComponentModel.DataAnnotations;

namespace SmartGym.Application.Common;

public class MembershipsOptions
{
    public const string SectionName = "Memberships";

    public TimeSpan ExpirationCheckInterval { get; set; } = TimeSpan.FromHours(1);

    [Range(0, 90)]
    public int ExpiringWarningDays { get; set; } = 7;

    [Range(1, 365)]
    public int PendingGraceDays { get; set; } = 7;

    [Range(1, 365)]
    public int MaxSuspensionDays { get; set; } = 90;
}
