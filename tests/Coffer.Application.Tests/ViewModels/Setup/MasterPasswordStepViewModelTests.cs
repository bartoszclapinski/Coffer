using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Setup;

public class MasterPasswordStepViewModelTests
{
    private const string ValidMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public void IsValid_EmptyPassword_ReturnsFalse()
    {
        var vm = CreateViewModel("MyTestPassword123!", () => "");
        vm.Password = "";
        vm.Confirmation = "";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_PasswordBelow12Chars_ReturnsFalse()
    {
        var vm = CreateViewModel("Abc!12Xy9z", () => ValidMnemonic);
        // 10 chars with 4 classes — but length rule rejects.
        vm.Password = "Abc!12Xy9z";
        vm.Confirmation = "Abc!12Xy9z";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_LessThanThreeCharClasses_ReturnsFalse()
    {
        // 12+ chars, but only lowercase + digit = 2 classes.
        var vm = CreateViewModel("aaaaaaaaaaaa1234", () => ValidMnemonic, fakeScore: 3);
        vm.Password = "aaaaaaaaaaaa1234";
        vm.Confirmation = "aaaaaaaaaaaa1234";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WeakScoreEvenWithAllStructuralRulesMet_ReturnsFalse()
    {
        // 12+ chars, 4 classes — but injected fake checker says score = 1.
        var vm = CreateViewModel("Aaaa!12345aa", () => ValidMnemonic, fakeScore: 1);
        vm.Password = "Aaaa!12345aa";
        vm.Confirmation = "Aaaa!12345aa";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_MismatchedConfirmation_ReturnsFalse()
    {
        var vm = CreateViewModel("StrongPassword!12", () => ValidMnemonic, fakeScore: 4);
        vm.Password = "StrongPassword!12";
        vm.Confirmation = "DifferentValue!34";

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_PasswordEqualToMnemonic_ReturnsFalse()
    {
        // Password text equals the BIP39 mnemonic — explicit security rule
        // from docs/architecture/09-security-key-management.md §"Master password".
        var vm = CreateViewModel(ValidMnemonic, () => ValidMnemonic, fakeScore: 4);
        vm.Password = ValidMnemonic;
        vm.Confirmation = ValidMnemonic;

        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_AllRulesMet_ReturnsTrue()
    {
        var vm = CreateViewModel("StrongPassword!12", () => ValidMnemonic, fakeScore: 4);
        vm.Password = "StrongPassword!12";
        vm.Confirmation = "StrongPassword!12";

        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ClearSensitive_ResetsPasswordAndConfirmation()
    {
        var vm = CreateViewModel("AnyStrongValue!12", () => ValidMnemonic, fakeScore: 4);
        vm.Password = "AnyStrongValue!12";
        vm.Confirmation = "AnyStrongValue!12";

        vm.ClearSensitive();

        vm.Password.Should().BeEmpty();
        vm.Confirmation.Should().BeEmpty();
    }

    private static MasterPasswordStepViewModel CreateViewModel(
        string anyText,
        Func<string> mnemonicAccessor,
        int fakeScore = 4)
    {
        var checker = new FakePasswordStrengthChecker(fakeScore);
        return new MasterPasswordStepViewModel(checker, mnemonicAccessor);
    }

    private sealed class FakePasswordStrengthChecker : IPasswordStrengthChecker
    {
        private readonly int _score;

        public FakePasswordStrengthChecker(int score) => _score = score;

        public PasswordStrength Evaluate(string password) =>
            new(_score, Warning: null, Suggestions: Array.Empty<string>());
    }
}
