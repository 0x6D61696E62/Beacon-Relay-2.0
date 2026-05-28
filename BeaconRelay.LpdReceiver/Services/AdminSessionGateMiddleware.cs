using BeaconRelay.LpdReceiver.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class AdminSessionGateMiddleware(RequestDelegate next, IOptions<AdminAuthOptions> options)
{
    private readonly AdminAuthOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !RequiresProtection(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }

        var returnUrl = context.Request.Path + context.Request.QueryString;
        var redirect = QueryHelpers.AddQueryString("/login.html", "returnUrl", returnUrl);
        context.Response.Redirect(redirect);
    }

    private static bool RequiresProtection(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);
    }
}
