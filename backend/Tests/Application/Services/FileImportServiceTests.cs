using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class FileImportServiceTests
{
    private static async Task<(FileImportService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId)> SetupAsync()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Name = "Importer",
            Email = "import@test.com",
            PasswordHash = "x",
            Role = UserRole.Admin,
            Active = true
        });
        await db.SaveChangesAsync();

        var service = new FileImportService(db, NullLogger<FileImportService>.Instance);
        return (service, db, tenantId);
    }

    [Fact]
    public async Task Upload_Csv_WithQuotesAndSemicolon_ImportsSuccessfully()
    {
        var (service, db, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento;data_emissao;email;telefone_whatsapp;link_boleto",
            "\"Construtora; Alpha\";12.345.678/0001-90;TIT-001;1500,50;aberto;2099-04-15;2099-03-01;financeiro@alpha.com;+5511999991234;https://boleto/1");

        var file = BuildFormFile("import.csv", csv, "text/csv");

        var result = await service.UploadAsync(tenantId, file);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.SuccessRows);
        Assert.Equal(0, result.ErrorRows);

        Assert.Single(db.Clients);
        Assert.Single(db.Titles);

        var title = db.Titles.Single();
        Assert.Equal("TIT-001", title.UniqueCode);
        Assert.Equal(1500.50m, title.Amount);
        Assert.Equal(TitleStatus.Open, title.Status);
    }

    [Fact]
    public async Task Upload_Xlsx_ImportsSuccessfully()
    {
        var (service, db, tenantId) = await SetupAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Import");
        ws.Cell(1, 1).Value = "nome_cliente";
        ws.Cell(1, 2).Value = "cnpj";
        ws.Cell(1, 3).Value = "codigo_titulo";
        ws.Cell(1, 4).Value = "valor";
        ws.Cell(1, 5).Value = "status";
        ws.Cell(1, 6).Value = "data_vencimento";
        ws.Cell(1, 7).Value = "data_emissao";
        ws.Cell(1, 8).Value = "email";
        ws.Cell(2, 1).Value = "Distribuidora Beta";
        ws.Cell(2, 2).Value = "98.765.432/0001-11";
        ws.Cell(2, 3).Value = "TIT-002";
        ws.Cell(2, 4).Value = "2500.75";
        ws.Cell(2, 5).Value = "aberto";
        ws.Cell(2, 6).Value = "2099-05-20";
        ws.Cell(2, 7).Value = "2099-04-01";
        ws.Cell(2, 8).Value = "financeiro@beta.com";

        await using var memory = new MemoryStream();
        workbook.SaveAs(memory);
        memory.Position = 0;

        var file = new FormFile(memory, 0, memory.Length, "file", "import.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        var result = await service.UploadAsync(tenantId, file);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.SuccessRows);
        Assert.Equal(0, result.ErrorRows);
        Assert.Single(db.Titles);
    }

    [Fact]
    public async Task Upload_WithInvalidRow_IncrementsErrorCount()
    {
        var (service, _, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento;data_emissao",
            "Cliente Valido;11.111.111/0001-11;TIT-100;100.00;aberto;2026-06-10;2026-05-01",
            "Cliente Invalido;22.222.222/0001-22;TIT-101;VALOR_INVALIDO;aberto;2026-06-10;2026-05-01");

        var file = BuildFormFile("import.csv", csv, "text/csv");

        var result = await service.UploadAsync(tenantId, file);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.SuccessRows);
        Assert.Equal(1, result.ErrorRows);
    }

    [Fact]
    public async Task Upload_WithoutIssueDateColumn_UsesDueDateAsFallback()
    {
        var (service, db, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento;email;telefone_whatsapp",
            "Cliente Sem Emissao;33.333.333/0001-33;TIT-200;199.90;aberto;2099-07-10;financeiro@cliente.com;+55 (11) 99888-7766");

        var file = BuildFormFile("import.csv", csv, "text/csv");

        var result = await service.UploadAsync(tenantId, file);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.SuccessRows);
        Assert.Equal(0, result.ErrorRows);

        var title = db.Titles.Single();
        Assert.Equal(title.DueDate, title.IssueDate);
    }

    [Fact]
    public async Task Upload_WithStatusColumn_UsesProvidedStatus()
    {
        var (service, db, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;data_vencimento;status",
            "Cliente Com Status;44.444.444/0001-44;TIT-300;350.00;2099-07-15;pago");

        var file = BuildFormFile("import.csv", csv, "text/csv");

        var result = await service.UploadAsync(tenantId, file);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.SuccessRows);

        var title = db.Titles.Single();
        Assert.Equal(TitleStatus.Paid, title.Status);
    }

    [Fact]
    public async Task Upload_WithStatusColumn_UpdatesExistingTitleStatus()
    {
        var (service, db, tenantId) = await SetupAsync();

        var firstCsv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento;email",
            "Cliente Base;55.555.555/0001-55;TIT-301;400.00;aberto;2099-08-20;financeiro@base.com");

        await service.UploadAsync(tenantId, BuildFormFile("base.csv", firstCsv, "text/csv"));

        var secondCsv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;data_vencimento;status",
            "Cliente Base;55.555.555/0001-55;TIT-301;400.00;2099-08-20;cancelado");

        var result = await service.UploadAsync(tenantId, BuildFormFile("update.csv", secondCsv, "text/csv"));

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.SuccessRows);

        var title = db.Titles.Single(t => t.UniqueCode == "TIT-301");
        Assert.Equal(TitleStatus.Cancelled, title.Status);
    }

    [Fact]
    public async Task Upload_OpenStatusWithPastDueDate_SetsOverdueAutomatically()
    {
        var (service, db, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento;email",
            "Cliente Vencido;77.777.777/0001-77;TIT-302;500.00;aberto;2020-01-10;vencido@empresa.com");

        var result = await service.UploadAsync(tenantId, BuildFormFile("overdue.csv", csv, "text/csv"));

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.SuccessRows);

        var title = db.Titles.Single(t => t.UniqueCode == "TIT-302");
        Assert.Equal(TitleStatus.Overdue, title.Status);
    }

    [Fact]
    public async Task Upload_StatusVencidoInFile_IsRejected()
    {
        var (service, _, tenantId) = await SetupAsync();

        var csv = string.Join('\n',
            "nome_cliente;cnpj;codigo_titulo;valor;status;data_vencimento",
            "Cliente Invalido;88.888.888/0001-88;TIT-303;250.00;vencido;2099-01-01");

        var result = await service.UploadAsync(tenantId, BuildFormFile("invalid-status.csv", csv, "text/csv"));

        Assert.Equal("Error", result.Status);
        Assert.Equal(1, result.ErrorRows);
        Assert.Equal(0, result.SuccessRows);
    }

    private static IFormFile BuildFormFile(string fileName, string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
