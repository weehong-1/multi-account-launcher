using System.Text.Json.Serialization;

namespace ClaudeMultiAccountLauncher.Core.Profiles;

/// <summary>Which app a profile opens (WhatsApp spec section 3).</summary>
public enum ProfileApp
{
    [JsonStringEnumMemberName("claude")]
    Claude,

    [JsonStringEnumMemberName("whatsapp")]
    WhatsApp,
}

/// <summary>
/// A user-added profile. The built-in Default profiles are not stored (FR-1).
/// No sign-in state is kept: whether it is running is detected (FR-7), and signing in
/// happens in the profile's own window (FR-4, FR-W1). Older profiles.json files carry a
/// "status" field; it is ignored on load and dropped on the next save. Entries written
/// before WhatsApp support have no "app" field and are read as Claude.
/// </summary>
public sealed record Profile(string Name, string Slug, DateTimeOffset CreatedAt, ProfileApp App = ProfileApp.Claude);
