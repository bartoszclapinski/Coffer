using Coffer.Core.Security;

namespace Coffer.Application.Tests.Fakes;

/// <summary>In-memory <see cref="ISeedRecoveryService"/>: records calls and can be made to throw.</summary>
internal sealed class FakeSeedRecoveryService : ISeedRecoveryService
{
    public bool Enabled { get; set; }

    public Exception? RecoverThrow { get; set; }

    public int RecoverCalls { get; private set; }

    public string? LastMnemonic { get; private set; }

    public string? LastNewPassword { get; private set; }

    public int EnableCalls { get; private set; }

    public string? LastEnableMnemonic { get; private set; }

    public Task<bool> IsSeedRecoveryEnabledAsync(CancellationToken ct) => Task.FromResult(Enabled);

    public Task RecoverWithSeedAsync(string mnemonic, string newMasterPassword, CancellationToken ct)
    {
        RecoverCalls++;
        LastMnemonic = mnemonic;
        LastNewPassword = newMasterPassword;
        return RecoverThrow is not null ? Task.FromException(RecoverThrow) : Task.CompletedTask;
    }

    public Task EnableSeedRecoveryAsync(string mnemonic, CancellationToken ct)
    {
        EnableCalls++;
        LastEnableMnemonic = mnemonic;
        Enabled = true;
        return Task.CompletedTask;
    }
}

/// <summary>Fake <see cref="ISeedManager"/> that treats one configured value as the only valid mnemonic.</summary>
internal sealed class FakeSeedManager : ISeedManager
{
    private readonly string _valid;

    public FakeSeedManager(string valid) => _valid = valid;

    public string GenerateMnemonic() => _valid;

    public bool IsValid(string mnemonic) => string.Equals(mnemonic, _valid, StringComparison.Ordinal);

    public Task<byte[]> DeriveRecoveryKeyAsync(string mnemonic, string passphrase, CancellationToken ct) =>
        Task.FromResult(new byte[32]);
}

/// <summary>Fake <see cref="IPasswordStrengthChecker"/> returning a fixed score.</summary>
internal sealed class FakePasswordStrengthChecker : IPasswordStrengthChecker
{
    private readonly int _score;

    public FakePasswordStrengthChecker(int score = 4) => _score = score;

    public PasswordStrength Evaluate(string password) => new(_score, Warning: null, Suggestions: []);
}
