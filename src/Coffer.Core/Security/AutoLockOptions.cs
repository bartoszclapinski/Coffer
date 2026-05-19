namespace Coffer.Core.Security;

/// <summary>
/// The idle threshold that drives <see cref="IAutoLockMonitor"/>. Registered as a
/// Singleton in <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>;
/// Sprint 7+'s Settings UI will replace the registration with a configurable source.
/// </summary>
public sealed record AutoLockOptions(TimeSpan IdleTimeout)
{
    public static AutoLockOptions Default { get; } = new(TimeSpan.FromMinutes(15));
}
