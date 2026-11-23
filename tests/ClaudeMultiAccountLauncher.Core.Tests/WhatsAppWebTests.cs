using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public class WhatsAppWebTests
{
    [Theory]
    [InlineData("(2) WhatsApp", 2)]    // seen 2026-11-22: 2 unread chats
    [InlineData("(120) WhatsApp", 120)]
    [InlineData("WhatsApp", 0)]
    [InlineData("WhatsApp Web", 0)]   // while loading
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("(x) WhatsApp", 0)]
    [InlineData("WhatsApp (3)", 0)]
    public void Unread_count_comes_from_a_leading_number_in_brackets(string? title, int expected) =>
        Assert.Equal(expected, WhatsAppWeb.UnreadCount(title));

    [Theory]
    [InlineData("Resetrix", 0, "WhatsApp - Resetrix")]
    [InlineData("Resetrix", 3, "(3) WhatsApp - Resetrix")]
    public void Window_title_is_the_profile_name_led_by_the_unread_count(string name, int unread, string expected) =>
        Assert.Equal(expected, WhatsAppWeb.WindowTitle(name, unread));

    [Theory]
    [InlineData("https://web.whatsapp.com/", true)]
    [InlineData("https://WEB.whatsapp.com/some/path?x=1", true)]
    [InlineData("http://web.whatsapp.com/", false)]
    [InlineData("https://www.whatsapp.com/", false)]
    [InlineData("https://web.whatsapp.com.evil.example/", false)]
    [InlineData("https://example.com/", false)]
    public void Only_whatsapp_web_over_https_stays_in_the_window(string url, bool expected) =>
        Assert.Equal(expected, WhatsAppWeb.StaysInWindow(new Uri(url)));

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com/a", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("https://web.whatsapp.com/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    public void Only_web_links_go_to_the_default_browser(string url, bool expected) =>
        Assert.Equal(expected, WhatsAppWeb.OpensInBrowser(new Uri(url)));
}
