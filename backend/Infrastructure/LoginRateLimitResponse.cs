namespace SmartCollect.Infrastructure;

using Microsoft.AspNetCore.Http;

public static class LoginRateLimitResponse
{
    public static async Task WriteAsync(HttpContext httpContext, TimeSpan retryAfter, CancellationToken cancellationToken = default)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(new
        {
            message = "Muitas tentativas de login. Aguarde para tentar novamente.",
            retryAfterSeconds
        }, cancellationToken);
    }
}
