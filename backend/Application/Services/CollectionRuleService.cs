namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class CollectionRuleService : ICollectionRuleService
{
    private readonly IAppDbContext _db;
    public CollectionRuleService(IAppDbContext db) => _db = db;

    private const string DefaultRulePreventiva = "Régua Preventiva";
    private const string DefaultRuleModerada = "Régua Moderada";
    private const string DefaultRuleEscalonada = "Régua Escalonada";

    private static bool IsDefaultRuleName(string name)
        => name.Equals(DefaultRulePreventiva, StringComparison.OrdinalIgnoreCase)
        || name.Equals(DefaultRuleModerada, StringComparison.OrdinalIgnoreCase)
        || name.Equals(DefaultRuleEscalonada, StringComparison.OrdinalIgnoreCase);

    private static CollectionChannel ParseTriggerChannel(string? rawChannel)
    {
        if (string.IsNullOrWhiteSpace(rawChannel))
            return CollectionChannel.Email;

        if (rawChannel.Trim().Equals("Ambos", StringComparison.OrdinalIgnoreCase))
            return CollectionChannel.Both;

        return Enum.TryParse<CollectionChannel>(rawChannel, true, out var parsed)
            ? parsed
            : CollectionChannel.Email;
    }

    private async Task CancelPendingDispatchesByTriggerIdsAsync(IReadOnlyCollection<Guid> triggerIds)
    {
        if (triggerIds.Count == 0)
            return;

        var pending = await _db.Dispatches
            .Where(d => triggerIds.Contains(d.TriggerId) && d.Status == DispatchStatus.Pending)
            .ToListAsync();

        foreach (var dispatch in pending)
            dispatch.Status = DispatchStatus.Cancelled;
    }

    private async Task EnsureDefaultRuleAsync(Guid tenantId)
    {
        var existingRuleNames = await _db.CollectionRules
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Name)
            .ToListAsync();

        var existingNameSet = existingRuleNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingDefaultNames = new[]
        {
            DefaultRulePreventiva,
            DefaultRuleModerada,
            DefaultRuleEscalonada,
        }.Where(name => !existingNameSet.Contains(name)).ToList();

        if (missingDefaultNames.Count == 0) return;

        var templates = await _db.MessageTemplates
            .Where(t => t.TenantId == tenantId && t.Active)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        if (templates.Count == 0) return;

        var reminderEmail = templates.FirstOrDefault(t => t.Name.Contains("D-3", StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(t => t.Channel == CollectionChannel.Email);
        var reminderWa = templates.FirstOrDefault(t => t.Name.Contains("D-1", StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(t => t.Channel == CollectionChannel.WhatsApp);
        var collectionBoth = templates.FirstOrDefault(t => t.Name.Contains("D+1", StringComparison.OrdinalIgnoreCase))
            ?? templates.FirstOrDefault(t => t.Channel == CollectionChannel.Both)
            ?? templates.FirstOrDefault(t => t.Channel == CollectionChannel.WhatsApp);
        var collectionEmail = templates.FirstOrDefault(t => t.Name.Contains("D+7", StringComparison.OrdinalIgnoreCase))
            ?? templates.LastOrDefault(t => t.Channel == CollectionChannel.Email);

        List<Domain.Entities.Trigger> BuildTriggers(params (Domain.Entities.MessageTemplate? Template, CollectionChannel Channel, int Offset, int Order)[] items)
            => items
                .Where(item => item.Template is not null)
                .Select(item => new Domain.Entities.Trigger
                {
                    Id = Guid.NewGuid(),
                    TemplateId = item.Template!.Id,
                    Channel = item.Channel,
                    DaysOffset = item.Offset,
                    Reference = TriggerReference.DueDate,
                    Order = item.Order,
                    Active = true,
                })
                .ToList();

        var defaultsToCreate = new List<Domain.Entities.CollectionRule>();

        if (missingDefaultNames.Contains(DefaultRulePreventiva, StringComparer.OrdinalIgnoreCase))
        {
            var triggers = BuildTriggers(
                (reminderEmail, CollectionChannel.Email, -3, 1),
                (reminderWa, CollectionChannel.WhatsApp, -1, 2));

            if (triggers.Count > 0)
            {
                defaultsToCreate.Add(new Domain.Entities.CollectionRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = DefaultRulePreventiva,
                    Description = "Lembretes antes do vencimento",
                    Active = true,
                    Triggers = triggers,
                });
            }
        }

        if (missingDefaultNames.Contains(DefaultRuleModerada, StringComparer.OrdinalIgnoreCase))
        {
            var triggers = BuildTriggers(
                (collectionBoth, CollectionChannel.Both, 1, 1),
                (collectionEmail, CollectionChannel.Email, 7, 2));

            if (triggers.Count > 0)
            {
                defaultsToCreate.Add(new Domain.Entities.CollectionRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = DefaultRuleModerada,
                    Description = "Cobrança após vencimento com escalonamento",
                    Active = true,
                    Triggers = triggers,
                });
            }
        }

        if (missingDefaultNames.Contains(DefaultRuleEscalonada, StringComparer.OrdinalIgnoreCase))
        {
            var triggers = BuildTriggers(
                (reminderEmail, CollectionChannel.Email, -5, 1),
                (collectionBoth, CollectionChannel.Both, 2, 2),
                (collectionEmail, CollectionChannel.Email, 10, 3));

            if (triggers.Count > 0)
            {
                defaultsToCreate.Add(new Domain.Entities.CollectionRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = DefaultRuleEscalonada,
                    Description = "Fluxo mais intenso para títulos em atraso",
                    Active = false,
                    Triggers = triggers,
                });
            }
        }

        if (defaultsToCreate.Count == 0) return;

        await _db.CollectionRules.AddRangeAsync(defaultsToCreate);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CollectionRuleResponse>> ListAsync(Guid tenantId)
    {
        await EnsureDefaultRuleAsync(tenantId);

        return await _db.CollectionRules
            .Include(r => r.Triggers)
                .ThenInclude(t => t.Template)
            .Where(r => r.TenantId == tenantId)
            .Where(r => r.Active || r.Triggers.Any(t => t.Active))
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new CollectionRuleResponse(
                r.Id,
                r.Name,
                r.Description,
                r.Active,
                r.Triggers
                    .Where(t => t.Active)
                    .OrderBy(t => t.Order)
                    .Select(t => new TriggerDto(
                    t.Id,
                    t.TemplateId,
                    t.Channel.ToString(),
                    t.DaysOffset,
                    t.Reference.ToString(),
                    t.Order,
                    t.Active,
                    t.Template.Name))
                    .ToList(),
                IsDefaultRuleName(r.Name)))
            .ToListAsync();
    }

    public async Task<CollectionRuleResponse> CreateAsync(Guid tenantId, CreateCollectionRuleRequest request)
    {
        var rule = new Domain.Entities.CollectionRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            Active = request.Active,
            Triggers = request.Triggers.Select(t =>
            {
                Enum.TryParse<TriggerReference>(t.Reference, true, out var refr);
                return new Domain.Entities.Trigger
                {
                    Id = Guid.NewGuid(),
                    TemplateId = t.TemplateId,
                    Channel = ParseTriggerChannel(t.Channel),
                    DaysOffset = t.DaysOffset,
                    Reference = refr,
                    Order = t.Order,
                    Active = true
                };
            }).ToList()
        };

        await _db.CollectionRules.AddAsync(rule);
        await _db.SaveChangesAsync();

        return (await ListAsync(tenantId)).First(r => r.Id == rule.Id);
    }

    public async Task<CollectionRuleResponse?> UpdateAsync(Guid tenantId, Guid id, CreateCollectionRuleRequest request)
    {
        var rule = await _db.CollectionRules
            .Include(r => r.Triggers)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);

        if (rule is null) return null;

        rule.Name = request.Name;
        rule.Description = request.Description;
        rule.Active = request.Active;

        // Persist rule changes first, then rebuild triggers to avoid double-delete tracking conflicts.
        await _db.SaveChangesAsync();

        var existingTriggers = await _db.Triggers
            .Where(t => t.CollectionRuleId == rule.Id)
            .ToListAsync();

        var existingTriggerIds = existingTriggers.Select(t => t.Id).ToList();

        if (!request.Active)
            await CancelPendingDispatchesByTriggerIdsAsync(existingTriggerIds);

        if (existingTriggers.Count > 0)
        {
            var referencedTriggerIds = await _db.Dispatches
                .Where(d => existingTriggerIds.Contains(d.TriggerId))
                .Select(d => d.TriggerId)
                .Distinct()
                .ToListAsync();

            await CancelPendingDispatchesByTriggerIdsAsync(referencedTriggerIds);

            foreach (var trigger in existingTriggers.Where(t => referencedTriggerIds.Contains(t.Id)))
                trigger.Active = false;

            var removableTriggers = existingTriggers
                .Where(t => !referencedTriggerIds.Contains(t.Id))
                .ToList();

            if (removableTriggers.Count > 0)
                _db.Triggers.RemoveRange(removableTriggers);

            await _db.SaveChangesAsync();
        }

        var nextTriggers = request.Triggers.Select(t =>
        {
            Enum.TryParse<TriggerReference>(t.Reference, true, out var refr);
            return new Domain.Entities.Trigger
            {
                Id = Guid.NewGuid(),
                CollectionRuleId = rule.Id,
                TemplateId = t.TemplateId,
                Channel = ParseTriggerChannel(t.Channel),
                DaysOffset = t.DaysOffset,
                Reference = refr,
                Order = t.Order,
                Active = true
            };
        }).ToList();

        await _db.Triggers.AddRangeAsync(nextTriggers);
        await _db.SaveChangesAsync();

        return (await ListAsync(tenantId)).FirstOrDefault(r => r.Id == id);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid id)
    {
        var rule = await _db.CollectionRules
            .Include(r => r.Triggers)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);

        if (rule is null)
            return false;

        if (IsDefaultRuleName(rule.Name))
            throw new InvalidOperationException("Esta é uma régua padrão e não pode ser excluída.");

        var triggerIds = rule.Triggers.Select(t => t.Id).ToList();
        if (triggerIds.Count > 0)
        {
            var hasLinkedDispatches = await _db.Dispatches.AnyAsync(d => triggerIds.Contains(d.TriggerId));
            if (hasLinkedDispatches)
            {
                // Keep historical dispatch integrity: archive the rule instead of hard-deleting.
                rule.Active = false;

                foreach (var trigger in rule.Triggers)
                    trigger.Active = false;

                await CancelPendingDispatchesByTriggerIdsAsync(triggerIds);

                await _db.SaveChangesAsync();
                return true;
            }
        }

        if (rule.Triggers.Count > 0)
            _db.Triggers.RemoveRange(rule.Triggers);

        _db.CollectionRules.Remove(rule);
        await _db.SaveChangesAsync();

        return true;
    }
}
