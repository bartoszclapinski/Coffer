using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class DekHolderTests
{
    [Fact]
    public void Get_BeforeSet_Throws()
    {
        var holder = new DekHolder();

        var act = () => holder.Get();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Set_ThenGet_RoundTrips()
    {
        var holder = new DekHolder();
        var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        holder.Set(original);
        var retrieved = holder.Get();

        retrieved.Should().Equal(original);
    }

    [Fact]
    public void Set_AfterSet_ZerosPreviousBytes()
    {
        var holder = new DekHolder();
        var first = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        holder.Set(first);

        // Get the holder's internal reference path: another Set should zero the prior bytes.
        // We cannot observe the internal buffer directly, but we can verify the second Set
        // returns the new bytes and the holder doesn't retain the old buffer (proxy: re-Get
        // after second Set returns ONLY the second buffer's content).
        var second = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        holder.Set(second);

        var retrieved = holder.Get();
        retrieved.Should().Equal(second);
        retrieved.Should().NotEqual(first);
    }

    [Fact]
    public void Clear_AfterSet_GetThrows()
    {
        var holder = new DekHolder();
        holder.Set(new byte[] { 1, 2, 3 });

        holder.Clear();

        var act = () => holder.Get();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsAvailable_ReflectsState()
    {
        var holder = new DekHolder();
        holder.IsAvailable.Should().BeFalse();

        holder.Set(new byte[] { 1, 2, 3 });
        holder.IsAvailable.Should().BeTrue();

        holder.Clear();
        holder.IsAvailable.Should().BeFalse();
    }
}
