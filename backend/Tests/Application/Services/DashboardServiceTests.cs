using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class DashboardServiceTests
{
    private static async Task<(DashboardService service, Guid tenantId)> SetupAsync()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client A", TaxId = "456" });

        db.Titles.AddRange(
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "T1", Amount = 1000, DueDate = DateTime.UtcNow.AddDays(10), IssueDate = DateTime.UtcNow, Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "T2", Amount = 500, DueDate = DateTime.UtcNow.AddDays(-20), IssueDate = DateTime.UtcNow.AddDays(-50), Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "T3", Amount = 2000, DueDate = DateTime.UtcNow.AddDays(-5), IssueDate = DateTime.UtcNow.AddDays(-30), Status = TitleStatus.Paid }
        );
        await db.SaveChangesAsync();

        return (new DashboardService(db), tenantId);
    }

    [Fact]
    public async Task Summary_TotalReceivable_SumsOnlyOpenAndOverdue()
    {
        var (svc, tenantId) = await SetupAsync();
        var summary = await svc.GetSummaryAsync(tenantId);

        // T1 (1000, Open, future) + T2 (500, Open, past due) = 1500
        Assert.Equal(1500, summary.TotalReceivable);
    }

    [Fact]
    public async Task Summary_TotalOverdue_SumsOnlyPastDue()
    {
        var (svc, tenantId) = await SetupAsync();
        var summary = await svc.GetSummaryAsync(tenantId);

        // Only T2 (500, past due date)
        Assert.Equal(500, summary.TotalOverdue);
    }

    [Fact]
    public async Task Summary_TotalPaid_SumsOnlyPaid()
    {
        var (svc, tenantId) = await SetupAsync();
        var summary = await svc.GetSummaryAsync(tenantId);

        // T3 = 2000
        Assert.Equal(2000, summary.TotalPaid);
    }

    [Fact]
    public async Task Summary_RecoveryRate_HandlesZeroDenominator()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Empty", TaxId = "000" });
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var summary = await svc.GetSummaryAsync(tenantId);

        Assert.Equal(0, summary.RecoveryRate);
    }

    [Fact]
    public async Task TopDefaulters_OrderedByAmountDescending()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@t.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = c1, TenantId = tenantId, UserId = userId, LegalName = "Small Debtor", TaxId = "A" });
        db.Clients.Add(new Client { Id = c2, TenantId = tenantId, UserId = userId, LegalName = "Big Debtor", TaxId = "B" });

        db.Titles.AddRange(
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = c1, UniqueCode = "S1", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-10), IssueDate = DateTime.UtcNow, Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = c2, UniqueCode = "B1", Amount = 5000, DueDate = DateTime.UtcNow.AddDays(-10), IssueDate = DateTime.UtcNow, Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = c2, UniqueCode = "B2", Amount = 3000, DueDate = DateTime.UtcNow.AddDays(-5), IssueDate = DateTime.UtcNow, Status = TitleStatus.Open }
        );
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var result = await svc.GetTopDefaultersAsync(tenantId);

        Assert.Equal("Big Debtor", result.Items[0].ClientName);
        Assert.Equal(8000, result.Items[0].TotalAmount);
        Assert.Equal(2, result.Items[0].TitleCount);
    }

    [Fact]
    public async Task SendsPerDay_IncludesQuickManualHistory()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@t.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client A", TaxId = "456" });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-MANUAL-001",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(5),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });
        db.TitleHistories.Add(new TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            TenantId = tenantId,
            Action = "Cobranca manual rapida",
            Description = "E-mail enviado para teste@empresa.com. WhatsApp enviado para +5511999999999",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var response = await svc.GetSendsPerDayAsync(tenantId);

        var day = createdAt.ToString("yyyy-MM-dd");
        var item = response.Items.Single(i => i.Day == day);
        Assert.Equal(1, item.EmailCount);
        Assert.Equal(1, item.WhatsAppCount);
    }

    [Fact]
    public async Task ChannelMetrics_IncludesQuickManualHistoryInSentCounts()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow.Date.AddDays(-2).AddHours(9);

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@t.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client A", TaxId = "456" });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-MANUAL-002",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(5),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });
        db.TitleHistories.Add(new TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            TenantId = tenantId,
            Action = "Cobranca manual rapida",
            Description = "E-mail enviado para teste@empresa.com",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        db.TitleHistories.Add(new TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            TenantId = tenantId,
            Action = "Cobranca manual rapida",
            Description = "WhatsApp enviado para +5511999999999",
            CreatedAt = createdAt.AddMinutes(1),
            UpdatedAt = createdAt.AddMinutes(1),
        });
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var metrics = await svc.GetChannelMetricsAsync(tenantId, createdAt.AddDays(-1), createdAt.AddDays(1));

        Assert.Equal(1, metrics.EmailSent);
        Assert.Equal(1, metrics.EmailDelivered);
        Assert.Equal(0, metrics.EmailViewed);
        Assert.Equal(1, metrics.WhatsAppSent);
        Assert.Equal(1, metrics.WhatsAppDelivered);
        Assert.Equal(0, metrics.WhatsAppViewed);
    }

    [Fact]
    public async Task CriticalMetrics_CriticalTitles_UsesMoreThanTenDaysOverdueRule()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@t.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client A", TaxId = "456" });

        db.Titles.AddRange(
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "C1", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-12), IssueDate = DateTime.UtcNow.AddDays(-20), Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "C2", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-11), IssueDate = DateTime.UtcNow.AddDays(-20), Status = TitleStatus.PendingData },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "N1", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-9), IssueDate = DateTime.UtcNow.AddDays(-20), Status = TitleStatus.Overdue },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "N2", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-20), IssueDate = DateTime.UtcNow.AddDays(-30), Status = TitleStatus.Paid }
        );
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var metrics = await svc.GetCriticalMetricsAsync(tenantId);

        Assert.Equal(2, metrics.CriticalTitles);
    }

    [Fact]
    public async Task CriticalMetrics_RecoveryRate_UsesOverduePaidOverOverdueBase()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@t.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client A", TaxId = "456" });

        db.Titles.AddRange(
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "R1", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-30), IssueDate = DateTime.UtcNow.AddDays(-60), Status = TitleStatus.Paid },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "R2", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-20), IssueDate = DateTime.UtcNow.AddDays(-40), Status = TitleStatus.Open },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "R3", Amount = 100, DueDate = DateTime.UtcNow.AddDays(-10), IssueDate = DateTime.UtcNow.AddDays(-20), Status = TitleStatus.Paid },
            new Title { Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, UniqueCode = "R4", Amount = 100, DueDate = DateTime.UtcNow.AddDays(5), IssueDate = DateTime.UtcNow.AddDays(-2), Status = TitleStatus.Open }
        );
        await db.SaveChangesAsync();

        var svc = new DashboardService(db);
        var metrics = await svc.GetCriticalMetricsAsync(tenantId);

        Assert.Equal(3, metrics.OverdueBaseTitles);
        Assert.Equal(2, metrics.RecoveredTitles);
        Assert.Equal(66.7, metrics.RecoveryRate);
        Assert.NotEmpty(metrics.Trend);
    }
}
