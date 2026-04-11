namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Templates;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class MessageTemplateService : IMessageTemplateService
{
    private readonly IAppDbContext _db;
    public MessageTemplateService(IAppDbContext db) => _db = db;

    private static CollectionChannel ParseTemplateChannel(string? rawChannel, CollectionChannel fallback = CollectionChannel.Email)
    {
        if (string.IsNullOrWhiteSpace(rawChannel))
            return fallback;

        var normalized = rawChannel.Trim();
        if (normalized.Equals("Ambos", StringComparison.OrdinalIgnoreCase))
            return CollectionChannel.Both;

        return Enum.TryParse<CollectionChannel>(normalized, true, out var parsed)
            ? parsed
            : fallback;
    }

    public async Task<List<MessageTemplateResponse>> ListAsync(Guid tenantId)
    {
        await EnsureDefaultTemplatesAsync(tenantId);

        return await _db.MessageTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new MessageTemplateResponse(
                t.Id, t.Name, t.Channel.ToString(), t.Subject, t.Body, t.Type.ToString(), t.Active))
            .ToListAsync();
    }

    private async Task EnsureDefaultTemplatesAsync(Guid tenantId)
    {
        var hasAny = await _db.MessageTemplates.AnyAsync(t => t.TenantId == tenantId);
        if (hasAny) return;

        var defaults = new List<Domain.Entities.MessageTemplate>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Lembrete D-3 (Email)",
                Channel = CollectionChannel.Email,
                Type = TemplateType.Reminder,
                Subject = "Lembrete: titulo {{CodigoTitulo}} vence em {{DataVencimento}}",
                Body = "Ola {{NomeCliente}}, lembramos que o titulo {{CodigoTitulo}} no valor de {{Valor}} vence em {{DataVencimento}}. Caso ja tenha pago, desconsidere esta mensagem.",
                Active = true,
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Aviso D-1 (WhatsApp)",
                Channel = CollectionChannel.WhatsApp,
                Type = TemplateType.Reminder,
                Subject = null,
                Body = "Ola {{NomeCliente}}, seu titulo {{CodigoTitulo}} ({{Valor}}) vence amanha ({{DataVencimento}}). Link do boleto: {{LinkBoleto}}",
                Active = true,
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Cobranca D+1 (WhatsApp)",
                Channel = CollectionChannel.WhatsApp,
                Type = TemplateType.Collection,
                Subject = null,
                Body = "Ola {{NomeCliente}}, identificamos atraso de {{DiasAtraso}} dia(s) no titulo {{CodigoTitulo}}. Valor: {{Valor}}. Regularize em: {{LinkBoleto}}",
                Active = true,
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Cobranca D+7 (Email)",
                Channel = CollectionChannel.Email,
                Type = TemplateType.Collection,
                Subject = "Cobranca pendente: titulo {{CodigoTitulo}}",
                Body = "Prezado(a), o titulo {{CodigoTitulo}} permanece em aberto ha {{DiasAtraso}} dia(s). Valor devido: {{Valor}}. Pagamento: {{LinkBoleto}}.",
                Active = true,
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Agradecimento de pagamento",
                Channel = CollectionChannel.Email,
                Type = TemplateType.ThankYou,
                Subject = "Pagamento confirmado - {{CodigoTitulo}}",
                Body = "Recebemos o pagamento do titulo {{CodigoTitulo}}. Obrigado pelo retorno, {{NomeCliente}}.",
                Active = true,
            },
        };

        await _db.MessageTemplates.AddRangeAsync(defaults);
        await _db.SaveChangesAsync();
    }

    public async Task<MessageTemplateResponse> CreateAsync(Guid tenantId, CreateTemplateRequest request)
    {
        var channel = ParseTemplateChannel(request.Channel, CollectionChannel.Email);
        if (!Enum.TryParse<TemplateType>(request.Type, true, out var type))
            type = TemplateType.Collection;

        // RN16: Email requires subject
        if ((channel == CollectionChannel.Email || channel == CollectionChannel.Both) && string.IsNullOrWhiteSpace(request.Subject))
            throw new InvalidOperationException("Email templates require a subject.");

        var template = new Domain.Entities.MessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Channel = channel,
            Subject = request.Subject,
            Body = request.Body,
            Type = type,
            Active = true
        };

        await _db.MessageTemplates.AddAsync(template);
        await _db.SaveChangesAsync();

        return new MessageTemplateResponse(
            template.Id, template.Name, template.Channel.ToString(),
            template.Subject, template.Body, template.Type.ToString(), template.Active);
    }

    public async Task<MessageTemplateResponse?> UpdateAsync(Guid tenantId, Guid id, UpdateTemplateRequest request)
    {
        var template = await _db.MessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId);

        if (template is null) return null;

        var channel = ParseTemplateChannel(request.Channel, template.Channel);
        if (!Enum.TryParse<TemplateType>(request.Type, true, out var type))
            type = template.Type;

        // RN16
        if ((channel == CollectionChannel.Email || channel == CollectionChannel.Both) && string.IsNullOrWhiteSpace(request.Subject))
            throw new InvalidOperationException("Email templates require a subject.");

        template.Name = request.Name;
        template.Channel = channel;
        template.Subject = request.Subject;
        template.Body = request.Body;
        template.Type = type;
        template.Active = request.Active;

        await _db.SaveChangesAsync();

        return new MessageTemplateResponse(
            template.Id, template.Name, template.Channel.ToString(),
            template.Subject, template.Body, template.Type.ToString(), template.Active);
    }
}
