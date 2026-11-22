namespace ClaudeMultiAccountLauncher.Core.Startup;

/// <summary>
/// Parsed launcher command line:
/// <c>--launch &lt;slug&gt;</c> opens any profile from a desktop shortcut (FR-8);
/// <c>--whatsapp &lt;slug&gt;</c> makes this process the window of one WhatsApp profile
/// (WhatsApp spec section 2). The first of the two on the command line wins.
/// </summary>
public sealed record StartupCommand(string? LaunchSlug, string? Error, string? WhatsAppSlug = null)
{
    public const string LaunchSwitch = "--launch";

    public const string WhatsAppSwitch = "--whatsapp";

    /// <summary>Slug that targets the built-in Claude Default profile; reserved by the slug generator.</summary>
    public const string DefaultSlug = "default";

    /// <summary>Slug that targets the built-in WhatsApp Default (the official app); reserved too.</summary>
    public const string WhatsAppDefaultSlug = "whatsapp-default";

    public static StartupCommand None { get; } = new(null, null);

    /// <param name="args">Arguments without the program path.</param>
    public static StartupCommand Parse(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            foreach (var name in (ReadOnlySpan<string>)[LaunchSwitch, WhatsAppSwitch])
            {
                var arg = args[i];
                string? value;
                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = arg[(name.Length + 1)..];
                }
                else if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = i + 1 < args.Count ? args[i + 1] : null;
                }
                else
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    return new StartupCommand(null, $"{name} needs a profile, e.g. {name} personal");
                }

                var slug = value.Trim().ToLowerInvariant();
                return name == LaunchSwitch ? new StartupCommand(slug, null) : new StartupCommand(null, null, slug);
            }
        }

        return None;
    }
}
