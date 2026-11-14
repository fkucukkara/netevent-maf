using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace ArchitectureReviewer.ApiService;

/// <summary>Routes each call to the deployment chosen for the current HTTP request, or the default.</summary>
internal sealed class ModelRoutingChatClient(
    Func<string, IChatClient> createClient,
    IHttpContextAccessor accessor,
    string defaultDeployment) : IChatClient
{
    public const string DeploymentItemKey = "ArchitectureReviewer.Deployment";

    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    private IChatClient Resolve()
    {
        var deployment = accessor.HttpContext?.Items[DeploymentItemKey] as string ?? defaultDeployment;
        return _clients.GetOrAdd(deployment, createClient);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Resolve().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Resolve().GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _clients.GetOrAdd(defaultDeployment, createClient).GetService(serviceType, serviceKey);

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }
}
