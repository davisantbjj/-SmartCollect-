namespace SmartCollect.Application.Interfaces;

public interface IDispatchExecutionGuard
{
    IDisposable BlockTenant(Guid tenantId);
    bool IsTenantBlocked(Guid tenantId);
}
