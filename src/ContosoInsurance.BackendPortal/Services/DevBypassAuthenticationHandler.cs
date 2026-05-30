using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ContosoInsurance.BackendPortal.Services;

public sealed class DevBypassAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userSection = configuration.GetSection("Authentication:DevUser");
        var displayName = userSection["DisplayName"] ?? "Lambert Dev";
        var email = userSection["Email"] ?? "lambert.dev@contoso.local";
        var objectId = userSection["ObjectId"] ?? Guid.NewGuid().ToString();
        var roles = userSection.GetSection("Roles").Get<string[]>() ?? ["Operations.Admin"];

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, objectId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, email),
            new("preferred_username", email),
            new("name", displayName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
