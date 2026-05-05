namespace SmartCollect.Api.Security;

using System.Security.Claims;

public static class TenantContextResolver
{
    public static Guid GetTenantIdOrThrow(ClaimsPrincipal user)
    {
        var claim = user.FindFirst("TenantId")?.Value;
        if (Guid.TryParse(claim, out var tenantId)) return tenantId;
        throw new UnauthorizedAccessException("Missing tenant claim.");
    }

    public static Guid ResolveTenantOrThrow(ClaimsPrincipal user, Guid? requestedTenantId)
    {
        if (user.IsInRole("Master"))
        {
            if (requestedTenantId is null || requestedTenantId == Guid.Empty)
                throw new InvalidOperationException("Selecione uma empresa para continuar.");

            return requestedTenantId.Value;
        }

        return GetTenantIdOrThrow(user);
    }

    public static Guid? ResolveDashboardTenant(ClaimsPrincipal user, Guid? requestedTenantId)
    {
        if (user.IsInRole("Master"))
            return requestedTenantId;

        return GetTenantIdOrThrow(user);
    }
}