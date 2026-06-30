using System.Reflection;
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

        // Capture the holder's own buffer (a defensive clone of `first`) before the
        // second Set. Asserting THIS array is zeroed in place proves hygiene, not just
        // behaviour: a refactor that swaps Array.Clear for a plain reassignment would
        // leave the old DEK in memory and fail this test.
        var bufferBefore = ReadInternalBuffer(holder);
        bufferBefore.Should().Equal(first);

        var second = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        holder.Set(second);

        bufferBefore.Should().OnlyContain(b => b == 0,
            "the previous DEK buffer must be zeroed in place, not abandoned to GC");

        var retrieved = holder.Get();
        retrieved.Should().Equal(second);
        retrieved.Should().NotEqual(first);
    }

    private static byte[] ReadInternalBuffer(DekHolder holder)
    {
        var field = typeof(DekHolder)
            .GetField("_dek", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("the in-place zeroing assertion needs the backing buffer");
        return (byte[])field!.GetValue(holder)!;
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
