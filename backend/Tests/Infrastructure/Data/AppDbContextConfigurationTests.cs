namespace SmartCollect.Tests.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Domain.Entities;
using SmartCollect.Infrastructure.Data;

public class AppDbContextConfigurationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Tenant_TaxId_HasUniqueIndex()
    {
        using var ctx = CreateContext();
        var indexes = ctx.Model.FindEntityType(typeof(Tenant))!.GetIndexes();
        Assert.Contains(indexes, i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Tenant.TaxId) }));
    }

    [Fact]
    public void User_TenantIdEmail_HasUniqueCompositeIndex()
    {
        using var ctx = CreateContext();
        var indexes = ctx.Model.FindEntityType(typeof(User))!.GetIndexes();
        Assert.Contains(indexes, i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(User.TenantId), nameof(User.Email) }));
    }

    [Fact]
    public void Client_TenantIdTaxId_HasUniqueCompositeIndex()
    {
        using var ctx = CreateContext();
        var indexes = ctx.Model.FindEntityType(typeof(Client))!.GetIndexes();
        Assert.Contains(indexes, i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Client.TenantId), nameof(Client.TaxId) }));
    }

    [Fact]
    public void Title_TenantIdUniqueCode_HasUniqueCompositeIndex()
    {
        using var ctx = CreateContext();
        var indexes = ctx.Model.FindEntityType(typeof(Title))!.GetIndexes();
        Assert.Contains(indexes, i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Title.TenantId), nameof(Title.UniqueCode) }));
    }

    [Fact]
    public void Title_Amount_HasPrecision18_2()
    {
        using var ctx = CreateContext();
        var prop = ctx.Model.FindEntityType(typeof(Title))!.FindProperty(nameof(Title.Amount));
        Assert.Equal(18, prop!.GetPrecision());
        Assert.Equal(2, prop.GetScale());
    }

    [Fact]
    public async Task SaveChangesAsync_UpdatesUpdatedAtOnModification()
    {
        using var ctx = CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();

        var originalUpdatedAt = tenant.UpdatedAt;
        await Task.Delay(10); // ensure time difference

        tenant.CompanyName = "Updated";
        ctx.Entry(tenant).State = EntityState.Modified;
        await ctx.SaveChangesAsync();

        Assert.True(tenant.UpdatedAt > originalUpdatedAt);
    }
}
