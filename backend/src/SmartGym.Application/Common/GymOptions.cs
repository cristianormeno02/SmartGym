using System.ComponentModel.DataAnnotations;

namespace SmartGym.Application.Common;

public class GymOptions
{
    public const string SectionName = "Gym";

    [Required]
    public string TimeZoneId { get; set; } = "America/Argentina/San_Juan";
}
