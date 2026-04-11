namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class CollectionRuleService : ICollectionRuleService
{
    private readonly IAppDbContext _db;
    public CollectionRuleService(IAppDbContext db) => _db = db;

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

    private async Task EnsureDefaultRuleAsync(Guid tenantId)
    {
        var hasAnyRule = await _db.CollectionRules.AnyAsync(r => r.TenantId == tenantId);
        if (hasAnyRule) return;

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

        var triggerCandidates = new List<(Domain.Entities.MessageTemplate? Template, CollectionChannel Channel, int Offset, int Order)>
        {
            (reminderEmail, CollectionChannel.Email, -3, 1),
            (reminderWa, CollectionChannel.WhatsApp, -1, 2),
            (collectionBoth, CollectionChannel.Both, 1, 3),
            (collectionEmail, CollectionChannel.Email, 7, 4),
        };

        var triggers = triggerCandidates
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

        if (triggers.Count == 0) return;

        var rule = new Domain.Entities.CollectionRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Régua padrão",
            Description = "Régua inicial criada automaticamente",
            Active = true,
            Triggers = triggers,
        };

        await _db.CollectionRules.AddAsync(rule);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CollectionRuleResponse>> ListAsync(Guid tenantId)
    {
        await EnsureDefaultRuleAsync(tenantId);

        return await _db.CollectionRules
            .Include(r => r.Triggers)
                .ThenInclude(t => t.Template)
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new CollectionRuleResponse(
                r.Id,
                r.Name,
                r.Description,
                r.Active,
                r.Triggers.OrderBy(t => t.Order).Select(t => new TriggerDto(
                    t.Id,
                    t.TemplateId,
                    t.Channel.ToString(),
                    t.DaysOffset,
                    t.Reference.ToString(),
                    t.Order,
                    t.Active,
                    t.Template.Name)).ToList()))
            .ToListAsync();
    }

    public async Task<CollectionRuleResponse> CreateAsync(Guid tenantId, CreateCollectionRuleRequest request)
    {
        // RN13: deactivate others if this is active
        if (request.Active)
        {
            var others = await _db.CollectionRules
                .Where(r => r.TenantId == tenantId && r.Active)
                .ToListAsync();
            foreach (var other in others) other.Active = false;
        }

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
                    Active = t.Active
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

        // RN13: deactivate others if activating
        if (request.Active && !rule.Active)
        {
            var others = await _db.CollectionRules
                .Where(r => r.TenantId == tenantId && r.Active && r.Id != id)
                .ToListAsync();
            foreach (var other in others) other.Active = false;
        }

        rule.Name = request.Name;
        rule.Description = request.Description;
        rule.Active = request.Active;

        // Remove old triggers, add new ones
        _db.Triggers.RemoveRange(rule.Triggers);
        rule.Triggers = request.Triggers.Select(t =>
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
                Active = t.Active
            };
        }).ToList();

        await _db.SaveChangesAsync();

        return (await ListAsync(tenantId)).FirstOrDefault(r => r.Id == id);
    }
}
