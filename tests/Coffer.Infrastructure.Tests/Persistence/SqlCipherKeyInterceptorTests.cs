using System.Reflection;
using System.Security.Cryptography;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Persistence;

public class SqlCipherKeyInterceptorTests
{
    [Fact]
    public void Dispose_ZerosDek()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var interceptor = new SqlCipherKeyInterceptor(dek);

        interceptor.Dispose();

        // The interceptor holds the caller's array directly; dispose must zero it so the
        // DEK does not outlive the interceptor's lifetime in managed memory.
        dek.Should().OnlyContain(b => b == 0);
        ReadInternalDek(interceptor).Should().BeNull();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var interceptor = new SqlCipherKeyInterceptor(RandomNumberGenerator.GetBytes(32));

        var act = () =>
        {
            interceptor.Dispose();
            interceptor.Dispose();
        };

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureSqlCipherActive_MissingCipherVersion_ThrowsSqlCipherUnavailable(string? cipherVersion)
    {
        // Plain SQLite answers PRAGMA cipher_version with no row (ExecuteScalar → null):
        // the guard must refuse rather than let the vault be written in plaintext.
        var act = () => SqlCipherKeyInterceptor.EnsureSqlCipherActive(cipherVersion);

        act.Should().Throw<SqlCipherUnavailableException>()
            .WithMessage("*SQLCipher native provider not loaded*plaintext*");
    }

    [Fact]
    public void EnsureSqlCipherActive_DbNull_ThrowsSqlCipherUnavailable()
    {
        var act = () => SqlCipherKeyInterceptor.EnsureSqlCipherActive(DBNull.Value);

        act.Should().Throw<SqlCipherUnavailableException>();
    }

    [Fact]
    public void EnsureSqlCipherActive_VersionString_DoesNotThrow()
    {
        var act = () => SqlCipherKeyInterceptor.EnsureSqlCipherActive("4.6.1 community");

        act.Should().NotThrow();
    }

    [Fact]
    public void NativeProvider_IsSqlCipher()
    {
        // If a plain bundle_e_sqlite3 ever re-enters the dependency graph with a higher
        // version, its batteries_v2 wins and binds e_sqlite3 — this pins the binding.
        // Opening a connection runs Microsoft.Data.Sqlite's own Batteries_V2.Init().
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        SQLitePCL.raw.GetNativeLibraryName().Should().Be("e_sqlcipher");
    }

    [Fact]
    public void ConnectionOpened_RealSqlCipherProvider_PassesGuard()
    {
        using var interceptor = new SqlCipherKeyInterceptor(RandomNumberGenerator.GetBytes(32));
        using var db = CreateInMemoryContext(interceptor);

        var act = () => db.Database.OpenConnection();

        act.Should().NotThrow();
        ReadCipherVersion(db).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConnectionOpenedAsync_RealSqlCipherProvider_PassesGuard()
    {
        using var interceptor = new SqlCipherKeyInterceptor(RandomNumberGenerator.GetBytes(32));
        await using var db = CreateInMemoryContext(interceptor);

        var act = async () => await db.Database.OpenConnectionAsync();

        await act.Should().NotThrowAsync();
        ReadCipherVersion(db).Should().NotBeNullOrWhiteSpace();
    }

    private static CofferDbContext CreateInMemoryContext(SqlCipherKeyInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(interceptor)
            .Options;
        return new CofferDbContext(options);
    }

    private static string? ReadCipherVersion(CofferDbContext db)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "PRAGMA cipher_version;";
        return cmd.ExecuteScalar() as string;
    }

    private static byte[]? ReadInternalDek(SqlCipherKeyInterceptor interceptor)
    {
        var field = typeof(SqlCipherKeyInterceptor)
            .GetField("_dek", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("the dispose hygiene assertion needs the backing field");
        return (byte[]?)field!.GetValue(interceptor);
    }
}
