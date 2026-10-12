using System;
using System.Threading;
using System.Threading.Tasks;
using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IClassSessionGenerator
{
    Task<GenerationResultDto> GenerateAsync(DateOnly from, DateOnly to, Guid? scheduleId = null, CancellationToken cancellationToken = default);
    Task<int> AutoOpenAsync(DateTime? nowUtc = null, CancellationToken cancellationToken = default);
}
