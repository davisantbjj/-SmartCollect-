namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;

public class DispatchWindowConfigService : IDispatchWindowConfigService
{
    private const int DefaultStartMinutes = 9 * 60;
    private const int DefaultEndMinutes = 18 * 60;

    private readonly IAppDbContext _db;

    public DispatchWindowConfigService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<DispatchWindowConfigResponse> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        return new DispatchWindowConfigResponse(
            tenant.DispatchWindowEnabled,
            ResolveStoredOrDefaultTimeZone(tenant.DispatchWindowTimeZone),
            FormatMinutes(tenant.DispatchWindowStartMinutes, DefaultStartMinutes),
            FormatMinutes(tenant.DispatchWindowEndMinutes, DefaultEndMinutes),
            true);
    }

    public async Task SaveAsync(Guid tenantId, DispatchWindowConfigRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant não encontrado.");

        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZone)
            ? GetDefaultTimeZoneId()
            : request.TimeZone.Trim();

        if (!DispatchWindowTimeZoneResolver.TryResolve(timeZoneId, out _))
            throw new InvalidOperationException("Fuso horário inválido.");

        var startMinutes = ParseHourMinute(request.StartTime, "Início");
        var endMinutes = ParseHourMinute(request.EndTime, "Fim");

        if (startMinutes == endMinutes)
            throw new InvalidOperationException("Hora inicial e final não podem ser iguais.");

        tenant.DispatchWindowEnabled = request.Enabled;
        tenant.DispatchWindowTimeZone = timeZoneId;
        tenant.DispatchWindowStartMinutes = startMinutes;
        tenant.DispatchWindowEndMinutes = endMinutes;
        tenant.PauseAutomaticDispatchDuringProcessing = true;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static int ParseHourMinute(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label}: informe um horário no formato HH:mm.");

        if (!TimeOnly.TryParse(value.Trim(), out var parsed))
            throw new InvalidOperationException($"{label}: horário inválido. Use HH:mm.");

        return parsed.Hour * 60 + parsed.Minute;
    }

    private static string FormatMinutes(int value, int fallback)
    {
        var normalized = value is < 0 or >= 1440 ? fallback : value;
        var hours = normalized / 60;
        var minutes = normalized % 60;
        return $"{hours:D2}:{minutes:D2}";
    }

    private static string ResolveStoredOrDefaultTimeZone(string? stored)
    {
        if (!string.IsNullOrWhiteSpace(stored) && DispatchWindowTimeZoneResolver.TryResolve(stored, out _))
            return stored;

        return GetDefaultTimeZoneId();
    }

    private static string GetDefaultTimeZoneId()
        => TimeZoneInfo.Local.Id;
}
