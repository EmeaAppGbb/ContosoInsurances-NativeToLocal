using ContosoInsurance.BackendPortal.Components;
using ContosoInsurance.BackendPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);
var useDevBypass = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:DevBypass");

builder.AddServiceDefaults();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddAuthorizationBuilder();

if (useDevBypass)
{
    builder.Services.AddAuthentication(DevBypassAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevBypassAuthenticationHandler>(DevBypassAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
}

builder.Services.AddScoped<BackendApiTokenHandler>();
builder.Services.AddHttpClient("backendapi", client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["BackendApi:BaseUrl"] ?? "https+http://backendapi");
    })
    .AddHttpMessageHandler<BackendApiTokenHandler>();

builder.Services.AddSingleton<IPortalOperationsService, PortalOperationsService>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();

app.MapGet("/auth/signin", [AllowAnonymous] (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/portal" : returnUrl },
        authenticationSchemes: [useDevBypass ? DevBypassAuthenticationHandler.SchemeName : OpenIdConnectDefaults.AuthenticationScheme]));

app.MapGet("/auth/signout", (string? returnUrl) =>
    useDevBypass
        ? Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl)
        : Results.SignOut(
            new AuthenticationProperties { RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl },
            authenticationSchemes: [OpenIdConnectDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme]));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
