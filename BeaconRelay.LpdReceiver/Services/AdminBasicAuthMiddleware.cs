using System.Net;
using System.Text;
using BeaconRelay.LpdReceiver.Options;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class AdminBasicAuthMiddleware(RequestDelegate next, IOptions<AdminAuthOptions> options)
{
    private readonly AdminAuthOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !RequiresProtection(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!TryValidate(context.Request, out var principalUser) || !string.Equals(principalUser, _options.Username, StringComparison.Ordinal))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{_options.Realm}\", charset=\"UTF-8\"";
            await context.Response.WriteAsync("Authentication required.");
            return;
        }

        await next(context);
    }

    private bool TryValidate(HttpRequest request, out string? principalUser)
    {
        principalUser = null;
        var authorization = request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = authorization[6..].Trim();
        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var user = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];
        if (!string.Equals(user, _options.Username, StringComparison.Ordinal) || !string.Equals(password, _options.Password, StringComparison.Ordinal))
        {
            return false;
        }

        principalUser = user;
        return true;
    }

    private static bool RequiresProtection(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);
    }
}
