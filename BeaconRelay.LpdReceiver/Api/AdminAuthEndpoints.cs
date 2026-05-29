using System.Security.Claims;
using BeaconRelay.LpdReceiver.Data;
using BeaconRelay.LpdReceiver.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Api;

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthApi(this WebApplication app)
    {
        app.MapPost("/auth/login", LoginAsync);
        app.MapPost("/auth/logout", (Delegate)LogoutAsync);
        app.MapGet("/auth/status", GetStatusHandler);

        static IResult GetStatusHandler(HttpContext context)
        {
            return GetStatus(context);
        }
    }

    private static async Task<IResult> LoginAsync(AdminLoginRequest request, HttpContext context, AppDbContext db, IOptions<AdminAuthOptions> options, CancellationToken cancellationToken)
    {
        var auth = options.Value;
        var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Username == request.Username && x.IsEnabled, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var hasher = new PasswordHasher<AdminUserRecord>();
        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, request.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
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

        return Results.Ok(new { authenticated = true, username = request.Username, role = user.Role });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { authenticated = false });
    }

    private static IResult GetStatus(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(new { authenticated = false });
        }

        return Results.Ok(new
        {
            authenticated = true,
            username = context.User.Identity?.Name,
            role = context.User.FindFirstValue(ClaimTypes.Role) ?? AdminRoles.ReadOnly,
        });
    }
}
