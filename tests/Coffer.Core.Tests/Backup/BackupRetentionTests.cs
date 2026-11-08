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
    public void SelectExpiredSnapshots_AllOlderThanWindow_KeepsTheNewestSeven()
    {
        var today = new DateOnly(2026, 11, 8);
        // Ten snapshots, all far older than 30 days (e.g. the machine was off for months).
        var names = Enumerable.Range(1, 10)
            .Select(day => BackupRetention.DailyFileName(new DateOnly(2026, 5, day)))
            .ToList();

        var expired = BackupRetention.SelectExpiredSnapshots(names, today, keepDays: 30, keepNewest: 7);

        expired.Should().BeEquivalentTo(
            ["coffer-2026-05-01.db", "coffer-2026-05-02.db", "coffer-2026-05-03.db"],
            "the newest 7 survive regardless of age; only the 3 oldest go");
    }

    [Fact]
    public void SelectExpiredSnapshots_BeyondTheFloor_AppliesTheAgeRuleToDailyAndManual()
    {
        var today = new DateOnly(2026, 7, 31);
        var recent = Enumerable.Range(24, 7) // 7 snapshots within the window
            .Select(day => BackupRetention.DailyFileName(new DateOnly(2026, 7, day)));
        string[] older =
        [
            BackupRetention.ManualFileName(new DateOnly(2026, 7, 1), new TimeOnly(9, 30, 0)), // boundary — kept
            BackupRetention.ManualFileName(new DateOnly(2026, 6, 30), new TimeOnly(9, 30, 0)), // expired
            BackupRetention.DailyFileName(new DateOnly(2026, 6, 1)), // expired
            "coffer-20260601T101500Z.db", // pre-migration name — ignored
            "notes.db", // junk — ignored
        ];

        var expired = BackupRetention.SelectExpiredSnapshots(
            recent.Concat(older), today, keepDays: 30, keepNewest: 7);

        expired.Should().BeEquivalentTo(["coffer-2026-06-30-manual-093000.db", "coffer-2026-06-01.db"]);
    }

    [Fact]
    public void ManualFileName_IsDistinctFromTheDailyNameAndParsesBack()
    {
        var date = new DateOnly(2026, 7, 3);
        var manual = BackupRetention.ManualFileName(date, new TimeOnly(14, 5, 9));

        manual.Should().Be("coffer-2026-07-03-manual-140509.db");
        manual.Should().NotBe(BackupRetention.DailyFileName(date));
        BackupRetention.ParseSnapshot(manual).Should().Be(
            new SnapshotName(date, new TimeOnly(14, 5, 9), SnapshotKind.Manual));
        BackupRetention.ParseDailyDate(manual).Should().BeNull("a manual name is not a daily name");
    }

    [Theory]
    [InlineData("coffer-2026-07-03.db", true)]
    [InlineData("coffer-2026-07-03-manual-101500.db", true)]
    [InlineData("coffer-2026-07-03-manual-251500.db", false)] // invalid hour
    [InlineData("coffer-2026-07-03-manual-101500.db-wal", false)] // side-file
    [InlineData("coffer-2026-07-03-manual-101500.db.tmp", false)] // temp file
    [InlineData("coffer-2026-07-03-other-101500.db", false)]
    [InlineData("coffer-20260703T101500Z.db", false)] // pre-migration format
    public void ParseSnapshot_MatchesOnlyDailyAndManualNames(string name, bool parses)
    {
        BackupRetention.ParseSnapshot(name).HasValue.Should().Be(parses);
    }

    [Fact]
    public void ParsePreMigrationDate_ReadsUtcTimestampDay()
    {
        BackupRetention.ParsePreMigrationDate("coffer-20260703T101500Z.db")
            .Should().Be(new DateOnly(2026, 7, 3));
        BackupRetention.ParsePreMigrationDate("coffer-2026-07-03.db").Should().BeNull();
    }
}
