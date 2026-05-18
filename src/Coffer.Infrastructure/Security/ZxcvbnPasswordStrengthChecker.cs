using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

public sealed class ZxcvbnPasswordStrengthChecker : IPasswordStrengthChecker
{
    public PasswordStrength Evaluate(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length == 0)
        {
            return new PasswordStrength(Score: 0, Warning: null, Suggestions: Array.Empty<string>());
        }

        var result = Zxcvbn.Core.EvaluatePassword(password);
        var warning = string.IsNullOrEmpty(result.Feedback.Warning) ? null : result.Feedback.Warning;
        var suggestions = result.Feedback.Suggestions?.ToList() ?? [];
        return new PasswordStrength(result.Score, warning, suggestions);
    }
}
