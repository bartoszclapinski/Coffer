using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class InMemorySecretStoreTests
{
    [Fact]
    public async Task SetThenGet_ReturnsStoredValue()
    {
        var store = new InMemorySecretStore();

        await store.SetSecretAsync("ai.claude.apiKey", "sk-ant-test", CancellationToken.None);

        (await store.GetSecretAsync("ai.claude.apiKey", CancellationToken.None)).Should().Be("sk-ant-test");
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var store = new InMemorySecretStore();

        (await store.GetSecretAsync("nope", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Set_Overwrites_PreviousValue()
    {
        var store = new InMemorySecretStore();

        await store.SetSecretAsync("k", "first", CancellationToken.None);
        await store.SetSecretAsync("k", "second", CancellationToken.None);

        (await store.GetSecretAsync("k", CancellationToken.None)).Should().Be("second");
    }

    [Fact]
    public async Task Delete_RemovesSecret()
    {
        var store = new InMemorySecretStore();
        await store.SetSecretAsync("k", "v", CancellationToken.None);

        await store.DeleteSecretAsync("k", CancellationToken.None);

        (await store.GetSecretAsync("k", CancellationToken.None)).Should().BeNull();
    }
}
