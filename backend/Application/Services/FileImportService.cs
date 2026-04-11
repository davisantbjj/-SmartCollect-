namespace SmartCollect.Application.Services;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.DTOs.Import;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class FileImportService : IFileImportService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<FileImportService> _logger;

    private static readonly HashSet<string> RequiredColumns = new(
        ["nome_cliente", "cnpj", "codigo_titulo", "valor", "status", "data_vencimento"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["razao_social"] = "nome_cliente",
        ["codigo_unico"] = "codigo_titulo",
        ["telefone"] = "telefone_whatsapp",
        ["whatsapp"] = "telefone_whatsapp",
        ["vencimento"] = "data_vencimento",
        ["emissao"] = "data_emissao",
        ["situacao"] = "status",
        ["status_titulo"] = "status"
    };

    private static readonly CultureInfo[] ParseCultures =
    [
        CultureInfo.InvariantCulture,
        new CultureInfo("pt-BR")
    ];

    public FileImportService(IAppDbContext db, ILogger<FileImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ImportResultResponse> UploadAsync(Guid tenantId, IFormFile file)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == tenantId);
        if (!tenantExists)
            throw new InvalidOperationException("Tenant não encontrado para esta sessão. Faça login novamente.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var type = extension switch
        {
            ".csv" => ImportType.Csv,
            ".xlsx" or ".xlsm" or ".xls" => ImportType.Excel,
            _ => throw new InvalidOperationException("Unsupported file type. Allowed: .csv, .xlsx, .xlsm, .xls")
        };

        var fileImport = new Domain.Entities.FileImport
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = file.FileName,
            Type = type,
            Status = ImportStatus.Processing
        };

        await _db.FileImports.AddAsync(fileImport);

        try
        {
            var parsedImport = type == ImportType.Csv
                ? await ParseCsvAsync(file)
                : ParseExcel(file);

            if (parsedImport.Rows.Count == 0)
            {
                fileImport.Status = ImportStatus.Error;
                await _db.SaveChangesAsync();
                return ToResponse(fileImport);
            }

            var missingColumns = RequiredColumns.Where(c => !parsedImport.Headers.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missingColumns.Count > 0)
            {
                _logger.LogWarning(
                    "Import {ImportId} missing required columns for tenant {TenantId}: {MissingColumns}",
                    fileImport.Id,
                    tenantId,
                    string.Join(", ", missingColumns));

                fileImport.Status = ImportStatus.Error;
                await _db.SaveChangesAsync();
                return ToResponse(fileImport);
            }

            var defaultUserId = await _db.Users
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            if (defaultUserId == Guid.Empty)
                throw new InvalidOperationException("Cannot import without at least one user in tenant.");

            var clientsByTaxId = await _db.Clients
                .Where(c => c.TenantId == tenantId)
                .ToDictionaryAsync(c => c.TaxId, StringComparer.OrdinalIgnoreCase);

            var titlesByUniqueCode = await _db.Titles
                .Where(t => t.TenantId == tenantId)
                .ToDictionaryAsync(t => t.UniqueCode, StringComparer.OrdinalIgnoreCase);

            var totalRows = 0;
            var successRows = 0;
            var errorRows = 0;

            foreach (var row in parsedImport.Rows)
            {
                totalRows++;

                try
                {
                    var uniqueCode = GetRequiredValue(row.Values, "codigo_titulo");
                    var taxId = GetRequiredValue(row.Values, "cnpj");
                    var clientName = GetRequiredValue(row.Values, "nome_cliente");
                    var rawAmount = GetRequiredValue(row.Values, "valor");
                    var rawStatus = GetRequiredValue(row.Values, "status");
                    var rawDueDate = GetRequiredValue(row.Values, "data_vencimento");
                    var rawIssueDate = GetOptionalValue(row.Values, "data_emissao");

                    if (!TryParseDecimal(rawAmount, out var amount))
                        throw new InvalidOperationException($"Invalid amount: '{rawAmount}'");

                    var status = ParseRequiredStatus(rawStatus);

                    if (!TryParseDate(rawDueDate, out var dueDate))
                        throw new InvalidOperationException($"Invalid due date: '{rawDueDate}'");

                    var issueDate = dueDate;
                    if (!string.IsNullOrWhiteSpace(rawIssueDate))
                    {
                        if (!TryParseDate(rawIssueDate, out issueDate))
                            throw new InvalidOperationException($"Invalid issue date: '{rawIssueDate}'");
                    }

                    var email = GetOptionalValue(row.Values, "email");
                    var phone = NormalizePhone(GetOptionalValue(row.Values, "telefone_whatsapp"));
                    var boletoUrl = GetOptionalValue(row.Values, "link_boleto");
                    var hasContactInfo = !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone);
                    var finalStatus = ComputeImportedStatus(status, dueDate, hasContactInfo);

                    if (!clientsByTaxId.TryGetValue(taxId, out var client))
                    {
                        client = new Domain.Entities.Client
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            UserId = defaultUserId,
                            LegalName = clientName,
                            TaxId = taxId
                        };
                        await _db.Clients.AddAsync(client);
                        clientsByTaxId[taxId] = client;
                    }
                    else
                    {
                        client.LegalName = clientName;
                    }

                    if (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone))
                    {
                        var contact = await _db.Contacts
                            .FirstOrDefaultAsync(c => c.ClientId == client.Id && c.IsPrimary);

                        if (contact is null)
                        {
                            contact = new Domain.Entities.Contact
                            {
                                Id = Guid.NewGuid(),
                                ClientId = client.Id,
                                Name = clientName,
                                Email = email,
                                WhatsAppPhone = phone,
                                IsPrimary = true
                            };
                            await _db.Contacts.AddAsync(contact);
                        }
                        else
                        {
                            contact.Name = clientName;
                            if (!string.IsNullOrWhiteSpace(email)) contact.Email = email;
                            if (!string.IsNullOrWhiteSpace(phone)) contact.WhatsAppPhone = phone;
                        }
                    }

                    if (titlesByUniqueCode.TryGetValue(uniqueCode, out var existingTitle))
                    {
                        var previousStatus = existingTitle.Status;
                        var previousDueDate = existingTitle.DueDate;
                        var previousAmount = existingTitle.Amount;
                        var previousBoleto = existingTitle.BoletoUrl;

                        existingTitle.ClientId = client.Id;
                        existingTitle.Amount = amount;
                        existingTitle.DueDate = dueDate;
                        existingTitle.IssueDate = issueDate;
                        existingTitle.ImportId = fileImport.Id;

                        if (!string.IsNullOrWhiteSpace(boletoUrl))
                            existingTitle.BoletoUrl = boletoUrl;

                        existingTitle.Status = finalStatus;

                        var updateDescription = BuildImportUpdateDescription(
                            previousStatus,
                            existingTitle.Status,
                            previousDueDate,
                            existingTitle.DueDate,
                            previousAmount,
                            existingTitle.Amount,
                            previousBoleto,
                            existingTitle.BoletoUrl);

                        if (!string.IsNullOrWhiteSpace(updateDescription))
                        {
                            await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                            {
                                Id = Guid.NewGuid(),
                                TitleId = existingTitle.Id,
                                TenantId = tenantId,
                                Action = "Atualizacao via planilha",
                                Description = updateDescription
                            });
                        }
                    }
                    else
                    {
                        var title = new Domain.Entities.Title
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            ClientId = client.Id,
                            ImportId = fileImport.Id,
                            UniqueCode = uniqueCode,
                            Amount = amount,
                            DueDate = dueDate,
                            IssueDate = issueDate,
                            BoletoUrl = boletoUrl,
                            Status = finalStatus
                        };

                        await _db.Titles.AddAsync(title);
                        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                        {
                            Id = Guid.NewGuid(),
                            TitleId = title.Id,
                            TenantId = tenantId,
                            Action = "Criacao via planilha",
                            Description = $"Titulo criado com status {finalStatus}"
                        });
                        titlesByUniqueCode[uniqueCode] = title;
                    }

                    successRows++;
                }
                catch (Exception ex)
                {
                    errorRows++;
                    _logger.LogWarning(
                        ex,
                        "Import row failed for import {ImportId}, tenant {TenantId}, row {RowNumber}",
                        fileImport.Id,
                        tenantId,
                        row.RowNumber);
                }
            }

            fileImport.TotalRows = totalRows;
            fileImport.SuccessRows = successRows;
            fileImport.ErrorRows = errorRows;
            fileImport.Status = errorRows == totalRows ? ImportStatus.Error : ImportStatus.Completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Import failed for tenant {TenantId}, file {FileName}",
                tenantId,
                file.FileName);

            fileImport.Status = ImportStatus.Error;
        }

        await _db.SaveChangesAsync();
        return ToResponse(fileImport);
    }

    private async Task<ParsedImportResult> ParseCsvAsync(IFormFile file)
    {
        string? headerPreview;
        using (var previewStream = file.OpenReadStream())
        using (var previewReader = new StreamReader(previewStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            headerPreview = await previewReader.ReadLineAsync();
        }

        if (string.IsNullOrWhiteSpace(headerPreview))
            return new ParsedImportResult([], []);

        var delimiter = DetectDelimiter(headerPreview);

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = args =>
            {
                _logger.LogWarning(
                    "Malformed CSV data at row {Row}, raw record: {RawRecord}",
                    args.Context?.Parser?.Row ?? 0,
                    args.RawRecord);
            }
        };

        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync() || !csv.ReadHeader())
            return new ParsedImportResult([], []);

        var headers = (csv.HeaderRecord ?? [])
            .Select(MapHeader)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var originalHeaders = csv.HeaderRecord ?? [];
        var mappedHeaders = originalHeaders.Select(MapHeader).ToArray();
        var rows = new List<ParsedImportRow>();

        while (await csv.ReadAsync())
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < mappedHeaders.Length; i++)
            {
                var mappedHeader = mappedHeaders[i];
                if (string.IsNullOrWhiteSpace(mappedHeader)) continue;

                if (!values.ContainsKey(mappedHeader))
                    values[mappedHeader] = csv.GetField(i)?.Trim();
            }

            if (values.Values.All(string.IsNullOrWhiteSpace))
                continue;

            rows.Add(new ParsedImportRow(csv.Context.Parser?.Row ?? 0, values));
        }

        return new ParsedImportResult(headers, rows);
    }

    private ParsedImportResult ParseExcel(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault();
        var firstUsedRow = worksheet?.FirstRowUsed();
        var lastUsedRow = worksheet?.LastRowUsed();
        var lastUsedColumn = worksheet?.LastColumnUsed();

        if (worksheet is null || firstUsedRow is null || lastUsedRow is null || lastUsedColumn is null)
            return new ParsedImportResult([], []);

        var firstRowNumber = firstUsedRow.RowNumber();
        var lastRowNumber = lastUsedRow.RowNumber();
        var lastColumnNumber = lastUsedColumn.ColumnNumber();

        var columnHeaderMap = new Dictionary<int, string>();
        for (var col = 1; col <= lastColumnNumber; col++)
        {
            var mappedHeader = MapHeader(worksheet.Cell(firstRowNumber, col).GetString());
            if (string.IsNullOrWhiteSpace(mappedHeader)) continue;
            if (!columnHeaderMap.Values.Contains(mappedHeader, StringComparer.OrdinalIgnoreCase))
                columnHeaderMap[col] = mappedHeader;
        }

        var headers = columnHeaderMap.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<ParsedImportRow>();
        for (var rowNumber = firstRowNumber + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (columnNumber, header) in columnHeaderMap)
                values[header] = worksheet.Cell(rowNumber, columnNumber).GetString()?.Trim();

            if (values.Values.All(string.IsNullOrWhiteSpace))
                continue;

            rows.Add(new ParsedImportRow(rowNumber, values));
        }

        return new ParsedImportResult(headers, rows);
    }

    private static string GetRequiredValue(IReadOnlyDictionary<string, string?> values, string key)
    {
        if (!values.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            throw new InvalidOperationException($"Missing required value: '{key}'");

        return rawValue.Trim();
    }

    private static string? GetOptionalValue(IReadOnlyDictionary<string, string?> values, string key)
    {
        if (!values.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            return null;

        return rawValue.Trim();
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        var sanitized = value
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        var commaIndex = sanitized.LastIndexOf(',');
        var dotIndex = sanitized.LastIndexOf('.');

        if (commaIndex >= 0 && dotIndex >= 0)
        {
            var normalized = commaIndex > dotIndex
                ? sanitized.Replace(".", string.Empty).Replace(",", ".")
                : sanitized.Replace(",", string.Empty);

            if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                return true;
        }
        else if (commaIndex >= 0)
        {
            var digitsAfterComma = sanitized.Length - commaIndex - 1;
            var normalized = digitsAfterComma is 1 or 2
                ? sanitized.Replace(",", ".")
                : sanitized.Replace(",", string.Empty);

            if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                return true;
        }
        else if (dotIndex >= 0)
        {
            var digitsAfterDot = sanitized.Length - dotIndex - 1;
            var normalized = digitsAfterDot is 1 or 2
                ? sanitized
                : sanitized.Replace(".", string.Empty);

            if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                return true;
        }

        foreach (var culture in ParseCultures)
        {
            if (decimal.TryParse(sanitized, NumberStyles.Any, culture, out result))
                return true;
        }

        result = default;
        return false;
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss" };
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, styles, out result))
        {
            if (result.Kind != DateTimeKind.Utc)
                result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
            return true;
        }

        foreach (var culture in ParseCultures)
        {
            if (DateTime.TryParse(value, culture, styles, out result))
            {
                if (result.Kind != DateTimeKind.Utc)
                    result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
                return true;
            }
        }

        return false;
    }

    private static TitleStatus ParseRequiredStatus(string rawStatus)
    {
        if (TryParseTitleStatus(rawStatus, out var status))
            return status;

        throw new InvalidOperationException(
                $"Invalid status: '{rawStatus}'. Allowed values: Em Aberto, Pendente de dados, Pago, Em Atraso, Cancelado.");
    }

    private static bool TryParseTitleStatus(string rawStatus, out TitleStatus status)
    {
        status = TitleStatus.Open;
        var normalized = NormalizeHeader(rawStatus);

        status = normalized switch
        {
            "open" or "aberto" or "em_aberto" => TitleStatus.Open,
            "pending_data" or "pendingdata" or "pendente_de_dados" or "pendente_dados" or "pendente" => TitleStatus.PendingData,
            "paid" or "pago" or "liquidado" or "recebido" => TitleStatus.Paid,
            "overdue" or "em_atraso" or "vencido" or "atrasado" => TitleStatus.Overdue,
            "cancelled" or "canceled" or "cancelado" => TitleStatus.Cancelled,
            _ => status
        };

        return normalized is "open" or "aberto" or "em_aberto"
            or "pending_data" or "pendingdata" or "pendente_de_dados" or "pendente_dados" or "pendente"
            or "paid" or "pago" or "liquidado" or "recebido"
            or "overdue" or "em_atraso" or "vencido" or "atrasado"
            or "cancelled" or "canceled" or "cancelado";
    }

    private static TitleStatus ComputeImportedStatus(TitleStatus parsedStatus, DateTime dueDate, bool hasContactInfo)
    {
        if (parsedStatus is TitleStatus.Paid or TitleStatus.Cancelled)
            return parsedStatus;

        if (!hasContactInfo)
            return TitleStatus.PendingData;

        return dueDate.Date < DateTime.UtcNow.Date
            ? TitleStatus.Overdue
            : TitleStatus.Open;
    }

    private static string? NormalizePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return null;

        var digits = Regex.Replace(rawPhone, "\\D", string.Empty);

        // Accept common BR formats with or without +55 and persist as numeric canonical value.
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.Length is < 10 or > 13)
            throw new InvalidOperationException($"Invalid phone number: '{rawPhone}'");

        return digits;
    }

    private static string BuildImportUpdateDescription(
        TitleStatus previousStatus,
        TitleStatus currentStatus,
        DateTime previousDueDate,
        DateTime currentDueDate,
        decimal previousAmount,
        decimal currentAmount,
        string? previousBoleto,
        string? currentBoleto)
    {
        var changes = new List<string>();

        if (previousStatus != currentStatus)
            changes.Add($"status atualizado: {previousStatus} -> {currentStatus}");

        if (previousDueDate.Date != currentDueDate.Date)
            changes.Add($"vencimento atualizado: {previousDueDate:dd/MM/yyyy} -> {currentDueDate:dd/MM/yyyy}");

        if (previousAmount != currentAmount)
            changes.Add($"valor atualizado: {previousAmount:0.00} -> {currentAmount:0.00}");

        if (!string.Equals(previousBoleto ?? string.Empty, currentBoleto ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            changes.Add("link de boleto atualizado");

        return changes.Count == 0 ? string.Empty : string.Join("; ", changes);
    }

    private static string DetectDelimiter(string line)
    {
        var semicolonCount = line.Count(ch => ch == ';');
        var commaCount = line.Count(ch => ch == ',');
        return semicolonCount >= commaCount ? ";" : ",";
    }

    private static string MapHeader(string rawHeader)
    {
        var normalized = NormalizeHeader(rawHeader);
        if (HeaderAliases.TryGetValue(normalized, out var canonical))
            return canonical;

        return normalized;
    }

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var noDiacritics = RemoveDiacritics(value.Trim().ToLowerInvariant());
        var normalized = Regex.Replace(noDiacritics, "[^a-z0-9]+", "_").Trim('_');
        return normalized;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static ImportResultResponse ToResponse(Domain.Entities.FileImport fi)
        => new(fi.Id, fi.FileName, fi.TotalRows, fi.SuccessRows, fi.ErrorRows, fi.Status.ToString());

    private sealed record ParsedImportRow(int RowNumber, IReadOnlyDictionary<string, string?> Values);

    private sealed record ParsedImportResult(IReadOnlyCollection<string> Headers, IReadOnlyCollection<ParsedImportRow> Rows);
}
