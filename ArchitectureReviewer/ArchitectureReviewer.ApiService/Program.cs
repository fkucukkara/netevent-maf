using ArchitectureReviewer.ApiService.Guardrails;
using ArchitectureReviewer.ApiService.Middleware;
using ArchitectureReviewer.ApiService.Setup;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

bool isDevelopment = builder.Environment.IsDevelopment();

/////// GUARDRAILS ///////

var guardrails = builder.Configuration.GetSection("Guardrails").Get<GuardrailOptions>() ?? new GuardrailOptions();
builder.Services.AddSingleton(guardrails);
builder.Services.AddSingleton<InputGuard>();
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(GuardrailMetrics.MeterName));
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = guardrails.MaxRequestBodyBytes);
builder.Services.AddGuardrailRateLimiting(guardrails);

/////// CHAT CLIENT ///////

string? endpointSetting = builder.Configuration["AZURE_OPENAI_ENDPOINT"];
if (endpointSetting is null)
{
    throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
}

string deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-6-luna";
var credential = AuthSetup.CreateCredential(builder.Configuration, isDevelopment);
var allowedDeployments = ChatClientSetup.GetAllowedDeployments(builder.Configuration, deploymentName);

builder.Services.AddRoutedChatClient(new Uri(endpointSetting), credential, deploymentName, isDevelopment);

/////// AGENTS AND WORKFLOWS ///////

builder.AddReviewAgents(guardrails);

/////// DEVUI (development) OR JWT AUTH (everything else) ///////

if (isDevelopment)
{
    builder.AddDevUI();
    builder.Services.AddOpenAIResponses();
    builder.Services.AddOpenAIConversations();
}
else
{
    builder.Services.AddJwtAuthentication(builder.Configuration);
}

var app = builder.Build();

app.MapDefaultEndpoints();

if (isDevelopment)
{
    app.MapDevUI();
    app.MapOpenAIResponses();
    app.MapOpenAIConversations();
}
else
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseRateLimiter();
app.UseGuardrailErrors();
app.UseModelSelection(allowedDeployments);

// Dedicated Chat Completions endpoints for the single agent and the full board.
app.MapOpenAIChatCompletions(
    app.Services.GetRequiredKeyedService<AIAgent>(AgentNames.Architecture),
    "/agents/architecture/v1/chat/completions");
app.MapOpenAIChatCompletions(
    app.Services.GetRequiredKeyedService<AIAgent>(AgentNames.ReviewBoard),
    "/agents/review-board/v1/chat/completions");

app.Run();
