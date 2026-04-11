namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class User : Entity
{
    // Master users have no TenantId (null = cross-tenant access)
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Worker;
    public bool Active { get; set; } = true;
    public DateTime? LastLogin { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<Client> Clients { get; set; } = new List<Client>();
}
