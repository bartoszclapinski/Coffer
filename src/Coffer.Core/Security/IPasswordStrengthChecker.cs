namespace Coffer.Core.Security;

public interface IPasswordStrengthChecker
{
    PasswordStrength Evaluate(string password);
}
