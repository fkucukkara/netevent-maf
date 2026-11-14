using System.Runtime.CompilerServices;
using ArchitectureReviewer.ApiService.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService.Guardrails;

/// <summary>Guards the input, then replaces the inner agent's typed output with validated, redacted markdown.</summary>
public sealed class GuardedAgent(
    AIAgent inner,
    InputGuard guard,
    Func<IReadOnlyList<ChatMessage>, string?> render,
    string name,
    string description) : DelegatingAIAgent(inner)
{
    public override string? Name => name;

    public override string? Description => description;

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken)
    {
        var safeMessages = await guard.ApplyAsync(messages, cancellationToken);
        var response = await InnerAgent.RunAsync(safeMessages, session, options, cancellationToken);

        var markdown = render(response.Messages.ToList());
        if (markdown is null)
        {
            GuardrailMetrics.Record("output", "invalid_contract");
            throw new GuardrailException(502, "invalid_model_output", "The model output did not match the review contract.");
        }

        var (clean, redactions) = Redactor.Redact(markdown);
        if (redactions > 0)
        {
            GuardrailMetrics.Record("output", "redacted");
        }

        GuardrailMetrics.Record("output", "allowed");
        return new AgentResponse(new ChatMessage(ChatRole.Assistant, clean) { AuthorName = name });
    }

    // Output is only valid once the whole contract is checked, so streaming emits the final result in one update.
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }
}

/// <summary>Checks the inner agent's typed output with <paramref name="check"/> and retries once on violations.</summary>
public sealed class ContractValidatingAgent(
    AIAgent inner,
    Func<IReadOnlyList<ChatMessage>, AgentResponse, List<string>> check) : DelegatingAIAgent(inner)
{
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken)
    {
        var input = messages.ToList();

        var response = await InnerAgent.RunAsync(input, session, options, cancellationToken);
        var errors = check(input, response);
        if (errors.Count == 0)
        {
            GuardrailMetrics.Record("contract", "valid");
            return response;
        }

        GuardrailMetrics.Record("contract", "repair");
        var repair = new List<ChatMessage>(input);
        repair.AddRange(response.Messages);
        repair.Add(new ChatMessage(ChatRole.User,
            "Your previous output was invalid:\n- " + string.Join("\n- ", errors) + "\nReturn the corrected JSON only."));

        response = await InnerAgent.RunAsync(repair, null, options, cancellationToken);
        errors = check(input, response);
        if (errors.Count > 0)
        {
            GuardrailMetrics.Record("contract", "invalid");
            throw new GuardrailException(502, "invalid_model_output", $"{Name} did not satisfy the review contract: " + string.Join("; ", errors));
        }

        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }

    public static ContractValidatingAgent ForSpecialist(AIAgent inner) => new(inner, (_, response) =>
        ReviewContract.TryParse<SpecialistReview>(response.Text, out var review)
            ? ReviewValidator.Validate(review)
            : ["The output is not valid JSON matching the SpecialistReview schema."]);

    public static ContractValidatingAgent ForSynthesis(AIAgent inner) => new(inner, (input, response) =>
        ReviewContract.TryParse<ArchitectureRecommendation>(response.Text, out var recommendation)
            ? ReviewValidator.Validate(recommendation, ReviewRenderer.Extract(input).Reviews.Keys.ToList())
            : ["The output is not valid JSON matching the ArchitectureRecommendation schema."]);
}
