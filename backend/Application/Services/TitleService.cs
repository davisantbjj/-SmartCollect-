namespace SmartCollect.Application.Services;

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
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

        if (filter.DueDateStart.HasValue)
        {
            var startUtc = NormalizeToUtc(filter.DueDateStart.Value).Date;
            query = query.Where(t => t.DueDate >= startUtc);
        }

        if (filter.DueDateEnd.HasValue)
        {
            var endUtc = NormalizeToUtc(filter.DueDateEnd.Value).Date.AddDays(1).AddTicks(-1);
            query = query.Where(t => t.DueDate <= endUtc);
        }

        var totalCount = await query.CountAsync();

        if (string.Equals(filter.OrderBy, "AmountDesc", StringComparison.OrdinalIgnoreCase))
            query = query.OrderByDescending(t => t.Amount).ThenByDescending(t => t.DueDate);
        else if (string.Equals(filter.OrderBy, "AmountAsc", StringComparison.OrdinalIgnoreCase))
            query = query.OrderBy(t => t.Amount).ThenByDescending(t => t.DueDate);
        else
            query = query.OrderByDescending(t => t.DueDate);

        var titles = await query
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .Include(t => t.Dispatches)
            .Include(t => t.Histories)
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
        var dueDateUtc = NormalizeToUtc(request.DueDate);
        var issueDateUtc = NormalizeToUtc(request.IssueDate);

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
            if (existing.DueDate.Date != dueDateUtc.Date) changes.Add($"vencimento atualizado para {dueDateUtc:dd/MM/yyyy}");
            if (existing.IssueDate.Date != issueDateUtc.Date) changes.Add($"emissao atualizada para {issueDateUtc:dd/MM/yyyy}");
            if (!string.Equals(existing.BoletoUrl ?? string.Empty, request.BoletoUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                changes.Add("link de boleto atualizado");

            // Update mutable fields only (never change TenantId, ClientId status arbitrarily)
            existing.ClientId = request.ClientId;
            existing.Amount = request.Amount;
            existing.DueDate = dueDateUtc;
            existing.IssueDate = issueDateUtc;
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

        var status = dueDateUtc.Date < DateTime.UtcNow.Date
            ? TitleStatus.Overdue
            : hasContactInfo ? TitleStatus.Open : TitleStatus.PendingData;

        var title = new Domain.Entities.Title
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = request.ClientId,
            UniqueCode = request.UniqueCode,
            Amount = request.Amount,
            DueDate = dueDateUtc,
            IssueDate = issueDateUtc,
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

        if (status is TitleStatus.Open or TitleStatus.Overdue)
        {
            await AutomaticDispatchScheduler.EnsureDispatchesForTitlesAsync(_db, tenantId, new[] { title.Id });
            await _db.SaveChangesAsync();
        }

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

        if (newStatus is TitleStatus.Open or TitleStatus.Overdue)
        {
            await AutomaticDispatchScheduler.EnsureDispatchesForTitlesAsync(_db, tenantId, new[] { title.Id });
            await _db.SaveChangesAsync();
        }

        return await GetByIdAsync(tenantId, id);
    }

    public async Task<bool> SendCollectionAsync(Guid tenantId, Guid titleId, SendCollectionRequest? request = null)
    {
        var title = await _db.Titles
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == titleId);

        if (title is null) return false;

        var isThankYouQuickTemplate = request?.UseQuickTemplate == true
            && string.Equals(request.TemplateType, "ThankYou", StringComparison.OrdinalIgnoreCase);

        // RN06: Never collect on paid/cancelled titles
        if (!isThankYouQuickTemplate && (title.Status == TitleStatus.Paid || title.Status == TitleStatus.Cancelled))
            return false;

        if (isThankYouQuickTemplate && title.Status != TitleStatus.Paid)
            throw new InvalidOperationException("Agradecimento rápido só pode ser enviado para títulos pagos.");

        var recipientContacts = ResolveCollectionRecipients(title.Client, request?.ContactIds);
        if (recipientContacts.Count == 0)
            return false;

        if (request?.UseQuickTemplate == true)
            return await SendQuickTemplateCollectionAsync(tenantId, title, recipientContacts, request, isThankYouQuickTemplate);

        var activeRules = await _db.CollectionRules
            .Include(r => r.Triggers)
                .ThenInclude(tr => tr.Template)
            .Where(r => r.TenantId == tenantId && r.Active)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        if (activeRules.Count == 0) return false;

        var totalDispatches = 0;
        foreach (var rule in activeRules)
        {
            foreach (var trigger in rule.Triggers.Where(tr => tr.Active).OrderBy(tr => tr.Order))
            {
                var eligibleContacts = recipientContacts
                    .Where(c => ContactSupportsChannel(c, trigger.Channel))
                    .ToList();

                if (eligibleContacts.Count == 0)
                    continue;

                var scheduledDate = trigger.Reference == TriggerReference.DueDate
                    ? title.DueDate.AddDays(trigger.DaysOffset)
                    : title.IssueDate.AddDays(trigger.DaysOffset);

                foreach (var contact in eligibleContacts)
                {
                    var dispatch = new Domain.Entities.Dispatch
                    {
                        Id = Guid.NewGuid(),
                        TitleId = title.Id,
                        ContactId = contact.Id,
                        TriggerId = trigger.Id,
                        Channel = trigger.Channel,
                        Status = DispatchStatus.Pending,
                        ScheduledFor = scheduledDate
                    };

                    await _db.Dispatches.AddAsync(dispatch);
                    totalDispatches++;
                }
            }
        }

        if (totalDispatches == 0)
            return false;

        var recipientMode = request?.ContactIds is { Count: > 0 }
            ? $"{recipientContacts.Count} contato(s) selecionado(s)"
            : NormalizeDispatchMode(title.Client.DispatchMode) == "Selected"
                ? "contatos selecionados"
                : title.Client.SendToAllContacts ? "todos os contatos" : "contato principal";

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Cobranca manual",
            Description = $"{totalDispatches} disparos agendados em {activeRules.Count} régua(s) ativa(s) ({recipientMode})"
        });

        await _db.SaveChangesAsync();

        if (_dispatchDeliveryService is not null)
            await _dispatchDeliveryService.ProcessPendingDispatchesAsync(tenantId);

        return true;
    }

    private async Task<bool> SendQuickTemplateCollectionAsync(
        Guid tenantId,
        Domain.Entities.Title title,
        IReadOnlyList<Domain.Entities.Contact> recipientContacts,
        SendCollectionRequest request,
        bool isThankYouQuickTemplate)
    {
        if (_dispatchDeliveryService is null)
            throw new InvalidOperationException("Serviço de envio não está disponível.");

        if (string.IsNullOrWhiteSpace(request.Body))
            throw new InvalidOperationException("Informe a mensagem para o template rápido.");

        var channel = ParseCollectionChannel(request.Channel);
        if (channel == CollectionChannel.Sms)
            throw new InvalidOperationException("Canal SMS ainda não está disponível no envio manual.");

        var sendEmail = channel is CollectionChannel.Email or CollectionChannel.Both;
        var sendWhatsApp = channel is CollectionChannel.WhatsApp or CollectionChannel.Both;

        var hasAnyEmailRecipient = recipientContacts.Any(c => !string.IsNullOrWhiteSpace(c.Email));
        var hasAnyWhatsAppRecipient = recipientContacts.Any(c => !string.IsNullOrWhiteSpace(c.WhatsAppPhone));

        if (sendEmail && !hasAnyEmailRecipient
            && (!sendWhatsApp || !hasAnyWhatsAppRecipient))
        {
            throw new InvalidOperationException("Nenhum contato com e-mail para envio.");
        }

        if (sendWhatsApp && !hasAnyWhatsAppRecipient
            && (!sendEmail || !hasAnyEmailRecipient))
        {
            throw new InvalidOperationException("Nenhum contato com WhatsApp para envio.");
        }

        var subjectTemplate = string.IsNullOrWhiteSpace(request.Subject)
            ? isThankYouQuickTemplate
                ? $"Agradecimento pelo pagamento do título {title.UniqueCode}"
                : $"Cobrança do título {title.UniqueCode}"
            : request.Subject.Trim();

        var tenantCompanyName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.CompanyName)
            .FirstOrDefaultAsync() ?? "SmartCollect";

        var body = RenderQuickTemplate(request.Body!, title, tenantCompanyName);
        var subject = RenderQuickTemplate(subjectTemplate, title, tenantCompanyName);

        var sentChannels = new List<string>();
        var failedChannels = new List<string>();

        if (sendEmail)
        {
            foreach (var contact in recipientContacts)
            {
                if (string.IsNullOrWhiteSpace(contact.Email))
                {
                    failedChannels.Add($"E-mail ({contact.Name}): contato sem e-mail.");
                    continue;
                }

                var emailResult = await _dispatchDeliveryService.SendQuickEmailAsync(
                    tenantId,
                    contact.Name,
                    contact.Email,
                    subject,
                    body,
                    isThankYouQuickTemplate ? null : title.BoletoUrl);

                if (emailResult.Sent)
                    sentChannels.Add($"E-mail enviado para {contact.Email}");
                else
                    failedChannels.Add($"E-mail ({contact.Email}): {emailResult.Detail}");
            }
        }

        if (sendWhatsApp)
        {
            foreach (var contact in recipientContacts)
            {
                if (string.IsNullOrWhiteSpace(contact.WhatsAppPhone))
                {
                    failedChannels.Add($"WhatsApp ({contact.Name}): contato sem número.");
                    continue;
                }

                var whatsAppResult = await _dispatchDeliveryService.SendQuickWhatsAppAsync(
                    tenantId,
                    contact.Name,
                    contact.WhatsAppPhone,
                    body);

                if (whatsAppResult.Sent)
                    sentChannels.Add($"WhatsApp enviado para {contact.WhatsAppPhone}");
                else
                    failedChannels.Add($"WhatsApp ({contact.WhatsAppPhone}): {whatsAppResult.Detail}");
            }
        }

        if (sentChannels.Count == 0)
        {
            var failureMessage = isThankYouQuickTemplate
                ? "Falha ao enviar agradecimento rápido."
                : "Falha ao enviar cobrança rápida.";
            throw new InvalidOperationException($"{failureMessage} {string.Join(" | ", failedChannels)}");
        }

        var details = string.Join(". ", sentChannels);
        if (failedChannels.Count > 0)
            details = $"{details}. Falhas parciais: {string.Join(" | ", failedChannels)}";

        var actionLabel = isThankYouQuickTemplate ? "Agradecimento manual enviado" : "Cobranca manual rapida";

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = actionLabel,
            Description = details
        });

        await _db.SaveChangesAsync();
        return true;
    }

    private static List<Domain.Entities.Contact> ResolveCollectionRecipients(
        Domain.Entities.Client client,
        IReadOnlyCollection<Guid>? requestedContactIds = null)
    {
        var orderedContacts = client.Contacts
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ToList();

        if (orderedContacts.Count == 0)
            return orderedContacts;

        if (requestedContactIds is { Count: > 0 })
        {
            var selectedIds = requestedContactIds.ToHashSet();
            return orderedContacts.Where(c => selectedIds.Contains(c.Id)).ToList();
        }

        var normalizedMode = NormalizeDispatchMode(client.DispatchMode);
        if (normalizedMode == "Selected")
        {
            var persistedIds = ParseSelectedDispatchContactIds(client.SelectedDispatchContactIdsJson).ToHashSet();
            var selectedContacts = orderedContacts.Where(c => persistedIds.Contains(c.Id)).ToList();
            if (selectedContacts.Count > 0)
                return selectedContacts;
        }

        if (normalizedMode == "All")
            return orderedContacts;

        if (client.SendToAllContacts)
            return orderedContacts;

        return new List<Domain.Entities.Contact> { orderedContacts[0] };
    }

    private static string NormalizeDispatchMode(string? rawMode)
    {
        if (string.Equals(rawMode, "All", StringComparison.OrdinalIgnoreCase))
            return "All";
        if (string.Equals(rawMode, "Selected", StringComparison.OrdinalIgnoreCase))
            return "Selected";

        return "Primary";
    }

    private static List<Guid> ParseSelectedDispatchContactIds(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return new List<Guid>();

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(serialized) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private static bool ContactSupportsChannel(Domain.Entities.Contact contact, CollectionChannel channel)
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
        var diasAtraso = Math.Max(0, (DateTime.UtcNow.Date - title.DueDate.Date).Days);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNome"] = title.Client.LegalName,
            ["NomeCliente"] = title.Client.LegalName,
            ["RazaoSocial"] = title.Client.LegalName,
            ["Cnpj"] = title.Client.TaxId,
            ["TituloCodigo"] = title.UniqueCode,
            ["CodigoTitulo"] = title.UniqueCode,
            ["DiasAtraso"] = diasAtraso.ToString(),
            ["Valor"] = title.Amount.ToString("C", new CultureInfo("pt-BR")),
            ["DataVencimento"] = title.DueDate.ToString("dd/MM/yyyy"),
            ["DataEmissao"] = title.IssueDate.ToString("dd/MM/yyyy"),
            ["LinkBoleto"] = title.BoletoUrl ?? string.Empty,
            ["Empresa"] = companyName,
            ["NomeEmpresa"] = companyName,
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

    private static DateTime NormalizeToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
