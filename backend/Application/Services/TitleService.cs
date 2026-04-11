namespace SmartCollect.Application.Services;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.DTOs.Titles;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class TitleService : ITitleService
{
    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled);

    private readonly IAppDbContext _db;
    private readonly IDispatchDeliveryService? _dispatchDeliveryService;

    public TitleService(IAppDbContext db, IDispatchDeliveryService? dispatchDeliveryService = null)
    {
        _db = db;
        _dispatchDeliveryService = dispatchDeliveryService;
    }

    public async Task<PaginatedResponse<TitleResponse>> ListAsync(Guid tenantId, TitleFilterRequest filter)
    {
        var query = _db.Titles
            .Where(t => t.TenantId == tenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<TitleStatus>(filter.Status, true, out var status))
            query = query.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.ToLower();
            query = query.Where(t =>
                t.Client.LegalName.ToLower().Contains(search) ||
                t.Client.TaxId.Contains(search) ||
                t.UniqueCode.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();

        var titles = await query
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .Include(t => t.Dispatches)
            .Include(t => t.Histories)
            .OrderByDescending(t => t.DueDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var items = titles.Select(MapTitleResponse).ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / filter.PageSize);
        return new PaginatedResponse<TitleResponse>(items, filter.Page, filter.PageSize, totalCount, totalPages);
    }

    public async Task<TitleResponse?> GetByIdAsync(Guid tenantId, Guid id)
    {
        var title = await _db.Titles
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .Include(t => t.Dispatches)
            .Include(t => t.Histories)
            .Where(t => t.TenantId == tenantId && t.Id == id)
            .FirstOrDefaultAsync();

        return title is null ? null : MapTitleResponse(title);
    }

    public async Task<List<TitleHistoryResponse>> GetHistoryAsync(Guid tenantId, Guid id)
    {
        return await _db.TitleHistories
            .Where(h => h.TenantId == tenantId && h.TitleId == id)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new TitleHistoryResponse(
                h.Id,
                h.CreatedAt,
                h.Action,
                h.Description))
            .ToListAsync();
    }

    public async Task<TitleResponse> CreateAsync(Guid tenantId, CreateTitleRequest request)
    {
        var clientExists = await _db.Clients.AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId);
        if (!clientExists)
            throw new InvalidOperationException("Cliente não encontrado ou não pertence a este tenant.");

        // RN01 - Upsert by UniqueCode: if exists update mutable fields, else insert
        var existing = await _db.Titles
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.UniqueCode == request.UniqueCode);

        if (existing is not null)
        {
            var changes = new List<string>();
            if (existing.ClientId != request.ClientId) changes.Add("cliente atualizado");
            if (existing.Amount != request.Amount) changes.Add($"valor atualizado para {request.Amount:0.00}");
            if (existing.DueDate.Date != request.DueDate.Date) changes.Add($"vencimento atualizado para {request.DueDate:dd/MM/yyyy}");
            if (existing.IssueDate.Date != request.IssueDate.Date) changes.Add($"emissao atualizada para {request.IssueDate:dd/MM/yyyy}");
            if (!string.Equals(existing.BoletoUrl ?? string.Empty, request.BoletoUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                changes.Add("link de boleto atualizado");

            // Update mutable fields only (never change TenantId, ClientId status arbitrarily)
            existing.ClientId = request.ClientId;
            existing.Amount = request.Amount;
            existing.DueDate = request.DueDate;
            existing.IssueDate = request.IssueDate;
            if (!string.IsNullOrWhiteSpace(request.BoletoUrl))
                existing.BoletoUrl = request.BoletoUrl;

            if (changes.Count > 0)
            {
                await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                {
                    Id = Guid.NewGuid(),
                    TitleId = existing.Id,
                    TenantId = tenantId,
                    Action = "Atualizacao manual",
                    Description = string.Join("; ", changes)
                });
            }

            await _db.SaveChangesAsync();
            return (await GetByIdAsync(tenantId, existing.Id))!;
        }

        var client = await _db.Clients
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.ClientId && c.TenantId == tenantId)
            ?? throw new InvalidOperationException("Cliente não encontrado ou não pertence a este tenant.");

        var hasContactInfo = client.Contacts.Any(c =>
            !string.IsNullOrWhiteSpace(c.Email) ||
            !string.IsNullOrWhiteSpace(c.WhatsAppPhone));

        var status = request.DueDate.Date < DateTime.UtcNow.Date
            ? TitleStatus.Overdue
            : hasContactInfo ? TitleStatus.Open : TitleStatus.PendingData;

        var title = new Domain.Entities.Title
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = request.ClientId,
            UniqueCode = request.UniqueCode,
            Amount = request.Amount,
            DueDate = request.DueDate,
            IssueDate = request.IssueDate,
            BoletoUrl = request.BoletoUrl,
            Status = status
        };

        await _db.Titles.AddAsync(title);
        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Criacao manual",
            Description = $"Titulo criado com status {status}"
        });
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(tenantId, title.Id))!;
    }

    public async Task<TitleResponse?> UpdateStatusAsync(Guid tenantId, Guid id, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("Informe o status do título.");

        if (!Enum.TryParse<TitleStatus>(status, true, out var newStatus))
            throw new InvalidOperationException("Status inválido para o título.");

        if (newStatus == TitleStatus.PendingData)
            throw new InvalidOperationException("Status 'Pendente de Dados' é controlado pelo sistema.");

        var title = await _db.Titles
            .Where(t => t.TenantId == tenantId && t.Id == id)
            .FirstOrDefaultAsync();

        if (title is null) return null;

        var oldStatus = title.Status;
        if (oldStatus == newStatus)
            return await GetByIdAsync(tenantId, id);

        title.Status = newStatus;

        if (newStatus is TitleStatus.Paid or TitleStatus.Cancelled)
        {
            var pendingDispatches = await _db.Dispatches
                .Where(d => d.TitleId == title.Id && d.Status == DispatchStatus.Pending)
                .ToListAsync();

            foreach (var dispatch in pendingDispatches)
                dispatch.Status = DispatchStatus.Cancelled;
        }

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Atualizacao manual de status",
            Description = $"Status alterado de {oldStatus} para {newStatus}"
        });

        await _db.SaveChangesAsync();
        return await GetByIdAsync(tenantId, id);
    }

    public async Task<bool> SendCollectionAsync(Guid tenantId, Guid titleId, SendCollectionRequest? request = null)
    {
        var title = await _db.Titles
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == titleId);

        if (title is null) return false;

        // RN06: Never collect on paid/cancelled titles
        if (title.Status == TitleStatus.Paid || title.Status == TitleStatus.Cancelled)
            return false;

        var primaryContact = title.Client.Contacts.FirstOrDefault(c => c.IsPrimary)
                          ?? title.Client.Contacts.FirstOrDefault();

        if (primaryContact is null) return false;

        if (request?.UseQuickTemplate == true)
            return await SendQuickTemplateCollectionAsync(tenantId, title, primaryContact, request);

        var activeRule = await _db.CollectionRules
            .Include(r => r.Triggers)
                .ThenInclude(tr => tr.Template)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Active);

        if (activeRule is null) return false;

        foreach (var trigger in activeRule.Triggers.Where(tr => tr.Active).OrderBy(tr => tr.Order))
        {
            var scheduledDate = trigger.Reference == TriggerReference.DueDate
                ? title.DueDate.AddDays(trigger.DaysOffset)
                : title.IssueDate.AddDays(trigger.DaysOffset);

            var dispatch = new Domain.Entities.Dispatch
            {
                Id = Guid.NewGuid(),
                TitleId = title.Id,
                ContactId = primaryContact.Id,
                TriggerId = trigger.Id,
                Channel = trigger.Channel,
                Status = DispatchStatus.Pending,
                ScheduledFor = scheduledDate
            };

            await _db.Dispatches.AddAsync(dispatch);
        }

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Cobranca manual",
            Description = $"{activeRule.Triggers.Count(tr => tr.Active)} disparos agendados"
        });

        await _db.SaveChangesAsync();

        if (_dispatchDeliveryService is not null)
            await _dispatchDeliveryService.ProcessPendingDispatchesAsync(tenantId);

        return true;
    }

    private async Task<bool> SendQuickTemplateCollectionAsync(
        Guid tenantId,
        Domain.Entities.Title title,
        Domain.Entities.Contact contact,
        SendCollectionRequest request)
    {
        if (_dispatchDeliveryService is null)
            throw new InvalidOperationException("Serviço de envio não está disponível.");

        if (string.IsNullOrWhiteSpace(request.Body))
            throw new InvalidOperationException("Informe a mensagem para o template rápido.");

        var channel = ParseCollectionChannel(request.Channel);
        if (channel == CollectionChannel.Sms)
            throw new InvalidOperationException("Canal SMS ainda não está disponível no envio manual.");

        if (channel == CollectionChannel.WhatsApp)
            throw new InvalidOperationException("Canal WhatsApp ainda não está disponível para envio rápido. Use E-mail ou Ambos.");

        if (string.IsNullOrWhiteSpace(contact.Email))
            throw new InvalidOperationException("Contato principal sem e-mail para envio.");

        var subjectTemplate = string.IsNullOrWhiteSpace(request.Subject)
            ? $"Cobrança do título {title.UniqueCode}"
            : request.Subject.Trim();

        var tenantCompanyName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.CompanyName)
            .FirstOrDefaultAsync() ?? "SmartCollect";

        var body = RenderQuickTemplate(request.Body!, title, tenantCompanyName);
        var subject = RenderQuickTemplate(subjectTemplate, title, tenantCompanyName);

        var sent = await _dispatchDeliveryService.SendQuickEmailAsync(
            tenantId,
            contact.Name,
            contact.Email,
            subject,
            body);

        if (!sent)
            throw new InvalidOperationException("Falha ao enviar cobrança rápida. Verifique a configuração SMTP.");

        var details = channel == CollectionChannel.Both
            ? $"E-mail enviado para {contact.Email}. Canal WhatsApp selecionado para uso conjunto."
            : $"E-mail enviado para {contact.Email}.";

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Cobranca manual rapida",
            Description = details
        });

        await _db.SaveChangesAsync();
        return true;
    }

    private static CollectionChannel ParseCollectionChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return CollectionChannel.Email;

        if (string.Equals(channel, "Ambos", StringComparison.OrdinalIgnoreCase))
            return CollectionChannel.Both;

        if (Enum.TryParse<CollectionChannel>(channel, true, out var parsed))
            return parsed;

        throw new InvalidOperationException("Canal inválido para envio manual.");
    }

    private static string RenderQuickTemplate(string template, Domain.Entities.Title title, string companyName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNome"] = title.Client.LegalName,
            ["RazaoSocial"] = title.Client.LegalName,
            ["Cnpj"] = title.Client.TaxId,
            ["TituloCodigo"] = title.UniqueCode,
            ["CodigoTitulo"] = title.UniqueCode,
            ["Valor"] = title.Amount.ToString("C"),
            ["DataVencimento"] = title.DueDate.ToString("dd/MM/yyyy"),
            ["DataEmissao"] = title.IssueDate.ToString("dd/MM/yyyy"),
            ["LinkBoleto"] = title.BoletoUrl ?? string.Empty,
            ["Empresa"] = companyName,
        };

        return TemplateRegex.Replace(template ?? string.Empty, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static TitleResponse MapTitleResponse(Domain.Entities.Title t)
    {
        var channels = GetChannels(t);
        var latestHistory = t.Histories.OrderByDescending(h => h.CreatedAt).FirstOrDefault();

        return new TitleResponse(
            t.Id,
            t.ClientId,
            t.Client.LegalName,
            t.Client.TaxId,
            t.UniqueCode,
            t.Amount,
            t.DueDate,
            t.IssueDate,
            t.BoletoUrl,
            t.Status.ToString(),
            channels,
            latestHistory?.Action,
            latestHistory?.CreatedAt,
            IsBoletoOverdue(t));
    }

    private static List<string> GetChannels(Domain.Entities.Title t)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (t.Client?.Contacts?.Any(c => !string.IsNullOrWhiteSpace(c.Email)) == true)
            channels.Add("Email");

        if (t.Client?.Contacts?.Any(c => !string.IsNullOrWhiteSpace(c.WhatsAppPhone)) == true)
            channels.Add("WhatsApp");

        foreach (var channel in t.Dispatches.Select(d => d.Channel.ToString()))
            channels.Add(channel);

        return channels.ToList();
    }

    private static bool IsBoletoOverdue(Domain.Entities.Title t)
    {
        if (string.IsNullOrWhiteSpace(t.BoletoUrl))
            return false;

        if (t.Status is TitleStatus.Paid or TitleStatus.Cancelled)
            return false;

        return t.DueDate.Date < DateTime.UtcNow.Date;
    }
}
