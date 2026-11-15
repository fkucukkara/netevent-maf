using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService.Setup;

internal static class ChatClientSetup
{
    // Deployments a caller may select through the request's "model" field.
    public static HashSet<string> GetAllowedDeployments(IConfiguration configuration, string defaultDeployment)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { defaultDeployment };

        string configured = configuration["AZURE_OPENAI_ALLOWED_DEPLOYMENTS"] ?? "";
        string[] names = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in names)
        {
            allowed.Add(name);
        }

        return allowed;
    }

    public static void AddRoutedChatClient(
        this IServiceCollection services,
        Uri endpoint,
        TokenCredential credential,
        string defaultDeployment,
        bool isDevelopment)
    {
        var azureOpenAI = new AzureOpenAIClient(endpoint, credential);

        // Telemetry wraps each deployment client so spans report the routed model, not the default.
        IChatClient CreateDeploymentClient(string deployment)
        {
            return azureOpenAI.GetChatClient(deployment).AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(configure: client => client.EnableSensitiveData = isDevelopment)
                .Build();
        }

        services.AddHttpContextAccessor();
        services.AddSingleton<IChatClient>(sp => new ModelRoutingChatClient(
            CreateDeploymentClient,
            sp.GetRequiredService<IHttpContextAccessor>(),
            defaultDeployment));
    }
}
