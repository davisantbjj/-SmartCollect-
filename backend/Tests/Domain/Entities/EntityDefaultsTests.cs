namespace SmartCollect.Tests.Domain.Entities;

using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class EntityDefaultsTests
{
    [Fact]
    public void User_ShouldDefaultToWorkerRole()
    {
        var user = new User();
        Assert.Equal(UserRole.Worker, user.Role);
        Assert.True(user.Active);
        Assert.NotEqual(default, user.CreatedAt);
        Assert.NotEqual(default, user.UpdatedAt);
    }

    [Fact]
    public void Contact_ShouldDefaultToFinanceDepartment()
    {
        var contact = new Contact();
        Assert.Equal(ContactDepartment.Finance, contact.Department);
        Assert.False(contact.IsPrimary);
    }

    [Fact]
    public void Title_ShouldDefaultToOpenStatus()
    {
        var title = new Title();
        Assert.Equal(TitleStatus.Open, title.Status);
        Assert.NotEqual(default, title.CreatedAt);
    }

    [Fact]
    public void Tenant_ShouldDefaultToBasicPlan()
    {
        var tenant = new Tenant();
        Assert.Equal(TenantPlan.Basic, tenant.Plan);
        Assert.True(tenant.Active);
    }

    [Fact]
    public void Dispatch_ShouldDefaultToPendingStatus()
    {
        var dispatch = new Dispatch();
        Assert.Equal(DispatchStatus.Pending, dispatch.Status);
        Assert.Equal(CollectionChannel.Email, dispatch.Channel);
    }

    [Fact]
    public void FileImport_ShouldDefaultToProcessingStatus()
    {
        var fi = new FileImport();
        Assert.Equal(ImportStatus.Processing, fi.Status);
        Assert.Equal(ImportType.Excel, fi.Type);
    }

    [Fact]
    public void MessageTemplate_ShouldDefaultToCollectionType()
    {
        var t = new MessageTemplate();
        Assert.Equal(TemplateType.Collection, t.Type);
        Assert.Equal(CollectionChannel.Email, t.Channel);
        Assert.True(t.Active);
    }

    [Fact]
    public void Trigger_ShouldDefaultToDueDateReference()
    {
        var t = new Trigger();
        Assert.Equal(TriggerReference.DueDate, t.Reference);
        Assert.True(t.Active);
    }

    [Fact]
    public void Occurrence_ShouldDefaultThankYouSentToFalse()
    {
        var o = new Occurrence();
        Assert.False(o.ThankYouSent);
    }
}
