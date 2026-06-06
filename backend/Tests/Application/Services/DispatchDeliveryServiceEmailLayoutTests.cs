using System.Reflection;
using System.Linq;
using MimeKit;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

namespace SmartCollect.Tests.Application.Services;

public class DispatchDeliveryServiceEmailLayoutTests
{
    private const string SamplePngDataUrl =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    [Fact]
    public void BuildEmailBody_WhenLayoutEnabled_EmbedsDataImagesAsCid()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            CompanyName = "Tenant",
            TaxId = "123",
            EmailLayoutEnabled = true,
            EmailLayoutLogoUrl = SamplePngDataUrl,
            EmailLayoutHeroUrl = SamplePngDataUrl,
            EmailLayoutInstagramUrl = "teste.com"
        };

        var method = typeof(DispatchDeliveryService).GetMethod(
            "BuildEmailBody",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var entity = (MimeEntity)method!.Invoke(
            null,
            new object?[] { "Mensagem", "Assunto", tenant, null })!;

        var raw = entity.ToString();
        Assert.Contains("cid:", raw);
        Assert.DoesNotContain("data:image/", raw);
        Assert.True(TryGetHtmlBody(entity, out var html));
        Assert.Contains("https://teste.com", html);
        Assert.Contains("Instagram", html);
        var inlineResources = GetInlineResources(entity).ToList();
        Assert.Equal(3, inlineResources.Count);
        Assert.All(inlineResources, part =>
            Assert.Contains($"src=\"cid:{part.ContentId}\"", html));
        Assert.All(inlineResources, part =>
        {
            Assert.True(string.IsNullOrWhiteSpace(part.FileName));
            Assert.True(string.IsNullOrWhiteSpace(part.ContentType.Name));
            Assert.True(string.IsNullOrWhiteSpace(part.ContentDisposition?.FileName));
        });
    }

    [Fact]
    public void BuildEmailBody_WhenLayoutDisabled_DoesNotAttachLayoutImages()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            CompanyName = "Tenant",
            TaxId = "123",
            EmailLayoutEnabled = false,
            EmailLayoutLogoUrl = SamplePngDataUrl,
            EmailLayoutHeroUrl = SamplePngDataUrl
        };

        var method = typeof(DispatchDeliveryService).GetMethod(
            "BuildEmailBody",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var entity = (MimeEntity)method!.Invoke(
            null,
            new object?[] { "Mensagem", "Assunto", tenant, null })!;

        var raw = entity.ToString();
        Assert.DoesNotContain("cid:", raw);
        Assert.DoesNotContain("data:image/", raw);
        Assert.Empty(GetInlineResources(entity));
    }

    private static IEnumerable<MimePart> GetInlineResources(MimeEntity entity)
    {
        if (entity is Multipart multipart)
        {
            foreach (var child in multipart.SelectMany(GetInlineResources))
                yield return child;
        }

        if (entity is MimePart part)
        {
            if (!string.IsNullOrWhiteSpace(part.ContentId)
                && part.ContentDisposition?.Disposition == ContentDisposition.Inline)
                yield return part;
        }
    }

    private static bool TryGetHtmlBody(MimeEntity entity, out string html)
    {
        html = string.Empty;
        if (entity is TextPart textPart && textPart.IsHtml)
        {
            html = textPart.Text ?? string.Empty;
            return true;
        }

        if (entity is Multipart multipart)
        {
            foreach (var child in multipart)
            {
                if (TryGetHtmlBody(child, out html))
                    return true;
            }
        }

        return false;
    }
}
