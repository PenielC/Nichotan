using FluentValidation;

namespace Nichotan.Application.Common;

/// <summary>
/// Phone numbers are kept in international form (+ and the country code first) so staff can reach a student by WhatsApp or a call
/// from anywhere. A local number such as 077 123 4567 can't be used that way, and the country can't be guessed from it.
/// </summary>
public static class PhoneNumbers
{
    public const string Advice = "Start the phone number with your country code, for example +263 77 123 4567. A local number like 077 123 4567 can't be reached on WhatsApp.";

    /// <summary>
    /// "+263 77 123 4567" and "00263 77 123 4567" both become "+263771234567". Returns null for anything that doesn't start with
    /// + or 00, or whose digits aren't a plausible length (8 to 15, the most any international number has).
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (!text.StartsWith('+') && !text.StartsWith("00")) return null;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (text.StartsWith("00")) digits = digits[2..];
        // No country code starts with 0.
        if (digits.Length is < 8 or > 15 || digits[0] == '0') return null;
        return "+" + digits;
    }

    /// <summary>Optional: an empty value passes. A value given must be in international form.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeInternationalNumber<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => string.IsNullOrWhiteSpace(v) || Normalise(v) is not null).WithMessage(Advice);
}
