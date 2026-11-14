using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewDomain>))]
public enum ReviewDomain
{
    Architecture,
    Security,
    ReliabilityScalability,
    CostOperations,
}

[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity
{
    Low,
    Medium,
    High,
    Critical,
}

[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]
public enum Priority
{
    Low,
    Medium,
    High,
}

public sealed record Finding(
    [property: Description("Stable id prefixed by the domain, e.g. SEC-1.")] string Id,
    string Title,
    Severity Severity,
    [property: Description("Why this is a concern for the proposal, in one or two sentences.")] string Rationale);

public sealed record ReviewRecommendation(
    string Action,
    Priority Priority,
    [property: Description("Ids of the findings this recommendation addresses.")] string[] FindingIds);

public sealed record SpecialistReview(
    ReviewDomain Domain,
    string[] Strengths,
    Finding[] Findings,
    ReviewRecommendation[] Recommendations);

public sealed record TradeOff(string Decision, string Gain, string Cost);

public sealed record Risk(
    [property: Description("Stable id such as R1.")] string Id,
    string Description,
    Severity Severity,
    [property: Description("Specialist domains that raised this risk.")] ReviewDomain[] RaisedBy);

public sealed record Mitigation(
    [property: Description("Id of the risk this mitigation addresses.")] string RiskId,
    string Action);

public sealed record ConflictResolution(
    string Topic,
    [property: Description("Each specialist's position, naming the specialist.")] string[] Positions,
    string Resolution,
    string Why);

public sealed record ArchitectureRecommendation(
    string ProposedDesign,
    string Rationale,
    TradeOff[] TradeOffs,
    Risk[] Risks,
    Mitigation[] Mitigations,
    string[] OpenQuestions,
    [property: Description("Empty when the specialists did not disagree.")] ConflictResolution[] ConflictResolutions);

public static class ReviewContract
{
    public const string Version = "1.0";

    // Missing or null members fail deserialization, so a partial payload can never pass as a valid contract.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static ChatResponseFormat SpecialistFormat { get; } = ChatResponseFormat.ForJsonSchema<SpecialistReview>(
        Json, nameof(SpecialistReview), "A specialist's structured review of an architecture proposal.");

    public static ChatResponseFormat RecommendationFormat { get; } = ChatResponseFormat.ForJsonSchema<ArchitectureRecommendation>(
        Json, nameof(ArchitectureRecommendation), "The review board's synthesized architecture recommendation.");

    public static bool TryParse<T>(string? text, [NotNullWhen(true)] out T? value) where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException)
        {
        }

        return value is not null;
    }
}
