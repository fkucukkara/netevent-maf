using ArchitectureReviewer.ApiService.Guardrails;
using System.ClientModel;

namespace ArchitectureReviewer.ApiService.Middleware;

// Surfaces guardrail rejections and Azure content-filter blocks as OpenAI-style errors instead of 500s.
internal static class GuardrailErrorMiddleware
{
    public static void UseGuardrailErrors(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                bool canHandle = context.Request.Path.StartsWithSegments("/agents") && !context.Response.HasStarted;
                if (!canHandle || !TryMapError(exception, out int status, out string code, out string message))
                {
                    throw;
                }

                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Guardrails");
                logger.LogWarning("Request rejected with {Status} {Code}: {Message}", status, code, message);

                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { error = new { message, type = "invalid_request_error", code } });
            }
        });
    }

    private static bool TryMapError(Exception exception, out int status, out string code, out string message)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is GuardrailException guardrail)
            {
                status = guardrail.StatusCode;
                code = guardrail.Code;
                message = guardrail.Message;
                return true;
            }

            if (current is ClientResultException { Status: 400 } clientError
                && clientError.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            {
                status = StatusCodes.Status400BadRequest;
                code = "content_filter";
                message = "The request was blocked by the content filter.";
                return true;
            }
        }

        status = 0;
        code = "";
        message = "";
        return false;
    }
}
