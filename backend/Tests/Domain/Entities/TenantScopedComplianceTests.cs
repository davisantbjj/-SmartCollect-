namespace SmartCollect.Tests.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Entities;

/// <summary>
/// Updated: User no longer implements ITenantScoped because Master users have no tenant.
/// TenantId on User is now nullable (Guid?).
/// </summary>
public class TenantScopedComplianceTests
{
    [Theory]
    [InlineData(typeof(Client))]
    [InlineData(typeof(Title))]
    [InlineData(typeof(TitleHistory))]
    [InlineData(typeof(FileImport))]
    [InlineData(typeof(CollectionRule))]
    [InlineData(typeof(MessageTemplate))]
    public void TenantScopedEntities_ShouldImplementITenantScoped(Type entityType)
    {
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(entityType),
            $"{entityType.Name} should implement ITenantScoped");
    }

    [Theory]
    [InlineData(typeof(User))]     // User no longer has ITenantScoped — TenantId is nullable for Master
    [InlineData(typeof(Contact))]
    [InlineData(typeof(Occurrence))]
    [InlineData(typeof(Trigger))]
    [InlineData(typeof(Dispatch))]
    [InlineData(typeof(Tenant))]
    public void NonTenantScopedEntities_ShouldNotImplementITenantScoped(Type entityType)
    {
        Assert.False(typeof(ITenantScoped).IsAssignableFrom(entityType),
            $"{entityType.Name} should NOT implement ITenantScoped");
    }

    [Theory]
    [InlineData(typeof(User))]
    [InlineData(typeof(Client))]
    [InlineData(typeof(Title))]
    [InlineData(typeof(TitleHistory))]
    [InlineData(typeof(Tenant))]
    [InlineData(typeof(Contact))]
    [InlineData(typeof(Occurrence))]
    [InlineData(typeof(FileImport))]
    [InlineData(typeof(CollectionRule))]
    [InlineData(typeof(Trigger))]
    [InlineData(typeof(MessageTemplate))]
    [InlineData(typeof(Dispatch))]
    public void AllEntities_ShouldInheritFromEntity(Type entityType)
    {
        Assert.True(typeof(Entity).IsAssignableFrom(entityType),
            $"{entityType.Name} should inherit from Entity");
    }

    [Fact]
    public void User_TenantId_ShouldBeNullable()
    {
        var prop = typeof(User).GetProperty("TenantId");
        Assert.NotNull(prop);
        // Guid? — underlying type is Guid, but property type is Nullable<Guid>
        Assert.True(
            Nullable.GetUnderlyingType(prop!.PropertyType) == typeof(Guid),
            "User.TenantId should be Guid? (nullable) to support Master users without a tenant");
    }
}
