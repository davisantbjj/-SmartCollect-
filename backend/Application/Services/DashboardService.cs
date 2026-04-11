namespace SmartCollect.Application.Services;

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

    private static (DateTime Start, DateTime End) ResolveRange(DateTime? startDate, DateTime? endDate)
    {
        var end = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        if (start > end)
            (start, end) = (end, start);

        return (start, end);
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
                (t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue)
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

    public async Task<SendsPerDayResponse> GetSendsPerDayAsync(Guid? tenantId)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-14);

        var dispatches = await DispatchesFor(tenantId)
            .Where(d => d.Status >= DispatchStatus.Sent)
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
        var dispatches = await DispatchesFor(tenantId)
            .Where(d => d.ScheduledFor >= start && d.ScheduledFor <= end)
            .ToListAsync();

        var email = dispatches.Where(d => d.Channel == CollectionChannel.Email).ToList();
        var wa = dispatches.Where(d => d.Channel == CollectionChannel.WhatsApp).ToList();
        var both = dispatches.Where(d => d.Channel == CollectionChannel.Both).ToList();

        return new ChannelMetricsResponse(
            email.Count(d => d.Status >= DispatchStatus.Sent) + both.Count(d => d.Status >= DispatchStatus.Sent),
            email.Count(d => d.Status >= DispatchStatus.Delivered) + both.Count(d => d.Status >= DispatchStatus.Delivered),
            email.Count(d => d.Status >= DispatchStatus.Viewed) + both.Count(d => d.Status >= DispatchStatus.Viewed),
            wa.Count(d => d.Status >= DispatchStatus.Sent) + both.Count(d => d.Status >= DispatchStatus.Sent),
            wa.Count(d => d.Status >= DispatchStatus.Delivered) + both.Count(d => d.Status >= DispatchStatus.Delivered),
            wa.Count(d => d.Status >= DispatchStatus.Viewed) + both.Count(d => d.Status >= DispatchStatus.Viewed));
    }

    public async Task<ActivityLogResponse> GetActivityLogAsync(Guid? tenantId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (start, end) = ResolveRange(startDate, endDate);

        var query = tenantId.HasValue
            ? _db.Dispatches
                .Include(d => d.Title)
                .Include(d => d.Contact)
                .Where(d => d.Title.TenantId == tenantId.Value)
            : _db.Dispatches
                .Include(d => d.Title)
                .Include(d => d.Contact);

        query = query.Where(d => d.ScheduledFor >= start && d.ScheduledFor <= end);

        var items = await query
            .OrderByDescending(d => d.ScheduledFor)
            .Take(20)
            .Select(d => new ActivityLogItem(
                d.Id,
                d.SentAt ?? d.ScheduledFor,
                d.Channel.ToString(),
                d.Status.ToString(),
                d.Contact.Name,
                $"Cobrança ref. {d.Title.UniqueCode}"))
            .ToListAsync();

        return new ActivityLogResponse(items);
    }
}

