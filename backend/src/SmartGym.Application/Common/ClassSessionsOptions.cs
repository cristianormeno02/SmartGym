using System;
using System.ComponentModel.DataAnnotations;

namespace SmartGym.Application.Common;

public class ClassSessionsOptions
{
    public const string SectionName = "ClassSessions";

    [Range(1, 180)]
    public int WindowDays { get; set; } = 60;

    public TimeSpan GenerationInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan? AutoOpenLeadTime { get; set; } = TimeSpan.FromDays(7);
}
