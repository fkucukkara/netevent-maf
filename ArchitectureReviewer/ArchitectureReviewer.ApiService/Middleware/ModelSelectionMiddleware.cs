using System.Text.Json;

namespace ArchitectureReviewer.ApiService.Middleware;

// Maps the Chat Completions "model" field to an allowed deployment for this request.
internal static class ModelSelectionMiddleware
{
    public static void UseModelSelection(this WebApplication app, HashSet<string> allowedDeployments)
    {
        app.Use(async (context, next) =>
        {
            bool isAgentCall = HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.StartsWithSegments("/agents");

            if (isAgentCall)
            {
                string? model = await ReadModelAsync(context.Request, context.RequestAborted);

                if (!string.IsNullOrWhiteSpace(model))
                {
                    if (!allowedDeployments.Contains(model))
                    {
                        await WriteModelNotAllowedAsync(context, model, allowedDeployments);
                        return;
                    }

                    context.Items[ModelRoutingChatClient.DeploymentItemKey] = model;
                }
            }

            await next();
        });
    }

    // Reads the body without consuming it so the endpoint can read it again.
    private static async Task<string?> ReadModelAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            return GetModelProperty(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static string? GetModelProperty(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("model", out JsonElement model))
        {
            return null;
        }

        if (model.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return model.GetString();
    }

    private static Task WriteModelNotAllowedAsync(HttpContext context, string model, HashSet<string> allowedDeployments)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        string message = $"Model '{model}' is not available. Allowed: {string.Join(", ", allowedDeployments)}.";
        return context.Response.WriteAsJsonAsync(new
        {
            error = new { message, type = "invalid_request_error", param = "model" },
        });
    }
}
