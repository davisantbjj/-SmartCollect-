namespace SmartCollect.Tests.Infrastructure;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SmartCollect.Infrastructure;

public class LoginRateLimitResponseTests
{
    [Fact]
    public async Task WriteAsync_ReturnsRetryAfterHeaderAndJsonPayload()
    {
        var context = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await LoginRateLimitResponse.WriteAsync(context, TimeSpan.FromSeconds(74.2));

        responseBody.Position = 0;
        var payload = await JsonDocument.ParseAsync(responseBody);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("75", context.Response.Headers["Retry-After"].ToString());
        Assert.Equal(75, payload.RootElement.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Contains("Muitas tentativas", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WriteAsync_UsesMinimumOneSecondRetryAfter()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await LoginRateLimitResponse.WriteAsync(context, TimeSpan.Zero);

        Assert.Equal("1", context.Response.Headers["Retry-After"].ToString());
    }
}
