using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class Bip39SeedManagerTests
{
    private const string _trezorVector1Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string _trezorVector2Mnemonic =
        "legal winner thank year wave sausage worth useful legal winner thank yellow";
    private const string _trezorVector3Mnemonic =
        "letter advice cage absurd amount doctor acoustic avoid letter advice cage above";

    [Fact]
    public void Generate_Produces12WordsAcceptedByIsValid()
    {
        var manager = new Bip39SeedManager();

        var mnemonic = manager.GenerateMnemonic();
        var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        words.Should().HaveCount(12);
        manager.IsValid(mnemonic).Should().BeTrue();
    }

    [Theory]
    [InlineData(_trezorVector1Mnemonic)]
    [InlineData(_trezorVector2Mnemonic)]
    [InlineData(_trezorVector3Mnemonic)]
    public void IsValid_OfficialBip39Vector_ReturnsTrue(string mnemonic)
    {
        var manager = new Bip39SeedManager();

        manager.IsValid(mnemonic).Should().BeTrue();
    }

    [Fact]
    public void IsValid_InvalidChecksum_ReturnsFalse()
    {
        var manager = new Bip39SeedManager();
        // Canonical "off by one" — the valid all-same-words 12-word mnemonic is
        // "abandon × 11 + about" (last word encodes the SHA256 checksum). Twelve
        // copies of "abandon" share the same word but encode the wrong checksum bits.
        var invalid = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon";

        manager.IsValid(invalid).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NonBip39Word_ReturnsFalse()
    {
        var manager = new Bip39SeedManager();
        var invalid = "blorpf gloop snarx blip frep grunch fleem honk yorp wamp zonk plink";

        manager.IsValid(invalid).Should().BeFalse();
    }

    [Theory]
    [InlineData(
        _trezorVector1Mnemonic,
        "TREZOR",
        "C55257C360C07C72029AEBC1B53C05ED0362ADA38EAD3E3E9EFA3708E5349553")]
    [InlineData(
        _trezorVector2Mnemonic,
        "TREZOR",
        "2E8905819B8723FE2C1D161860E5EE1830318DBF49A83BD451CFB8440C28BD6F")]
    [InlineData(
        _trezorVector3Mnemonic,
        "TREZOR",
        "D71DE856F81A8ACC65E6FC851A38D4D7EC216FD0796D0A6827A3AD6ED5511A30")]
    public async Task DeriveRecoveryKey_OfficialBip39Vector_ProducesExpectedSeed(
        string mnemonic,
        string passphrase,
        string expectedFirst32BytesHex)
    {
        var manager = new Bip39SeedManager();

        var key = await manager.DeriveRecoveryKeyAsync(mnemonic, passphrase, CancellationToken.None);

        Convert.ToHexString(key).Should().Be(expectedFirst32BytesHex);
    }

    [Fact]
    public async Task DeriveRecoveryKey_DifferentPassphrase_ProducesDifferentSeed()
    {
        var manager = new Bip39SeedManager();

        var withoutPassphrase = await manager.DeriveRecoveryKeyAsync(_trezorVector1Mnemonic, "", CancellationToken.None);
        var withPassphrase = await manager.DeriveRecoveryKeyAsync(_trezorVector1Mnemonic, "TREZOR", CancellationToken.None);

        withoutPassphrase.Should().NotEqual(withPassphrase);
    }
}
