using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class DispatchWindowConfigServiceTests
{
    [Fact]
    public async Task SaveAndGetAsync_PersistsDispatchWindowConfiguration()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant A",
            TaxId = "123"
        });

        await db.SaveChangesAsync();

        var service = new DispatchWindowConfigService(db);

        await service.SaveAsync(tenantId, new DispatchWindowConfigRequest
        {
            Enabled = true,
            TimeZone = "UTC",
            StartTime = "08:00",
            EndTime = "19:30",
            DaysOfWeek = new List<int> { 1, 3, 5 },
            PauseAutomaticDispatchDuringProcessing = true
        });

        var result = await service.GetAsync(tenantId);

        Assert.True(result.Enabled);
        Assert.Equal("UTC", result.TimeZone);
        Assert.Equal("08:00", result.StartTime);
        Assert.Equal("19:30", result.EndTime);
        Assert.Equal(new List<int> { 1, 3, 5 }, result.DaysOfWeek);
        Assert.True(result.PauseAutomaticDispatchDuringProcessing);
    }

    [Fact]
    public async Task SaveAsync_InvalidTimeZone_ThrowsInvalidOperationException()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant B",
            TaxId = "456"
        });

        await db.SaveChangesAsync();

        var service = new DispatchWindowConfigService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(tenantId, new DispatchWindowConfigRequest
            {
                Enabled = true,
                TimeZone = "Invalid/Zone",
                StartTime = "09:00",
                EndTime = "18:00",
                DaysOfWeek = new List<int> { 1, 2, 3, 4, 5 },
                PauseAutomaticDispatchDuringProcessing = true
            }));
    }
}
