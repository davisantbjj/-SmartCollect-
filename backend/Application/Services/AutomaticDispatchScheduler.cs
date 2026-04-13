namespace SmartCollect.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

internal static class AutomaticDispatchScheduler
{
    public static async Task<int> CancelPendingForTitlesAsync(
        IAppDbContext db,
        IReadOnlyCollection<Guid> titleIds,
        CancellationToken cancellationToken = default)
    {
        if (titleIds.Count == 0)
            return 0;

        var pending = await db.Dispatches
            .Where(d => titleIds.Contains(d.TitleId) && d.Status == DispatchStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var dispatch in pending)
            dispatch.Status = DispatchStatus.Cancelled;

        return pending.Count;
    }

    public static async Task<int> EnsureDispatchesForTitlesAsync(
        IAppDbContext db,
        Guid tenantId,
        IReadOnlyCollection<Guid> titleIds,
        CancellationToken cancellationToken = default)
    {
        if (titleIds.Count == 0)
            return 0;

        var activeRules = await db.CollectionRules
            .Include(r => r.Triggers)
            .Where(r => r.TenantId == tenantId && r.Active)
            .ToListAsync(cancellationToken);

        if (activeRules.Count == 0)
            return 0;

        var titles = await db.Titles
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .Where(t => t.TenantId == tenantId && titleIds.Contains(t.Id))
            .Where(t => t.Status == TitleStatus.Open || t.Status == TitleStatus.Overdue)
            .ToListAsync(cancellationToken);

        if (titles.Count == 0)
            return 0;

        var existingDispatchKeys = await db.Dispatches
            .Where(d => titleIds.Contains(d.TitleId))
            .Select(d => new { d.TitleId, d.ContactId, d.TriggerId, d.Channel })
            .ToListAsync(cancellationToken);

        var seenKeys = existingDispatchKeys
            .Select(x => BuildKey(x.TitleId, x.ContactId, x.TriggerId, x.Channel))
            .ToHashSet(StringComparer.Ordinal);

        var created = 0;
        var nowUtc = DateTime.UtcNow;

        foreach (var title in titles)
        {
            var recipients = ResolveRecipients(title.Client);
            if (recipients.Count == 0)
                continue;

            var activeTriggers = activeRules
                .SelectMany(rule => rule.Triggers)
                .Where(tr => tr.Active)
                .OrderBy(tr => tr.Order)
                .ToList();

            foreach (var contact in recipients)
            {
                var candidates = activeTriggers
                    .Where(trigger => ContactSupportsChannel(contact, trigger.Channel))
                    .Select(trigger => new
                    {
                        Trigger = trigger,
                        ScheduledDate = trigger.Reference == TriggerReference.DueDate
                            ? title.DueDate.AddDays(trigger.DaysOffset)
                            : title.IssueDate.AddDays(trigger.DaysOffset)
                    })
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                var overdueCandidate = candidates
                    .Where(c => c.ScheduledDate <= nowUtc)
                    .OrderByDescending(c => c.ScheduledDate)
                    .ThenBy(c => c.Trigger.Order)
                    .FirstOrDefault();

                var selectedCandidates = candidates
                    .Where(c => c.ScheduledDate > nowUtc)
                    .ToList();

                if (overdueCandidate is not null)
                    selectedCandidates.Add(overdueCandidate);

                foreach (var candidate in selectedCandidates
                             .OrderBy(c => c.ScheduledDate)
                             .ThenBy(c => c.Trigger.Order))
                {
                    var key = BuildKey(title.Id, contact.Id, candidate.Trigger.Id, candidate.Trigger.Channel);
                    if (seenKeys.Contains(key))
                        continue;

                    await db.Dispatches.AddAsync(new Dispatch
                    {
                        Id = Guid.NewGuid(),
                        TitleId = title.Id,
                        ContactId = contact.Id,
                        TriggerId = candidate.Trigger.Id,
                        Channel = candidate.Trigger.Channel,
                        Status = DispatchStatus.Pending,
                        ScheduledFor = candidate.ScheduledDate
                    }, cancellationToken);

                    seenKeys.Add(key);
                    created++;
                }
            }
        }

        return created;
    }

    private static string BuildKey(Guid titleId, Guid contactId, Guid triggerId, CollectionChannel channel)
        => $"{titleId:N}|{contactId:N}|{triggerId:N}|{(int)channel}";

    private static List<Contact> ResolveRecipients(Client client)
    {
        var orderedContacts = client.Contacts
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ToList();

        if (orderedContacts.Count == 0)
            return orderedContacts;

        var mode = NormalizeDispatchMode(client.DispatchMode);
        if (mode == "Selected")
        {
            var selectedIds = ParseSelectedContactIds(client.SelectedDispatchContactIdsJson).ToHashSet();
            var selectedContacts = orderedContacts.Where(c => selectedIds.Contains(c.Id)).ToList();
            if (selectedContacts.Count > 0)
                return selectedContacts;
        }

        if (mode == "All" || client.SendToAllContacts)
            return orderedContacts;

        return new List<Contact> { orderedContacts[0] };
    }

    private static string NormalizeDispatchMode(string? mode)
    {
        if (string.Equals(mode, "All", StringComparison.OrdinalIgnoreCase))
            return "All";

        if (string.Equals(mode, "Selected", StringComparison.OrdinalIgnoreCase))
            return "Selected";

        return "Primary";
    }

    private static List<Guid> ParseSelectedContactIds(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(serialized) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContactSupportsChannel(Contact contact, CollectionChannel channel)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(contact.Email);
        var hasWhatsApp = !string.IsNullOrWhiteSpace(contact.WhatsAppPhone);

        return channel switch
        {
            CollectionChannel.Email => hasEmail,
            CollectionChannel.WhatsApp => hasWhatsApp,
            CollectionChannel.Both => hasEmail || hasWhatsApp,
            _ => false,
        };
    }
}
