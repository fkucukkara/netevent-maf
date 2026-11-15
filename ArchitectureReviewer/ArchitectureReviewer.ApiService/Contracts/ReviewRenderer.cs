using System.Text;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService.Contracts;

public static class ReviewRenderer
{
    /// <summary>Renders the board's final answer; null when no valid recommendation is present.</summary>
    public static string? RenderBoard(IReadOnlyList<ChatMessage> messages)
    {
        var (reviews, recommendation) = Extract(messages);
        if (recommendation is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        Render(sb, recommendation);

        if (reviews.Count > 0)
        {
            sb.AppendLine("## Specialist Findings");
            foreach (var review in reviews.Values.OrderBy(r => r.Domain))
            {
                sb.AppendLine().AppendLine($"### {review.Domain}");
                foreach (var finding in review.Findings)
                {
                    sb.AppendLine($"- **{finding.Id}** ({finding.Severity}): {finding.Title}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Renders a single specialist's review; null when the output is not a valid review.</summary>
    public static string? RenderSpecialist(IReadOnlyList<ChatMessage> messages)
    {
        var (reviews, _) = Extract(messages);
        if (reviews.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        RenderReview(sb, reviews.Values.Last());
        return sb.ToString().TrimEnd();
    }

    /// <summary>Renders every specialist review in the panel's output; null when none is valid.</summary>
    public static string? RenderPanel(IReadOnlyList<ChatMessage> messages)
    {
        var (reviews, _) = Extract(messages);
        if (reviews.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var review in reviews.Values.OrderBy(r => r.Domain))
        {
            RenderReview(sb, review);
        }

        return sb.ToString().TrimEnd();
    }

    private static void RenderReview(StringBuilder sb, SpecialistReview review)
    {
        sb.AppendLine($"## {review.Domain} Review").AppendLine();
        sb.AppendLine("### Strengths");
        AppendList(sb, review.Strengths);
        sb.AppendLine("### Concerns");
        AppendList(sb, review.Findings.Select(f => $"**{f.Id}** ({f.Severity}): {f.Title}. {f.Rationale}"));
        sb.AppendLine("### Recommendations");
        AppendList(sb, review.Recommendations.Select(r =>
            $"{r.Action} (priority {r.Priority}{(r.FindingIds.Length > 0 ? $", addresses {string.Join(", ", r.FindingIds)}" : "")})"));
    }

    public static (Dictionary<ReviewDomain, SpecialistReview> Reviews, ArchitectureRecommendation? Recommendation) Extract(
        IEnumerable<ChatMessage> messages)
    {
        var reviews = new Dictionary<ReviewDomain, SpecialistReview>();
        ArchitectureRecommendation? recommendation = null;

        foreach (var message in messages.Where(m => m.Role == ChatRole.Assistant))
        {
            if (ReviewContract.TryParse<ArchitectureRecommendation>(message.Text, out var rec))
            {
                recommendation = rec;
            }
            else if (ReviewContract.TryParse<SpecialistReview>(message.Text, out var review))
            {
                reviews[review.Domain] = review;
            }
        }

        return (reviews, recommendation);
    }

    private static void Render(StringBuilder sb, ArchitectureRecommendation r)
    {
        sb.AppendLine("## Proposed Design").AppendLine().AppendLine(r.ProposedDesign).AppendLine();
        sb.AppendLine("## Rationale").AppendLine().AppendLine(r.Rationale).AppendLine();

        sb.AppendLine("## Trade-offs").AppendLine();
        AppendList(sb, r.TradeOffs.Select(t => $"**{t.Decision}**: gains {t.Gain}; costs {t.Cost}"));

        sb.AppendLine("## Risks").AppendLine();
        AppendList(sb, r.Risks.Select(x => $"**{x.Id}** ({x.Severity}, raised by {string.Join(", ", x.RaisedBy)}): {x.Description}"));

        sb.AppendLine("## Mitigations").AppendLine();
        AppendList(sb, r.Mitigations.Select(m => $"**{m.RiskId}**: {m.Action}"));

        sb.AppendLine("## Open Questions").AppendLine();
        AppendList(sb, r.OpenQuestions);

        if (r.ConflictResolutions.Length > 0)
        {
            sb.AppendLine("## Conflicting Opinions").AppendLine();
            foreach (var c in r.ConflictResolutions)
            {
                sb.AppendLine($"**{c.Topic}**");
                foreach (var position in c.Positions)
                {
                    sb.AppendLine($"- {position}");
                }

                sb.AppendLine().AppendLine($"Resolution: {c.Resolution} Why: {c.Why}").AppendLine();
            }
        }
    }

    private static void AppendList(StringBuilder sb, IEnumerable<string> items)
    {
        var any = false;
        foreach (var item in items)
        {
            sb.AppendLine($"- {item}");
            any = true;
        }

        if (!any)
        {
            sb.AppendLine("- None");
        }

        sb.AppendLine();
    }
}
