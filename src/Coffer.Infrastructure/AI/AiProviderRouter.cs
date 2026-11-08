using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// The <see cref="IAiProvider"/> every AI consumer gets from DI (Sprint 31): it dispatches each request
/// to the registered <see cref="IAiProviderBackend"/> whose <see cref="IAiProviderBackend.ProviderName"/>
/// matches the request's <c>Provider</c> (case-insensitive). It adds no behaviour of its own — no retry,
/// no fallback to another provider (a silent fallback could move a prompt meant for a local model to a
/// remote vendor). A request naming a provider that is not registered throws
/// <see cref="UnknownAiProviderException"/> before anything is sent.
/// </summary>
public sealed class AiProviderRouter : IAiProvider
{
    private readonly Dictionary<string, IAiProviderBackend> _backends;

    public AiProviderRouter(IEnumerable<IAiProviderBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);

        _backends = new Dictionary<string, IAiProviderBackend>(StringComparer.OrdinalIgnoreCase);
        foreach (var backend in backends)
        {
            if (!_backends.TryAdd(backend.ProviderName, backend))
            {
                throw new ArgumentException(
                    $"Two AI providers are registered as '{backend.ProviderName}'.", nameof(backends));
            }
        }
    }

    public Task<AiResult<string>> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BackendFor(request.Provider).CompleteAsync(request, ct);
    }

    public Task<AiResult<TResult>> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BackendFor(request.Provider).CompleteJsonAsync<TResult>(request, ct);
    }

    public Task<AiResult<AiToolTurn>> CompleteWithToolsAsync(AiToolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BackendFor(request.Provider).CompleteWithToolsAsync(request, ct);
    }

    public IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BackendFor(request.Provider).StreamAsync(request, ct);
    }

    private IAiProviderBackend BackendFor(string provider) =>
        !string.IsNullOrEmpty(provider) && _backends.TryGetValue(provider, out var backend)
            ? backend
            : throw new UnknownAiProviderException(provider);
}
