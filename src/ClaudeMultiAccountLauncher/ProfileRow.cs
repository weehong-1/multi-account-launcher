using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Startup;

namespace ClaudeMultiAccountLauncher;

/// <summary>
/// One row in the profile list (FR-1). <see cref="Profile"/> null is a built-in Default: Claude's,
/// or the official WhatsApp Desktop in the WhatsApp section (WhatsApp spec section 3).
/// </summary>
public sealed partial class ProfileRow : INotifyPropertyChanged
{
    private Profile? _profile;
    private bool _isRunning;
    private bool _canLaunch;

    public ProfileRow(Profile? profile, ProfileApp app, string folderText)
    {
        _profile = profile;
        App = app;
        FolderText = folderText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfileApp App { get; }

    /// <summary>Stable identity across refreshes, and what <c>--launch</c> takes: the profile's slug or a reserved one.</summary>
    public string Key => _profile?.Slug ?? (IsWhatsApp ? StartupCommand.WhatsAppDefaultSlug : StartupCommand.DefaultSlug);

    public Profile? Profile
    {
        get => _profile;
        set
        {
            if (_profile == value)
            {
                return;
            }

            _profile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
            OnStatusChanged();
        }
    }

    public bool IsDefault => _profile is null;

    public bool IsCustom => !IsDefault;

    public bool IsClaude => App == ProfileApp.Claude;

    public bool IsWhatsApp => App == ProfileApp.WhatsApp;

    /// <summary>A WhatsApp profile hosted by the launcher (not the official app): it can be quit from here (FR-W4).</summary>
    public bool IsWhatsAppProfile => IsWhatsApp && IsCustom;

    public string Name => _profile?.Name ?? ProfileService.DefaultProfileName;

    public string FolderText { get; }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnStatusChanged();
        }
    }

    public bool IsNotRunning => !IsRunning;

    /// <summary>FR-1: only whether it is running; there is no stored sign-in state.</summary>
    public string StatusText => IsRunning ? "Running" : "Not running";

    /// <summary>False while the app can't be found (Claude: FR-6).</summary>
    public bool CanLaunch
    {
        get => _canLaunch;
        set
        {
            if (_canLaunch != value)
            {
                _canLaunch = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Removing is blocked while running so folders are never deleted under the app.</summary>
    public bool CanRemove => IsCustom && !IsRunning;

    /// <summary>FR-W4: Quit in the row's menu, for a running WhatsApp profile.</summary>
    public bool CanQuit => IsWhatsAppProfile && IsRunning;

    private void OnStatusChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsNotRunning));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanQuit));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
