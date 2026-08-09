using System.Text.RegularExpressions;

namespace OrderProcessing.Domain.Rules;

internal static partial class OrderValidationRules
{
    public static bool IsValidEmail(string email) => EmailRegex().IsMatch(email);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
