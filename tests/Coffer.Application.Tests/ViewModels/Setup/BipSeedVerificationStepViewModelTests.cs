using Coffer.Application.ViewModels.Setup;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Setup;

public class BipSeedVerificationStepViewModelTests
{
    private const string _mnemonic =
        "abandon abandon ability abandon abandon abandon ability abandon abandon abandon abandon about";

    [Fact]
    public void IsValid_CorrectWords_ReturnsTrue()
    {
        // Word #3 (1-indexed → array[2]) = "ability"; word #7 → array[6] = "ability".
        var vm = new BipSeedVerificationStepViewModel(() => _mnemonic);
        vm.Word3 = "ability";
        vm.Word7 = "ability";

        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_CaseInsensitive_ReturnsTrue()
    {
        var vm = new BipSeedVerificationStepViewModel(() => _mnemonic);
        vm.Word3 = "ABILITY";
        vm.Word7 = "Ability";

        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_TrimsWhitespace_ReturnsTrue()
    {
        var vm = new BipSeedVerificationStepViewModel(() => _mnemonic);
        vm.Word3 = "  ability  ";
        vm.Word7 = " ability ";

        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_WrongWord3_ReturnsFalse()
    {
        var vm = new BipSeedVerificationStepViewModel(() => _mnemonic);
        vm.Word3 = "wrong";
        vm.Word7 = "ability";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_Empty_mnemonic_ReturnsFalse()
    {
        var vm = new BipSeedVerificationStepViewModel(() => "");
        vm.Word3 = "anything";
        vm.Word7 = "anything";

        vm.IsValid.Should().BeFalse();
    }
}
