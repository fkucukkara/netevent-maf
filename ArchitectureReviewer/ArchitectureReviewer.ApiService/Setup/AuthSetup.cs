using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace ArchitectureReviewer.ApiService.Setup;

internal static class AuthSetup
{
    public static TokenCredential CreateCredential(IConfiguration configuration, bool isDevelopment)
    {
        // AzureCliCredential is for local development only (requires `az login`).
        if (isDevelopment)
        {
            return new AzureCliCredential();
        }

        string? clientId = configuration["AZURE_CLIENT_ID"];
        if (string.IsNullOrEmpty(clientId))
        {
            return new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        }

        return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
    }

    public static void AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        string tenantId = GetRequired(configuration, "AzureAd:TenantId");
        string audience = GetRequired(configuration, "AzureAd:Audience");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.Audience = audience;
            });

        var requireSignedInUser = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        services.AddAuthorizationBuilder().SetFallbackPolicy(requireSignedInUser);
    }

    private static string GetRequired(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (value is null)
        {
            throw new InvalidOperationException($"{key} is not set.");
        }

        return value;
    }
}
