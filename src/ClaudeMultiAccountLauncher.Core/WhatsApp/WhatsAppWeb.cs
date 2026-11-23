using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeMultiAccountLauncher.Core.WhatsApp;

/// <summary>Rules for the page a WhatsApp profile window hosts (WhatsApp spec section 5).</summary>
public static partial class WhatsAppWeb
{
    public const string Host = "web.whatsapp.com";

    public static Uri Home { get; } = new("https://" + Host + "/");

    /// <summary>
    /// True when <paramref name="uri"/> may load inside the profile window: WhatsApp Web itself,
    /// over https. Everything else opens in the default browser (FR-W5), so the window only ever
    /// navigates to WhatsApp Web (section 7).
    /// </summary>
    public static bool StaysInWindow(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a link that leaves WhatsApp Web may be handed to the default browser. Only web
    /// links: anything else (file:, custom schemes) is dropped rather than run.
    /// </summary>
    public static bool OpensInBrowser(Uri uri) =>
        !StaysInWindow(uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto);

    /// <summary>
    /// Unread chats from the page title (FR-W3, verified 2026-11-22): <c>(n) WhatsApp</c> means
    /// n unread chats; anything else means none.
    /// </summary>
    public static int UnreadCount(string? title)
    {
        var match = title is null ? null : UnreadPrefix().Match(title);
        return match is { Success: true } && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }

    /// <summary>
    /// A profile window's title: <c>WhatsApp - name</c>, led by <c>(n) </c> while there are unread
    /// chats, as WhatsApp Web's own title is.
    /// </summary>
    public static string WindowTitle(string profileName, int unread) =>
        (unread > 0 ? $"({unread}) " : string.Empty) + "WhatsApp - " + profileName;

    [GeneratedRegex(@"^\((\d{1,6})\)\s")]
    private static partial Regex UnreadPrefix();
}
