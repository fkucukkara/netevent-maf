using ArchitectureReviewer.ApiService.Guardrails;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace ArchitectureReviewer.ApiService.Setup;

internal static class RateLimitingSetup
{
    public static void AddGuardrailRateLimiting(this IServiceCollection services, GuardrailOptions guardrails)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => GetPartition(context, guardrails));
        });
    }

    private static RateLimitPartition<string> GetPartition(HttpContext context, GuardrailOptions guardrails)
    {
        bool isAgentCall = context.Request.Path.StartsWithSegments("/agents")
            && HttpMethods.IsPost(context.Request.Method);

        if (!isAgentCall)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var limiterOptions = new FixedWindowRateLimiterOptions
        {
            PermitLimit = guardrails.RequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        };

        return RateLimitPartition.GetFixedWindowLimiter(GetCallerId(context), _ => limiterOptions);
    }

    private static string GetCallerId(HttpContext context)
    {
        string? objectId = context.User.FindFirst("oid")?.Value;
        if (objectId is not null)
        {
            return objectId;
        }

        string? nameIdentifier = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (nameIdentifier is not null)
        {
            return nameIdentifier;
        }

        string? remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (remoteIp is not null)
        {
            return remoteIp;
        }

        return "anonymous";
    }
}
