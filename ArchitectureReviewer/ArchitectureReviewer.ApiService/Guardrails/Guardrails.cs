using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace ArchitectureReviewer.ApiService.Guardrails;

public sealed class GuardrailOptions
{
    public int MaxInputChars { get; set; } = 8_000;
    public long MaxRequestBodyBytes { get; set; } = 64 * 1024;
    public bool EnableClassifier { get; set; } = true;
    public int SpecialistMaxOutputTokens { get; set; } = 4_000;
    public int SynthesisMaxOutputTokens { get; set; } = 6_000;

    // Unset by default: some deployments (reasoning models) reject a temperature override.
    public float? Temperature { get; set; }
    public int RequestsPerMinute { get; set; } = 10;
}

/// <summary>A guardrail decision that maps to an OpenAI-style HTTP error.</summary>
public sealed class GuardrailException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public static class GuardrailMetrics
{
    public const string MeterName = "ArchitectureReviewer.Guardrails";

    private static readonly Counter<long> Decisions =
        new Meter(MeterName).CreateCounter<long>("guardrail.decisions", description: "Guardrail outcomes by guard.");

    public static void Record(string guard, string outcome) =>
        Decisions.Add(1, new KeyValuePair<string, object?>("guard", guard), new KeyValuePair<string, object?>("outcome", outcome));
}

public static class Redactor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", Options, Timeout), "[REDACTED_PRIVATE_KEY]"),
        (new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", Options, Timeout), "[REDACTED_JWT]"),
        (new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", Options, Timeout), "[REDACTED_AWS_KEY]"),
        (new(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", Options, Timeout), "[REDACTED_GITHUB_TOKEN]"),
        (new(@"\bsk-[A-Za-z0-9_-]{20,}", Options, Timeout), "[REDACTED_API_KEY]"),
        (new(@"\b(AccountKey|SharedAccessKey|Password|Pwd|ClientSecret)\s*=\s*[^;\s""']+", Options | RegexOptions.IgnoreCase, Timeout), "$1=[REDACTED]"),
        (new(@"([?&]sig=)[^&\s""']+", Options | RegexOptions.IgnoreCase, Timeout), "$1[REDACTED]"),
        (new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", Options, Timeout), "[REDACTED_EMAIL]"),
    ];

    public static (string Text, int Count) Redact(string text)
    {
        var count = 0;
        foreach (var (pattern, replacement) in Rules)
        {
            text = pattern.Replace(text, m =>
            {
                count++;
                return m.Result(replacement);
            });
        }

        return (text, count);
    }
}
