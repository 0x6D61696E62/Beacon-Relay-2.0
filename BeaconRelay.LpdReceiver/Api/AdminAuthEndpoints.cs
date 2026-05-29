using System.Security.Claims;
using BeaconRelay.LpdReceiver.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Api;

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthApi(this WebApplication app)
    {
        app.MapPost("/auth/login", LoginAsync);
        app.MapPost("/auth/logout", (Delegate)LogoutAsync);
        app.MapGet("/auth/status", GetStatusHandler);

        static IResult GetStatusHandler(HttpContext context, IOptions<AdminAuthOptions> options)
        {
            return GetStatus(context, options);
        }
    }

    private static async Task<IResult> LoginAsync(AdminLoginRequest request, HttpContext context, IOptions<AdminAuthOptions> options)
    {
        var auth = options.Value;
        if (!auth.Enabled)
        {
            return Results.Ok(new { authenticated = true, disabled = true });
        }

        if (!string.Equals(request.Username, auth.Username, StringComparison.Ordinal)
            || !string.Equals(request.Password, auth.Password, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, request.Username),
            new Claim(ClaimTypes.Role, "Admin"),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = request.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(5, auth.SessionMinutes)),
            });

        return Results.Ok(new { authenticated = true, username = request.Username });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { authenticated = false });
    }

    private static IResult GetStatus(HttpContext context, IOptions<AdminAuthOptions> options)
    {
        var auth = options.Value;
        if (!auth.Enabled)
        {
            return Results.Ok(new { enabled = false, authenticated = true });
        }

        return Results.Ok(new
        {
            enabled = true,
            authenticated = context.User.Identity?.IsAuthenticated == true,
            username = context.User.Identity?.Name,
        });
    }
}
