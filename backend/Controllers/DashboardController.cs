namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Admin,Worker,Master")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;
    public DashboardController(IDashboardService service) => _service = service;

    /// <summary>
    /// Resolves the effective tenant ID.
    /// Master users can pass ?tenantId= to view a specific tenant's data.
    /// Admin/Worker always see their own tenant.
    /// </summary>
    private Guid? ResolveEffectiveTenantId(Guid? tenantIdParam = null)
        => TenantContextResolver.ResolveDashboardTenant(User, tenantIdParam);

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetSummaryAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("status-breakdown")]
    public async Task<IActionResult> StatusBreakdown([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetStatusBreakdownAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("funnel")]
    public async Task<IActionResult> Funnel([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetFunnelAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("aging")]
    public async Task<IActionResult> Aging([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetAgingAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("top-defaulters")]
    public async Task<IActionResult> TopDefaulters([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetTopDefaultersAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("sends-per-day")]
    public async Task<IActionResult> SendsPerDay([FromQuery] Guid? tenantId = null)
        => Ok(await _service.GetSendsPerDayAsync(ResolveEffectiveTenantId(tenantId)));

    [HttpGet("channel-metrics")]
    public async Task<IActionResult> ChannelMetrics(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
        => Ok(await _service.GetChannelMetricsAsync(ResolveEffectiveTenantId(tenantId), startDate, endDate));

    [HttpGet("activity-log")]
    public async Task<IActionResult> ActivityLog(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
        => Ok(await _service.GetActivityLogAsync(ResolveEffectiveTenantId(tenantId), startDate, endDate));
}

