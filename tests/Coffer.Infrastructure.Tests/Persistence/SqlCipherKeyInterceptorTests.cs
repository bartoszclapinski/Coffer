using System.Reflection;
using System.Security.Cryptography;
using Coffer.Infrastructure.Persistence.Encryption;
using FluentAssertions;

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

    private static byte[]? ReadInternalDek(SqlCipherKeyInterceptor interceptor)
    {
        var field = typeof(SqlCipherKeyInterceptor)
            .GetField("_dek", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("the dispose hygiene assertion needs the backing field");
        return (byte[]?)field!.GetValue(interceptor);
    }
}
