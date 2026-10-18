var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ArchitectureReviewer_ApiService>("apiservice")
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", builder.Configuration["AZURE_OPENAI_ENDPOINT"] ?? string.Empty)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-5-mini")
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

builder.Build().Run();
