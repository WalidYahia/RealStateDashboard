using System.Net;
using Microsoft.AspNetCore.Html;

namespace RealState.Web;

/// <summary>
/// Click-to-call (<c>tel:</c>) and click-to-chat (<c>wa.me</c>) buttons for a phone number, used on the
/// customers / leads lists and profile. Local numbers (0…) are assumed Egyptian for the WhatsApp link.
/// </summary>
public static class ContactLinks
{
    private const string DefaultCountryCode = "20";

    private const string PhoneSvg =
        "<svg viewBox='0 0 24 24' width='15' height='15' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true'>" +
        "<path d='M22 16.92v3a2 2 0 0 1-2.18 2 19.8 19.8 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.18 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z'/></svg>";

    private const string WhatsAppSvg =
        "<svg viewBox='0 0 24 24' width='15' height='15' fill='currentColor' aria-hidden='true'>" +
        "<path d='M12.04 2a9.9 9.9 0 0 0-8.5 14.98L2 22l5.16-1.5A9.93 9.93 0 1 0 12.04 2Zm0 18.13a8.2 8.2 0 0 1-4.18-1.15l-.3-.18-3.06.89.9-2.98-.2-.31a8.22 8.22 0 1 1 6.84 3.73Zm4.5-6.15c-.25-.12-1.46-.72-1.69-.8-.23-.08-.39-.12-.55.12-.16.25-.63.8-.78.96-.14.17-.29.19-.53.06a6.74 6.74 0 0 1-3.36-2.94c-.25-.44.25-.41.72-1.36.08-.16.04-.3-.02-.43l-.75-1.82c-.2-.48-.4-.41-.55-.42h-.47a.9.9 0 0 0-.65.3 2.74 2.74 0 0 0-.85 2.03 4.76 4.76 0 0 0 1 2.53 10.9 10.9 0 0 0 4.17 3.68c1.55.67 2.16.73 2.93.61.47-.07 1.46-.6 1.66-1.17.2-.58.2-1.07.15-1.17-.06-.1-.22-.17-.47-.29Z'/></svg>";

    /// <summary>Digits for <c>wa.me</c>: <c>01001234567</c> / <c>+20 100…</c> / <c>0020100…</c> → <c>201001234567</c>; null when unusable.</summary>
    public static string? WhatsAppNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 7) return null;
        if (trimmed.StartsWith('+')) return digits;
        if (digits.StartsWith("00")) return digits[2..];
        if (digits.StartsWith('0')) return DefaultCountryCode + digits[1..];
        return digits;
    }

    /// <summary>The number for a <c>tel:</c> link (keeps a leading +, drops spaces/dashes); null when empty.</summary>
    public static string? TelNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : (trimmed.StartsWith('+') ? "+" : "") + digits;
    }

    /// <summary>
    /// The two buttons (call + WhatsApp). <paramref name="labeled"/> adds the text «اتصال» / «واتساب» (profile header);
    /// otherwise icon-only (table cells). Renders nothing for an empty number.
    /// </summary>
    public static IHtmlContent Buttons(string? phone, bool labeled = false)
    {
        var tel = TelNumber(phone);
        if (tel is null) return HtmlString.Empty;
        var wa = WhatsAppNumber(phone);
        var cls = labeled ? "cbtn cbtn-lg" : "cbtn";

        var html = $"<span class='contact-btns'>" +
                   $"<a class='{cls} cbtn-call' href='tel:{tel}' title='اتصال {WebUtility.HtmlEncode(phone)}' aria-label='اتصال'>{PhoneSvg}{(labeled ? "<span>اتصال</span>" : "")}</a>";
        if (wa is not null)
        {
            html += $"<a class='{cls} cbtn-wa' href='https://wa.me/{wa}' target='_blank' rel='noopener' title='محادثة واتساب' aria-label='واتساب'>{WhatsAppSvg}{(labeled ? "<span>واتساب</span>" : "")}</a>";
        }
        return new HtmlString(html + "</span>");
    }
}
