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

    public static IServiceCollection AddCofferLogging(this IServiceCollection services)
    {
        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Coffer",
            "logs");
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
