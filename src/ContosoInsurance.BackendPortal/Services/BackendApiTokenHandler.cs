using System.Net.Http.Headers;
using Microsoft.Identity.Web;

namespace ContosoInsurance.BackendPortal.Services;

public sealed class BackendApiTokenHandler(
    IConfiguration configuration,
    IHostEnvironment environment,
    IServiceProvider serviceProvider,
    ILogger<BackendApiTokenHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var useDevBypass = environment.IsDevelopment() && configuration.GetValue<bool>("Authentication:DevBypass");
        var scopes = configuration.GetSection("BackendApi:Scopes").Get<string[]>() ?? [];

        if (!useDevBypass && scopes.Length > 0)
        {
            var tokenAcquisition = serviceProvider.GetService<ITokenAcquisition>();
            if (tokenAcquisition is not null)
            {
                try
                {
                    var token = await tokenAcquisition.GetAccessTokenForUserAsync(scopes);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Could not acquire backend API token; sending request without bearer token.");
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
