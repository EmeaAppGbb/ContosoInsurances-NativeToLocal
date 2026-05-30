using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ContosoInsurance.BackendApi.Authentication;

public static class BackendApiAuthenticationExtensions
{
    public const string DevelopmentBypassScheme = "DevelopmentBypass";
    private const string CompositeScheme = "BackendApiAuth";

    public static IServiceCollection AddBackendApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var bypassEnabled = configuration.GetValue("Authentication:Bypass:Enabled", environment.IsDevelopment());

        if (environment.IsDevelopment() && bypassEnabled)
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = CompositeScheme;
                    options.DefaultChallengeScheme = CompositeScheme;
                })
                .AddPolicyScheme(CompositeScheme, CompositeScheme, options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        var hasBearer = context.Request.Headers.Authorization.Any(h => h is not null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));
                        return hasBearer ? JwtBearerDefaults.AuthenticationScheme : DevelopmentBypassScheme;
                    };
                })
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options => ConfigureJwt(options, configuration))
                .AddScheme<AuthenticationSchemeOptions, DevelopmentBypassAuthenticationHandler>(DevelopmentBypassScheme, _ => { });
        }
        else
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => ConfigureJwt(options, configuration));
        }

        services.AddAuthorizationBuilder();
        return services;
    }

    private static void ConfigureJwt(JwtBearerOptions options, IConfiguration configuration)
    {
        var authority = configuration["Authentication:Entra:Authority"];
        var audience = configuration["Authentication:Entra:Audience"];

        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
        }

        if (!string.IsNullOrWhiteSpace(audience))
        {
            options.Audience = audience;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    }
}

internal sealed class DevelopmentBypassAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder, clock)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Fail("Development bypass authentication is disabled."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "dev-user"),
            new Claim(ClaimTypes.Name, "Development User"),
            new Claim(ClaimTypes.Email, "dev.user@contoso.local"),
            new Claim(ClaimTypes.Role, "Claims.Adjuster"),
            new Claim(ClaimTypes.Role, "Claims.Supervisor"),
            new Claim(ClaimTypes.Role, "Quotes.Underwriter"),
            new Claim(ClaimTypes.Role, "Operations.Admin")
        };

        var identity = new ClaimsIdentity(claims, BackendApiAuthenticationExtensions.DevelopmentBypassScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, BackendApiAuthenticationExtensions.DevelopmentBypassScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
