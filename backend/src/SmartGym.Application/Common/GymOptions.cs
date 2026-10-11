using System.ComponentModel.DataAnnotations;

namespace SmartGym.Application.Common;

public class GymOptions
{
    public const string SectionName = "Gym";

    [Required]
    public string TimeZoneId { get; set; } = "America/Argentina/San_Juan";

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string DefaultCurrency { get; set; } = "ARS";

    public bool RequireValidMedicalCertificateForAccess { get; set; } = false;
}
