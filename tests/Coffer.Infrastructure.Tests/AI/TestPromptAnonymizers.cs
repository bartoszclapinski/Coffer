using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>An anonymiser whose sessions change nothing, for tests that are not about privacy.</summary>
public sealed class PassthroughPromptAnonymizer : IPromptAnonymizer
{
    public Task<IPromptRedactionSession> BeginSessionAsync(CancellationToken ct) =>
        Task.FromResult<IPromptRedactionSession>(new Session());

    private sealed class Session : IPromptRedactionSession
    {
        public bool HasOwnerNames => true;

        public string Redact(string text) => text;

        public string RedactDocument(string text) => text;

        public string Restore(string text) => text;
    }
}

/// <summary>An anonymiser that prefixes every redacted text with <c>ANON:</c>, proving the path redacts.</summary>
public sealed class MarkingPromptAnonymizer : IPromptAnonymizer
{
    public Task<IPromptRedactionSession> BeginSessionAsync(CancellationToken ct) =>
        Task.FromResult<IPromptRedactionSession>(new Session());

    private sealed class Session : IPromptRedactionSession
    {
        public bool HasOwnerNames => true;

        public string Redact(string text) => "ANON:" + text;

        public string RedactDocument(string text) => "ANON:" + text;

        public string Restore(string text) => text;
    }
}

/// <summary>AI settings with defaults and a settable owner-identity string.</summary>
public sealed class StubAiSettings : IAiSettings
{
    public string? OwnerNames { get; set; }

    public Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct) => Task.FromResult(AiDefaults.MonthlyCapPln);

    public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct) => Task.CompletedTask;

    public Task<string> GetActiveProviderAsync(CancellationToken ct) => Task.FromResult(AiDefaults.ClaudeProvider);

    public Task SetActiveProviderAsync(string provider, CancellationToken ct) => Task.CompletedTask;

    public Task<string> GetCategorizationModelAsync(CancellationToken ct) => Task.FromResult(AiDefaults.CategorizationModel);

    public Task SetCategorizationModelAsync(string model, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct) => Task.FromResult(true);

    public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;

    public Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct) => Task.FromResult(OwnerNames);

    public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A counterparty source over a fixed list, standing in for the stored-description scan.</summary>
public sealed class StaticCounterpartyNameSource : ICounterpartyNameSource
{
    private readonly IReadOnlyCollection<string> _names;

    public StaticCounterpartyNameSource(params string[] names) => _names = names;

    public Task<IReadOnlyCollection<string>> GetKnownCounterpartyNamesAsync(CancellationToken ct) =>
        Task.FromResult(_names);
}

/// <summary>The production redaction rules over a fixed set of owner and counterparty names (no DB).</summary>
public sealed class FixedPromptAnonymizer : IPromptAnonymizer
{
    private readonly IReadOnlyList<string> _ownerNames;
    private readonly IReadOnlyList<string> _counterpartyNames;

    public FixedPromptAnonymizer(IReadOnlyList<string>? ownerNames = null, IReadOnlyList<string>? counterpartyNames = null)
    {
        _ownerNames = ownerNames ?? [];
        _counterpartyNames = counterpartyNames ?? [];
    }

    public int SessionsOpened { get; private set; }

    public PromptRedactor? LastSession { get; private set; }

    public Task<IPromptRedactionSession> BeginSessionAsync(CancellationToken ct)
    {
        SessionsOpened++;
        LastSession = new PromptRedactor(_ownerNames, _counterpartyNames);
        return Task.FromResult<IPromptRedactionSession>(LastSession);
    }
}
