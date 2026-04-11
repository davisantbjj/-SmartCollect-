namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Dashboard;

public interface IDashboardService
{
    // tenantId = null means aggregate all tenants (Master cross-tenant view)
    Task<DashboardSummaryResponse> GetSummaryAsync(Guid? tenantId);
    Task<StatusBreakdownResponse> GetStatusBreakdownAsync(Guid? tenantId);
    Task<FunnelDataResponse> GetFunnelAsync(Guid? tenantId);
    Task<AgingListResponse> GetAgingAsync(Guid? tenantId);
    Task<TopDefaultersResponse> GetTopDefaultersAsync(Guid? tenantId);
    Task<SendsPerDayResponse> GetSendsPerDayAsync(Guid? tenantId);
    Task<ChannelMetricsResponse> GetChannelMetricsAsync(Guid? tenantId, DateTime? startDate = null, DateTime? endDate = null);
    Task<ActivityLogResponse> GetActivityLogAsync(Guid? tenantId, DateTime? startDate = null, DateTime? endDate = null);
}

