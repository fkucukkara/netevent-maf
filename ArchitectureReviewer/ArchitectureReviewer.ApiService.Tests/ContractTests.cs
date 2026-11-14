using ArchitectureReviewer.ApiService.Contracts;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService.Tests;

public class ContractTests
{
    private static SpecialistReview Review(ReviewDomain domain = ReviewDomain.Security, string recommendationFinding = "SEC-1") => new(
        domain,
        ["Managed identity everywhere"],
        [new Finding("SEC-1", "Public ingress", Severity.High, "All traffic enters through one public endpoint.")],
        [new ReviewRecommendation("Add WAF", Priority.High, [recommendationFinding])]);

    private static ArchitectureRecommendation Recommendation(string riskId = "R1", string mitigationRiskId = "R1", ReviewDomain raisedBy = ReviewDomain.Security) => new(
        "Hub-spoke with AKS",
        "Fits the stated goals.",
        [new TradeOff("Microservices", "Independent scaling", "Operational overhead")],
        [new Risk(riskId, "Single public entry point", Severity.High, [raisedBy])],
        [new Mitigation(mitigationRiskId, "Front with WAF")],
        ["What is the RTO?"],
        []);

    [Fact]
    public void SpecialistReview_round_trips_as_json()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Review(), ReviewContract.Json);

        Assert.True(ReviewContract.TryParse<SpecialistReview>(json, out var parsed));
        Assert.Equal(ReviewDomain.Security, parsed.Domain);
        Assert.Contains("\"severity\":\"High\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("## Markdown instead of JSON")]
    [InlineData("{\"domain\":\"Security\"}")]
    public void TryParse_rejects_incomplete_or_non_json_output(string text) =>
        Assert.False(ReviewContract.TryParse<SpecialistReview>(text, out _));

    [Fact]
    public void Recommendation_json_is_not_accepted_as_a_specialist_review()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Recommendation(), ReviewContract.Json);

        Assert.False(ReviewContract.TryParse<SpecialistReview>(json, out _));
        Assert.True(ReviewContract.TryParse<ArchitectureRecommendation>(json, out _));
    }

    [Fact]
    public void Valid_specialist_review_has_no_errors() =>
        Assert.Empty(ReviewValidator.Validate(Review()));

    [Fact]
    public void Specialist_recommendation_must_reference_an_existing_finding() =>
        Assert.Contains(ReviewValidator.Validate(Review(recommendationFinding: "SEC-9")), e => e.Contains("SEC-9"));

    [Fact]
    public void Valid_recommendation_has_no_errors() =>
        Assert.Empty(ReviewValidator.Validate(Recommendation(), [ReviewDomain.Security]));

    [Fact]
    public void Mitigation_must_reference_an_existing_risk()
    {
        var errors = ReviewValidator.Validate(Recommendation(mitigationRiskId: "R7"), []);

        Assert.Contains(errors, e => e.Contains("unknown risk 'R7'"));
        Assert.Contains(errors, e => e.Contains("'R1' has no mitigation"));
    }

    [Fact]
    public void Risk_cannot_be_attributed_to_a_specialist_that_did_not_review()
    {
        var errors = ReviewValidator.Validate(Recommendation(raisedBy: ReviewDomain.CostOperations), [ReviewDomain.Security]);

        Assert.Contains(errors, e => e.Contains("CostOperations"));
    }

    [Fact]
    public void RenderBoard_includes_required_sections_and_specialist_findings()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.Assistant, System.Text.Json.JsonSerializer.Serialize(Review(), ReviewContract.Json)),
            new ChatMessage(ChatRole.Assistant, System.Text.Json.JsonSerializer.Serialize(Recommendation(), ReviewContract.Json)),
        };

        var markdown = ReviewRenderer.RenderBoard(messages);

        Assert.NotNull(markdown);
        foreach (var heading in new[] { "## Proposed Design", "## Rationale", "## Trade-offs", "## Risks", "## Mitigations", "## Open Questions", "## Specialist Findings" })
        {
            Assert.Contains(heading, markdown);
        }
    }

    [Fact]
    public void RenderBoard_returns_null_without_a_recommendation() =>
        Assert.Null(ReviewRenderer.RenderBoard([new ChatMessage(ChatRole.Assistant, "plain text")]));
}
