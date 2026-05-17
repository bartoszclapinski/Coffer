namespace Coffer.Infrastructure.Persistence;

public class SchemaInfoEntry
{
    public int Id { get; set; }

    public string Version { get; set; } = "";

    public DateTime MigratedAt { get; set; }

    public string AppVersion { get; set; } = "";
}
