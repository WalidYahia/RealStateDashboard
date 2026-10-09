namespace RealState.Application.WhatsApp;

/// <summary>Normalizes user-entered phone numbers into the international digits-only form WhatsApp expects.</summary>
public static class WhatsAppPhoneNumber
{
    /// <summary>
    /// <c>01001234567</c> → <c>201001234567</c> (with country code <c>20</c>), <c>+20 100 123 4567</c> /
    /// <c>00201001234567</c> → <c>201001234567</c>. Chat ids containing <c>@</c> (<c>…@c.us</c>, <c>…@lid</c>,
    /// <c>…@g.us</c>) are returned unchanged.
    /// </summary>
    /// <param name="defaultCountryCode">Digits prefixed to local numbers that start with a single 0; null leaves them as-is.</param>
    public static string Normalize(string phoneOrChatId, string? defaultCountryCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneOrChatId);

        var value = phoneOrChatId.Trim();
        if (value.Contains('@'))
            return value;

        var hasPlus = value.StartsWith('+');
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
            throw new ArgumentException($"'{phoneOrChatId}' is not a valid phone number.", nameof(phoneOrChatId));

        if (hasPlus)
            return digits;
        if (digits.StartsWith("00"))
            return digits[2..];
        if (digits.StartsWith('0') && !string.IsNullOrWhiteSpace(defaultCountryCode))
            return defaultCountryCode.Trim().TrimStart('+') + digits[1..];

        return digits;
    }
}
