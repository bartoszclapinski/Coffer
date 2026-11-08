using Coffer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Coffer.Infrastructure.Logging;

public static class SerilogConfiguration
{
    private static readonly string[] _sensitivePropertyNames =
    {
        "Password",
        "MasterKey",
        "Dek",
        "Mnemonic",
        "Seed",
        "ApiKey",
        "RefreshToken"
    };

    /// <summary>
    /// Configures Serilog with its rolling log files under <see cref="CofferDataRoot.Current"/>'s <c>logs</c>
    /// folder — the same root as the vault, so a Debug run (or a <c>COFFER_DATA_DIR</c> override) never writes
    /// into the real vault's logs.
    /// </summary>
    public static IServiceCollection AddCofferLogging(this IServiceCollection services) =>
        services.AddCofferLogging(CofferDataRoot.Current);

    /// <summary>Configures Serilog with its rolling log files under <paramref name="dataRoot"/>'s <c>logs</c> folder.</summary>
    public static IServiceCollection AddCofferLogging(this IServiceCollection services, string dataRoot)
    {
        var logsDirectory = GetLogsDirectory(dataRoot);
        Directory.CreateDirectory(logsDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(GetMinimumLevel())
            .Enrich.FromLogContext()
            .Filter.ByExcluding(IsSensitiveLogEvent)
#if DEBUG
            .WriteTo.Console()
#endif
            .WriteTo.File(
                Path.Combine(logsDirectory, "coffer-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 30)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        return services;
    }

    /// <summary>The folder the rolling log files go to for a given data root.</summary>
    public static string GetLogsDirectory(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, "logs");
    }

    private static bool IsSensitiveLogEvent(Serilog.Events.LogEvent logEvent) =>
        logEvent.Properties.Keys.Any(key =>
            _sensitivePropertyNames.Contains(key, StringComparer.OrdinalIgnoreCase));

    private static LogEventLevel GetMinimumLevel()
    {
#if DEBUG
        return LogEventLevel.Debug;
#else
        return LogEventLevel.Information;
#endif
    }
}
