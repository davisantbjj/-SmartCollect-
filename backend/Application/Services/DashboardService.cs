namespace SmartCollect.Application.Services;

using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Dashboard;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class DashboardService : IDashboardService
{
    private readonly IAppDbContext _db;
    public DashboardService(IAppDbContext db) => _db = db;

    // Helper: applies tenant filter. null = all tenants (Master aggregate)
    private IQueryable<Domain.Entities.Title> TitlesFor(Guid? tenantId)
        => tenantId.HasValue
            ? _db.Titles.Where(t => t.TenantId == tenantId.Value)
            : _db.Titles;

    private IQueryable<Domain.Entities.Dispatch> DispatchesFor(Guid? tenantId)
        => tenantId.HasValue
            ? _db.Dispatches.Include(d => d.Title).Where(d => d.Title.TenantId == tenantId.Value)
            : _db.Dispatches.Include(d => d.Title);

    private IQueryable<Domain.Entities.TitleHistory> TitleHistoriesFor(Guid? tenantId)
        => tenantId.HasValue
            ? _db.TitleHistories.Where(h => h.TenantId == tenantId.Value)
            : _db.TitleHistories;

    private static (DateTime Start, DateTime End) ResolveRange(DateTime? startDate, DateTime? endDate)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        if (start > end)
            (start, end) = (end, start);

        return (start, end);
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    private static bool IsQuickManualAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        return NormalizeText(action.Trim()) == "cobranca manual rapida";
    }

    private static (int EmailCount, int WhatsAppCount) ParseQuickManualChannels(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (0, 0);

        var normalized = NormalizeText(description);
        var emailCount = CountOccurrences(normalized, "e-mail enviado") + CountOccurrences(normalized, "email enviado");
        var whatsAppCount = CountOccurrences(normalized, "whatsapp enviado");

        return (emailCount, whatsAppCount);
    }

    private static int CountOccurrences(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            return 0;

        var count = 0;
        var index = 0;

        while (true)
        {
            index = text.IndexOf(token, index, StringComparison.Ordinal);
            if (index < 0)
                break;

            count++;
            index += token.Length;
        }

        return count;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(Guid? tenantId)
    {
        var titles = TitlesFor(tenantId);

        var totalReceivable = await titles
            .Where(t => t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue)
            .SumAsync(t => t.Amount);

        var totalOverdue = await titles
            .Where(t => (t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue) && t.DueDate < DateTime.UtcNow)
            .SumAsync(t => t.Amount);

        // Renamed: TotalRecovered → TotalPaid (B-06)
        var totalPaid = await titles
            .Where(t => t.Status == TitleStatus.Paid)
            .SumAsync(t => t.Amount);

        var denominator = totalPaid + totalReceivable;
        var recoveryRate = denominator > 0 ? Math.Round(totalPaid / denominator * 100, 2) : 0;

        return new DashboardSummaryResponse(totalReceivable, totalOverdue, totalPaid, recoveryRate);
    }

    public async Task<StatusBreakdownResponse> GetStatusBreakdownAsync(Guid? tenantId)
    {
        var titles = TitlesFor(tenantId);

        var open = await titles.CountAsync(t => t.Status == TitleStatus.Open);
        var pendingData = await titles.CountAsync(t => t.Status == TitleStatus.PendingData);
        var overdue = await titles.CountAsync(t => t.Status == TitleStatus.Overdue);
        var paid = await titles.CountAsync(t => t.Status == TitleStatus.Paid);
        var cancelled = await titles.CountAsync(t => t.Status == TitleStatus.Cancelled);

        return new StatusBreakdownResponse(open, pendingData, overdue, paid, cancelled);
    }

    public async Task<FunnelDataResponse> GetFunnelAsync(Guid? tenantId)
    {
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var titles = await TitlesFor(tenantId)
            .Where(t => t.CreatedAt >= sixMonthsAgo)
            .ToListAsync();

        var items = titles
            .GroupBy(t => t.CreatedAt.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new FunnelItem(
                g.Key,
                g.Where(t => t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue).Sum(t => t.Amount),
                g.Where(t => t.DueDate < DateTime.UtcNow && t.Status != TitleStatus.Paid).Sum(t => t.Amount),
                g.Where(t => t.Status == TitleStatus.Paid).Sum(t => t.Amount)))
            .ToList();

        return new FunnelDataResponse(items);
    }

    public async Task<AgingListResponse> GetAgingAsync(Guid? tenantId)
    {
        var overdue = await TitlesFor(tenantId)
            .Where(t =>
                (t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue || t.Status == TitleStatus.PendingData)
                && t.DueDate < DateTime.UtcNow)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var buckets = new (string Range, string Color, Func<int, bool> Pred)[]
        {
            ("1-30 dias",  "#F59E0B", d => d >= 1 && d <= 30),
            ("31-60 dias", "#F97316", d => d >= 31 && d <= 60),
            ("61-90 dias", "#EF4444", d => d >= 61 && d <= 90),
            (">90 dias",   "#991B1B", d => d > 90)
        };

        var items = buckets.Select(b => new AgingItem(
            b.Range,
            overdue.Where(t => b.Pred((now - t.DueDate).Days)).Sum(t => t.Amount),
            b.Color)).ToList();

        return new AgingListResponse(items);
    }

    public async Task<TopDefaultersResponse> GetTopDefaultersAsync(Guid? tenantId)
    {
        var overdueTitles = await TitlesFor(tenantId)
            .Include(t => t.Client)
            .Where(t =>
                (t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue)
                && t.DueDate < DateTime.UtcNow)
            .ToListAsync();

        var items = overdueTitles
            .GroupBy(t => new { t.Client.LegalName, t.Client.TaxId })
            .Select(g => new DefaulterItem(
                g.Key.LegalName,
                g.Key.TaxId,
                g.Sum(t => t.Amount),
                g.Count()))
            .OrderByDescending(d => d.TotalAmount)
            .Take(5)
            .ToList();

        return new TopDefaultersResponse(items);
    }

    public async Task<CriticalMetricsResponse> GetCriticalMetricsAsync(Guid? tenantId)
    {
        var now = DateTime.UtcNow;
        var criticalCutoff = now.Date.AddDays(-10);

        // Critical: unpaid titles with overdue greater than 10 days.
        var criticalTitles = await TitlesFor(tenantId)
            .CountAsync(t =>
                (t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue || t.Status == TitleStatus.PendingData)
                && t.DueDate.Date < criticalCutoff);

        // Recovery concept: titles that became overdue (due date in past) and are now paid.
        var overdueUniverse = await TitlesFor(tenantId)
            .Where(t => t.DueDate.Date < now.Date && t.Status != TitleStatus.Cancelled)
            .ToListAsync();

        var overdueBaseTitles = overdueUniverse.Count;
        var recoveredTitles = overdueUniverse.Count(t => t.Status == TitleStatus.Paid);
        var recoveryRate = overdueBaseTitles > 0
            ? Math.Round((double)recoveredTitles / overdueBaseTitles * 100, 1)
            : 0;

        var trendStart = now.Date.AddMonths(-5);
        var trendItems = overdueUniverse
            .Where(t => t.DueDate.Date >= trendStart)
            .GroupBy(t => t.DueDate.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var baseCount = g.Count();
                var recoveredCount = g.Count(t => t.Status == TitleStatus.Paid);
                var rate = baseCount > 0
                    ? Math.Round((double)recoveredCount / baseCount * 100, 1)
                    : 0;
                return new RecoveryRatePointResponse(g.Key, baseCount, recoveredCount, rate);
            })
            .ToList();

        return new CriticalMetricsResponse(
            criticalTitles,
            overdueBaseTitles,
            recoveredTitles,
            recoveryRate,
            trendItems);
    }

    public async Task<SendsPerDayResponse> GetSendsPerDayAsync(Guid? tenantId)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-14);

        var dispatches = await DispatchesFor(tenantId)
            .Where(d => d.Status == DispatchStatus.Sent
                || d.Status == DispatchStatus.Delivered
                || d.Status == DispatchStatus.Viewed)
            .Where(d => (d.SentAt ?? d.ScheduledFor) >= start && (d.SentAt ?? d.ScheduledFor) <= end)
            .ToListAsync();

        var grouped = dispatches
            .GroupBy(d => (d.SentAt ?? d.ScheduledFor).Date)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Email = g.Count(d => d.Channel == CollectionChannel.Email || d.Channel == CollectionChannel.Both),
                    WhatsApp = g.Count(d => d.Channel == CollectionChannel.WhatsApp || d.Channel == CollectionChannel.Both)
                });

        var quickManualHistories = (await TitleHistoriesFor(tenantId)
            .Where(h => h.CreatedAt >= start && h.CreatedAt <= end)
            .ToListAsync())
            .Where(h => IsQuickManualAction(h.Action));

        foreach (var history in quickManualHistories)
        {
            var parsed = ParseQuickManualChannels(history.Description);
            if (parsed.EmailCount == 0 && parsed.WhatsAppCount == 0)
                continue;

            var day = history.CreatedAt.Date;
            if (grouped.TryGetValue(day, out var existing))
            {
                grouped[day] = new
                {
                    Email = existing.Email + parsed.EmailCount,
                    WhatsApp = existing.WhatsApp + parsed.WhatsAppCount,
                };
            }
            else
            {
                grouped[day] = new
                {
                    Email = parsed.EmailCount,
                    WhatsApp = parsed.WhatsAppCount,
                };
            }
        }

        var items = new List<SendsDayItem>();
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            grouped.TryGetValue(day, out var counts);
            items.Add(new SendsDayItem(
                day.ToString("yyyy-MM-dd"),
                counts?.Email ?? 0,
                counts?.WhatsApp ?? 0));
        }

        return new SendsPerDayResponse(items);
    }

    public async Task<ChannelMetricsResponse> GetChannelMetricsAsync(Guid? tenantId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (start, end) = ResolveRange(startDate, endDate);
        var sentStatuses = new[] { DispatchStatus.Sent, DispatchStatus.Delivered, DispatchStatus.Viewed };
        // Delivery callbacks are not yet integrated for all channels/providers.
        // Consider provider-accepted sends as delivered for operational dashboard metrics.
        var deliveredStatuses = new[] { DispatchStatus.Sent, DispatchStatus.Delivered, DispatchStatus.Viewed };
        var viewedStatuses = new[] { DispatchStatus.Viewed };

        var dispatches = await DispatchesFor(tenantId)
            .Where(d => (d.SentAt ?? d.ScheduledFor) >= start && (d.SentAt ?? d.ScheduledFor) <= end)
            .ToListAsync();

        var email = dispatches.Where(d => d.Channel == CollectionChannel.Email).ToList();
        var wa = dispatches.Where(d => d.Channel == CollectionChannel.WhatsApp).ToList();
        var both = dispatches.Where(d => d.Channel == CollectionChannel.Both).ToList();

        var quickManualHistories = (await TitleHistoriesFor(tenantId)
            .Where(h => h.CreatedAt >= start && h.CreatedAt <= end)
            .ToListAsync())
            .Where(h => IsQuickManualAction(h.Action));

        var quickEmailSent = 0;
        var quickWhatsAppSent = 0;
        var quickEmailDelivered = 0;
        var quickWhatsAppDelivered = 0;

        foreach (var history in quickManualHistories)
        {
            var parsed = ParseQuickManualChannels(history.Description);
            quickEmailSent += parsed.EmailCount;
            quickWhatsAppSent += parsed.WhatsAppCount;
            quickEmailDelivered += parsed.EmailCount;
            quickWhatsAppDelivered += parsed.WhatsAppCount;
        }

        return new ChannelMetricsResponse(
            email.Count(d => sentStatuses.Contains(d.Status)) + both.Count(d => sentStatuses.Contains(d.Status)) + quickEmailSent,
                email.Count(d => deliveredStatuses.Contains(d.Status)) + both.Count(d => deliveredStatuses.Contains(d.Status)) + quickEmailDelivered,
            email.Count(d => viewedStatuses.Contains(d.Status)) + both.Count(d => viewedStatuses.Contains(d.Status)),
            wa.Count(d => sentStatuses.Contains(d.Status)) + both.Count(d => sentStatuses.Contains(d.Status)) + quickWhatsAppSent,
                wa.Count(d => deliveredStatuses.Contains(d.Status)) + both.Count(d => deliveredStatuses.Contains(d.Status)) + quickWhatsAppDelivered,
            wa.Count(d => viewedStatuses.Contains(d.Status)) + both.Count(d => viewedStatuses.Contains(d.Status)));
    }

    public async Task<ActivityLogResponse> GetActivityLogAsync(Guid? tenantId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (start, end) = ResolveRange(startDate, endDate);

        var dispatchQuery = tenantId.HasValue
            ? _db.Dispatches
                .Include(d => d.Title)
                .Include(d => d.Contact)
                .Where(d => d.Title.TenantId == tenantId.Value)
            : _db.Dispatches
                .Include(d => d.Title)
                .Include(d => d.Contact);

        dispatchQuery = dispatchQuery
            .Where(d => (d.SentAt ?? d.ScheduledFor) >= start && (d.SentAt ?? d.ScheduledFor) <= end);

        var dispatchRows = await dispatchQuery
            .OrderByDescending(d => d.SentAt ?? d.ScheduledFor)
            .Take(30)
            .ToListAsync();

        var dispatchItems = dispatchRows
            .Select(d =>
            {
                var titleCode = string.IsNullOrWhiteSpace(d.Title?.UniqueCode) ? "sem código" : d.Title.UniqueCode;
                var recipient = string.IsNullOrWhiteSpace(d.Contact?.Name) ? "Contato não informado" : d.Contact.Name;
                var timestamp = d.Status is DispatchStatus.Cancelled or DispatchStatus.Error
                    ? d.UpdatedAt
                    : d.SentAt ?? d.ScheduledFor;
                var summary = d.Status == DispatchStatus.Pending
                    ? $"Agendado: Cobrança ref. {titleCode}"
                    : d.Status == DispatchStatus.Cancelled
                        ? $"Agendamento cancelado automaticamente: Cobrança ref. {titleCode}"
                        : $"Cobrança ref. {titleCode}";

                return new ActivityLogItem(
                    d.Id,
                    timestamp,
                    d.Channel.ToString(),
                    d.Status.ToString(),
                    recipient,
                    summary);
            })
            .ToList();

        var historyQuery = tenantId.HasValue
            ? _db.TitleHistories
                .Include(h => h.Title)
                .Where(h => h.TenantId == tenantId.Value)
            : _db.TitleHistories
                .Include(h => h.Title);

        historyQuery = historyQuery.Where(h => h.CreatedAt >= start && h.CreatedAt <= end);

        var historyRows = await historyQuery
            .OrderByDescending(h => h.CreatedAt)
            .Take(30)
            .ToListAsync();

        var historyItems = historyRows
            .Select(h => new ActivityLogItem(
                h.Id,
                h.CreatedAt,
                "System",
                "Info",
                string.IsNullOrWhiteSpace(h.Title?.UniqueCode) ? "Sistema" : h.Title.UniqueCode,
                $"{h.Action}: {h.Description}"))
            .ToList();

        var items = dispatchItems
            .Concat(historyItems)
            .OrderByDescending(i => i.Timestamp)
            .Take(30)
            .ToList();

        return new ActivityLogResponse(items);
    }
}
