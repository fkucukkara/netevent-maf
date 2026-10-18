using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-5-mini";

// AzureCliCredential is for local development only (requires `az login`).
IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(configure: c => c.EnableSensitiveData = true)
    .Build();
builder.Services.AddSingleton(chatClient);

/////// SPECIALIST AGENTS ///////

AddSpecialist(
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

builder.AddWorkflow("ArchitectureReviewBoard", (sp, name) =>
{
    var reviewPanel = sp.GetRequiredKeyedService<AIAgent>("ReviewPanel");
    var synthesisAgent = sp.GetRequiredKeyedService<AIAgent>("SynthesisAgent");

    return AgentWorkflowBuilder.BuildSequential(name, reviewPanel, synthesisAgent);
}).AddAsAIAgent();

builder.AddDevUI();
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapDevUI();
app.MapOpenAIResponses();
app.MapOpenAIConversations();

app.Run();

void AddSpecialist(string agentName, string description, string instructions) =>
    builder.AddAIAgent(agentName, (sp, name) => new ChatClientAgent(
        chatClient: sp.GetRequiredService<IChatClient>(),
        instructions: instructions,
        name: name,
        description: description,
        tools: null,
        loggerFactory: sp.GetService<ILoggerFactory>(),
        services: sp));

