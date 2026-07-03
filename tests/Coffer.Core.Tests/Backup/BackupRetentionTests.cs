using Coffer.Core.Backup;
using FluentAssertions;

namespace Coffer.Core.Tests.Backup;

public class BackupRetentionTests
{
    [Fact]
    public void SelectExpired_DropsOlderThanWindow_KeepsBoundaryAndNewer()
    {
        var today = new DateOnly(2026, 7, 31);
        string[] names =
        [
            "coffer-2026-06-30.db", // 31 days back — expired
            "coffer-2026-07-01.db", // exactly 30 days back — kept (boundary)
            "coffer-2026-07-31.db", // today — kept
            "garbage.txt",          // unparsable — ignored
            "coffer-not-a-date.db", // unparsable — ignored
        ];

        var expired = BackupRetention.SelectExpired(names, today, keepDays: 30, BackupRetention.ParseDailyDate);

        expired.Should().ContainSingle().Which.Should().Be("coffer-2026-06-30.db");
    }

    [Theory]
    [InlineData("coffer-2026-07-03.db", true)]
    [InlineData("coffer-2026-07-03.db-wal", false)] // side-file, not a primary snapshot
    [InlineData("coffer-20260703T101500Z.db", false)] // pre-migration format
    [InlineData("nope.db", false)]
    public void ParseDailyDate_MatchesOnlyDailyFormat(string name, bool parses)
    {
        BackupRetention.ParseDailyDate(name).HasValue.Should().Be(parses);
    }

    [Fact]
    public void ParsePreMigrationDate_ReadsUtcTimestampDay()
    {
        BackupRetention.ParsePreMigrationDate("coffer-20260703T101500Z.db")
            .Should().Be(new DateOnly(2026, 7, 3));
        BackupRetention.ParsePreMigrationDate("coffer-2026-07-03.db").Should().BeNull();
    }
}
