namespace SmartCollect.Domain.Common;

public interface ITenantScoped
{
    Guid TenantId { get; }
}
