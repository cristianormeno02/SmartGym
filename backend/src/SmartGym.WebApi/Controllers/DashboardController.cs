using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IAuditService _auditService;

    public DashboardController(
        IDashboardService dashboardService,
        IAuditService auditService)
    {
        _dashboardService = dashboardService;
        _auditService = auditService;
    }

    [HttpGet("metrics")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        var metrics = await _dashboardService.GetDashboardMetricsAsync(cancellationToken);
        return Ok(metrics);
    }

    [HttpGet("audit-logs")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int count = 50, CancellationToken cancellationToken = default)
    {
        var logs = await _auditService.GetRecentLogsAsync(count, cancellationToken);
        return Ok(logs);
    }
}
