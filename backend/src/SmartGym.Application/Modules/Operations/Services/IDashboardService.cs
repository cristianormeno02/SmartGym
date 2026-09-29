using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface IDashboardService
{
    Task<DashboardMetricsDto> GetDashboardMetricsAsync(CancellationToken cancellationToken = default);
}
