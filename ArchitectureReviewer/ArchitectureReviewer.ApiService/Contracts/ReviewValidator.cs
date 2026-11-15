namespace ArchitectureReviewer.ApiService.Contracts;

public static class ReviewValidator
{
    public static List<string> Validate(SpecialistReview review)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in review.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Id) || !ids.Add(finding.Id))
            {
                errors.Add($"Finding id '{finding.Id}' is empty or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(finding.Title) || string.IsNullOrWhiteSpace(finding.Rationale))
            {
                errors.Add($"Finding '{finding.Id}' needs a title and a rationale.");
            }
        }

        foreach (var recommendation in review.Recommendations)
        {
            if (string.IsNullOrWhiteSpace(recommendation.Action))
            {
                errors.Add("A recommendation has an empty action.");
            }

            foreach (var findingId in recommendation.FindingIds.Where(id => !ids.Contains(id)))
            {
                errors.Add($"Recommendation '{recommendation.Action}' references unknown finding '{findingId}'.");
            }
        }

        return errors;
    }

    /// <param name="reviewed">Domains that actually reviewed the proposal; empty skips the attribution check.</param>
    public static List<string> Validate(ArchitectureRecommendation recommendation, IReadOnlyCollection<ReviewDomain> reviewed)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(recommendation.ProposedDesign))
        {
            errors.Add("ProposedDesign is empty.");
        }

        if (string.IsNullOrWhiteSpace(recommendation.Rationale))
        {
            errors.Add("Rationale is empty.");
        }

        if (recommendation.TradeOffs.Length == 0)
        {
            errors.Add("TradeOffs must contain at least one entry.");
        }

        if (recommendation.Risks.Length == 0)
        {
            errors.Add("Risks must contain at least one entry.");
        }

        var riskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var risk in recommendation.Risks)
        {
            if (string.IsNullOrWhiteSpace(risk.Id) || !riskIds.Add(risk.Id))
            {
                errors.Add($"Risk id '{risk.Id}' is empty or duplicated.");
            }

            if (risk.RaisedBy.Length == 0)
            {
                errors.Add($"Risk '{risk.Id}' must name the specialists that raised it.");
            }

            if (reviewed.Count > 0)
            {
                foreach (var domain in risk.RaisedBy.Where(d => !reviewed.Contains(d)))
                {
                    errors.Add($"Risk '{risk.Id}' is attributed to {domain}, which did not review the proposal.");
                }
            }
        }

        foreach (var mitigation in recommendation.Mitigations.Where(m => !riskIds.Contains(m.RiskId)))
        {
            errors.Add($"Mitigation '{mitigation.Action}' references unknown risk '{mitigation.RiskId}'.");
        }

        foreach (var risk in recommendation.Risks.Where(r =>
                     !recommendation.Mitigations.Any(m => string.Equals(m.RiskId, r.Id, StringComparison.OrdinalIgnoreCase))))
        {
            errors.Add($"Risk '{risk.Id}' has no mitigation.");
        }

        return errors;
    }
}
