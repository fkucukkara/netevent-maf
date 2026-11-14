using System.Text.Json;
using ArchitectureReviewer.ApiService;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-6-luna";

var isDevelopment = builder.Environment.IsDevelopment();

// AzureCliCredential is for local development only (requires `az login`).
var clientId = builder.Configuration["AZURE_CLIENT_ID"];
TokenCredential credential = isDevelopment
    ? new AzureCliCredential()
    : new ManagedIdentityCredential(string.IsNullOrEmpty(clientId)
        ? ManagedIdentityId.SystemAssigned
        : ManagedIdentityId.FromUserAssignedClientId(clientId));

// Deployments a caller may select through the request's "model" field (comma-separated).
var allowedDeployments = new HashSet<string>(
    (builder.Configuration["AZURE_OPENAI_ALLOWED_DEPLOYMENTS"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Append(deploymentName),
    StringComparer.OrdinalIgnoreCase);

var azureOpenAI = new AzureOpenAIClient(new Uri(endpoint), credential);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IChatClient>(sp =>
    new ModelRoutingChatClient(
        // Telemetry wraps each deployment client so spans report the routed model, not the default.
        deployment => azureOpenAI.GetChatClient(deployment).AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(configure: c => c.EnableSensitiveData = isDevelopment)
            .Build(),
        sp.GetRequiredService<IHttpContextAccessor>(),
        deploymentName));

/////// SPECIALIST AGENTS ///////

var architectureAgent = AddSpecialist(
    "ArchitectureAgent",
    "Reviews structure, service boundaries, coupling, data ownership and integration patterns.",
    """
    You are a principal software architect on an architecture review board.
    Review the proposed architecture for structure and design quality: service boundaries, coupling and cohesion,
    data ownership, integration and communication patterns, and fit with the stated goals.
    Stay within this scope; other reviewers cover security, reliability/scalability and cost/operations.
    Respond with: Strengths, Concerns, Recommendations. Keep it under 250 words.
    """);

AddSpecialist(
    "SecurityAgent",
    "Reviews identity, network exposure, data protection, secrets and compliance.",
    """
    You are a cloud security architect on an architecture review board.
    Review the proposed architecture for security: identity and access, network exposure and segmentation,
    data protection in transit and at rest, secrets management, supply chain, and compliance concerns.
    Stay within this scope; other reviewers cover design, reliability/scalability and cost/operations.
    Respond with: Strengths, Concerns, Recommendations. Keep it under 250 words.
    """);

AddSpecialist(
    "ReliabilityScalabilityAgent",
    "Reviews availability, resilience, failure modes, scaling and disaster recovery.",
    """
    You are a reliability and scalability engineer on an architecture review board.
    Review the proposed architecture for availability targets, failure modes, resilience patterns, data consistency,
    scaling behavior, bottlenecks, and disaster recovery across regions.
    Stay within this scope; other reviewers cover design, security and cost/operations.
    Respond with: Strengths, Concerns, Recommendations. Keep it under 250 words.
    """);

AddSpecialist(
    "CostOperationsAgent",
    "Reviews cost drivers, operational complexity, observability and team readiness.",
    """
    You are a FinOps and operations lead on an architecture review board.
    Review the proposed architecture for cost drivers, operational complexity, observability, deployment and release
    process, on-call burden, and the skills the team needs to run it.
    Stay within this scope; other reviewers cover design, security and reliability/scalability.
    Respond with: Strengths, Concerns, Recommendations. Keep it under 250 words.
    """);

/////// CONCURRENT REVIEW PANEL ///////

builder.AddWorkflow("ReviewPanel", (sp, name) =>
{
    var specialists = new[]
    {
        "ArchitectureAgent",
        "SecurityAgent",
        "ReliabilityScalabilityAgent",
        "CostOperationsAgent",
    }.Select(n => sp.GetRequiredKeyedService<AIAgent>(n));

    return AgentWorkflowBuilder.BuildConcurrent(name, specialists);
}).AddAsAIAgent();

/////// SYNTHESIS AGENT ///////

builder.AddAIAgent("SynthesisAgent", (sp, name) => new ChatClientAgent(
    chatClient: sp.GetRequiredService<IChatClient>(),
    instructions:
        """
        You are the chair of an architecture review board.
        The conversation contains the original architecture proposal followed by independent reviews from
        the Architecture, Security, Reliability/Scalability and Cost/Operations specialists.
        Synthesize them into one recommendation, using exactly these markdown sections:
        ## Proposed Design
        ## Rationale
        ## Trade-offs
        ## Risks
        ## Mitigations
        ## Open Questions
        Where specialists disagree, state the conflict and how you resolved it, and why.
        Do not introduce concerns that none of the specialists raised unless they are clearly implied by the proposal.
        """,
    name: name,
    description: "Synthesizes specialist reviews into a structured architecture recommendation.",
    tools: null,
    loggerFactory: sp.GetService<ILoggerFactory>(),
    services: sp));

/////// SEQUENTIAL BOARD: ReviewPanel -> SynthesisAgent ///////

var reviewBoard = builder.AddWorkflow("ArchitectureReviewBoard", (sp, name) =>
{
    var reviewPanel = sp.GetRequiredKeyedService<AIAgent>("ReviewPanel");
    var synthesisAgent = sp.GetRequiredKeyedService<AIAgent>("SynthesisAgent");

    return AgentWorkflowBuilder.BuildSequential(name, reviewPanel, synthesisAgent);
}).AddAsAIAgent();

// DevUI and the stateful Responses/Conversations endpoints are Development-only.
if (isDevelopment)
{
    builder.AddDevUI();
    builder.Services.AddOpenAIResponses();
    builder.Services.AddOpenAIConversations();
}
else
{
    var tenantId = builder.Configuration["AzureAd:TenantId"]
        ?? throw new InvalidOperationException("AzureAd:TenantId is not set.");
    var audience = builder.Configuration["AzureAd:Audience"]
        ?? throw new InvalidOperationException("AzureAd:Audience is not set.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            options.Audience = audience;
        });
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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

// Map the Chat Completions "model" field to an allowed deployment for this request.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/agents"))
    {
        context.Request.EnableBuffering();
        string? model = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("model", out var m)
                && m.ValueKind == JsonValueKind.String)
            {
                model = m.GetString();
            }
        }
        catch (JsonException)
        {
        }
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(model))
        {
            if (!allowedDeployments.Contains(model))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        message = $"Model '{model}' is not available. Allowed: {string.Join(", ", allowedDeployments)}.",
                        type = "invalid_request_error",
                        param = "model",
                    },
                });
                return;
            }
            context.Items[ModelRoutingChatClient.DeploymentItemKey] = model;
        }
    }

    await next();
});

// Dedicated Chat Completions endpoints for the single agent and the full board.
app.MapOpenAIChatCompletions(architectureAgent, "/agents/architecture/v1/chat/completions");
app.MapOpenAIChatCompletions(reviewBoard, "/agents/review-board/v1/chat/completions");

app.Run();

IHostedAgentBuilder AddSpecialist(string agentName, string description, string instructions) =>
    builder.AddAIAgent(agentName, (sp, name) => new ChatClientAgent(
        chatClient: sp.GetRequiredService<IChatClient>(),
        instructions: instructions,
        name: name,
        description: description,
        tools: null,
        loggerFactory: sp.GetService<ILoggerFactory>(),
        services: sp));