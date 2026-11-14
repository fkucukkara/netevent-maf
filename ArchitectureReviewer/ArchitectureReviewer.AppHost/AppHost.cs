using Azure.Provisioning.CognitiveServices;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ArchitectureReviewer_ApiService>("apiservice")
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? string.Empty)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-6-luna")
    .WithEnvironment("AZURE_OPENAI_ALLOWED_DEPLOYMENTS", builder.Configuration["AZURE_OPENAI_ALLOWED_DEPLOYMENTS"] ?? string.Empty)
    .WithUrls(context =>
    {
        var baseUrl = context.Urls.FirstOrDefault();
        if (baseUrl is not null)
        {
            context.Urls.Add(new()
            {
                Url = baseUrl.Url.TrimEnd('/') + "/devui",
                DisplayText = "DevUI Visual App"
            });
        }
    });

// Deployment-only wiring; local runs are unchanged.
if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureContainerAppEnvironment("aca");

    var tenantId = builder.AddParameter("azuread-tenant-id");
    var audience = builder.AddParameter("azuread-audience");
    var openAiName = builder.AddParameter("openai-name");
    var openAiResourceGroup = builder.AddParameter("openai-resource-group");

    var openAi = builder.AddAzureOpenAI("openai").AsExisting(openAiName, openAiResourceGroup);

    apiService
        .WithExternalHttpEndpoints()
        .WithEnvironment("AzureAd__TenantId", tenantId)
        .WithEnvironment("AzureAd__Audience", audience)
        .WithRoleAssignments(openAi, CognitiveServicesBuiltInRole.CognitiveServicesOpenAIUser);
}

builder.Build().Run();
