# multi-account-launcher

Windows launcher to run several Claude Desktop accounts side by side, each
in its own profile, and open any of them with one click.

> **Unofficial.** It starts Claude Desktop with Electron's
> `--user-data-dir` flag. Anthropic doesn't support this, and a Claude
> Desktop update can break it.

Spec: [docs/spec-2026-11-19-claude-multi-account-launcher.md](docs/spec-2026-11-19-claude-multi-account-launcher.md)
· Acceptance status: [docs/acceptance-2026-11-19.md](docs/acceptance-2026-11-19.md)

## Requirements

- Windows 10 2004 (19041) or later, x64
- Claude Desktop, Microsoft Store (MSIX) or the older standalone install

Nothing else. The app bundles .NET and the Windows App SDK, needs no admin
rights, and makes no network calls.

## Install

Download from [Releases](https://github.com/weehong-1/multi-account-launcher/releases/latest).

**Installer (recommended):** run `claude-multi-account-launcher-<version>-x64.msi`.

- Installs for your user only, no admin prompt, into
  `%LOCALAPPDATA%\Programs\claude-multi-account-launcher\`
- Adds **Multi-Account Launcher** to the Start menu and opens it
- To update, run the newer MSI; it replaces the old version
- To uninstall, see [Uninstall](#uninstall)

**Install or reinstall in one command** (PowerShell; downloads the latest
release's MSI and runs it, closing the launcher first if it's open):

```powershell
$release = Invoke-RestMethod https://api.github.com/repos/weehong-1/multi-account-launcher/releases/latest
$msi = $release.assets | Where-Object name -like '*.msi' | Select-Object -First 1
$file = Join-Path $env:TEMP $msi.name
Invoke-WebRequest $msi.browser_download_url -OutFile $file
Get-Process claude-multi-account-launcher -ErrorAction Ignore | Stop-Process
Start-Process msiexec.exe -ArgumentList "/i `"$file`"" -Wait
```

**Portable:** download `claude-multi-account-launcher.exe` and put it in a
folder it will stay in. Desktop shortcuts point at the exe's location, so
if you move it, create the shortcuts again.

### Unsigned: turn off Smart App Control first

The files aren't code-signed. On a PC with **Smart App Control** on (common
on fresh Windows 11 installs), Windows blocks the launcher outright with
"An Application Control policy has blocked this file", and there is no
"Run anyway". The installer itself still installs, but the app won't start.

Check before installing: **Windows Security → App & browser control →
Smart App Control settings**. If it's **On** or **Evaluation**, set it to
**Off**, then install.

- Since the April 2026 Windows update (builds 26100.8116 / 26200.8116 and
  later) it can be turned back on from the same page. On older builds,
  turning it off is one-way until Windows is reset.
- If Smart App Control is off, SmartScreen may still say "Windows
  protected your PC" for a downloaded file: choose **More info → Run
  anyway**.

## Use

- **Default** is your existing Claude account. It is always there.
- **Add profile**: give it a name, e.g. "Personal", then choose **Open
  Claude**. A new Claude window opens next to the one you have; nothing is
  closed.
- **Sign in with "Continue with email"**, not "Continue with Google".
  Google sign-in comes back through a `claude://` link that Windows always
  hands to your Default Claude, so it would sign in there instead. Email
  sign-in also works for accounts created with Google.
- **Launch**: opens the profile, or brings it to the front if it's already
  running. Several profiles can run at once.
- **⋯ menu**: Create desktop shortcut, Rename, Remove. Remove asks a
  second time before deleting the profile's folders; keeping them is the
  default. A profile can't be removed while it's running.
- **Desktop shortcut** `Claude - <name>` opens that profile in one click,
  without showing the launcher. It runs
  `claude-multi-account-launcher.exe --launch <slug>`
  (`--launch default` for Default).
- *Running* updates by itself: it clears as soon as a Claude exits, and
  picks up a Claude started elsewhere within a few seconds while the
  launcher is on screen. If Claude stays in the background after you
  close its window, it still counts as running.

## Uninstall

**Remove everything:** in the launcher, **⋯ (next to Add profile) →
Uninstall and remove everything…**

1. Close the Claude windows of your profiles first; the launcher asks if
   any are still open.
2. It lists everything it will delete: each profile's Claude data and
   Claude Code settings, its desktop shortcuts, its own settings, the
   installed app and its unpacked files in `%TEMP%`. Folders of profiles
   you removed earlier with "Keep folders" are listed separately; tick
   them to delete those too.
3. Choose **Remove everything**. The launcher deletes the files, closes,
   and then uninstalls itself.

Your **Default** Claude account and its data are never touched. Deleted
profiles' sign-ins and chats can't be recovered on this PC (chats stay in
the account online).

If you ran the portable exe, it tells you to delete that file yourself at
the end.

**Remove only the app:** Settings → Apps → Installed apps →
Multi-Account Launcher → Uninstall. This keeps your profiles and their
data, so reinstalling picks them up again.

## Where things live

| What | Where |
|---|---|
| Profile list | `%APPDATA%\claude-multi-account-launcher\profiles.json` (names and slugs; no secrets) |
| Shortcut icon | `%APPDATA%\claude-multi-account-launcher\claude.ico`, copied once from Claude |
| Profile's Claude Desktop data | `%APPDATA%\Claude-<slug>` |
| Profile's Claude Code config | `%USERPROFILE%\.claude-<slug>` (set as `CLAUDE_CONFIG_DIR`) |
| Default's data | Claude's own; the MSIX version keeps it in `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude` |
| Unpacked runtime | `%TEMP%\.net\claude-multi-account-launcher\` (about 160 MB, reused between runs) |

## Known limits

- After Claude updates itself and relaunches, Claude Code may fall back to
  `~/.claude`. Close Claude and launch the profile again from here.
- Connectors (GitLab etc.) must be connected again in each profile.
- Only one profile at a time can run Cowork's VM.
- Profile data folders must stay directly under `%APPDATA%` for Cowork.
  The launcher creates a profile's folder before every launch. MSIX
  Claude would otherwise put a missing folder's data in its package
  cache instead.
- Each new launcher version unpacks into a new folder under
  `%TEMP%\.net\claude-multi-account-launcher\`. Old ones can be deleted.

## Build

Needs the .NET 11 SDK (pinned in `global.json`). Visual Studio is not
needed; the Windows App SDK comes from NuGet.

```powershell
dotnet build
```

```powershell
dotnet run --project src/ClaudeMultiAccountLauncher
```

```powershell
dotnet test --project tests/ClaudeMultiAccountLauncher.Core.Tests
```

Single self-contained exe (about 70 MB, compressed):

```powershell
dotnet publish src/ClaudeMultiAccountLauncher -c Release -o artifacts/publish
```

### Release

1. Bump `<Version>` in `Directory.Build.props` (the app and the MSI share it).
2. Build the release files. This runs the tests, publishes the exe and
   builds the MSI with WiX 5 from NuGet; nothing else to install:

   ```powershell
   pwsh tools/build-release.ps1
   ```

   Output in `artifacts/release`: `claude-multi-account-launcher-<version>-x64.msi`
   and `claude-multi-account-launcher.exe`.
3. Commit, tag `v<version>`, and attach both files to a GitHub Release.

Keep the `UpgradeCode` in `installer/Package.wxs` unchanged forever;
it's what lets a new MSI replace the old one.

Don't turn on trimming or Native AOT: WMI process lookup and `.lnk`
writing use built-in COM.

## Git over SSH

This repo belongs to the `weehong-1` GitHub account. On a machine where
the default `github.com` SSH key belongs to another account, a push fails
with "Could not read from remote repository". Use the `toy.github.com`
host alias, which connects to GitHub with `weehong-1`'s key.

The alias in `~/.ssh/config` (here it lives in `~/.ssh/config.d/git`):

```
Host toy.github.com
    Hostname github.com
    User git
    IdentityFile ~/.ssh/keys/git/github_toy
```

Clone through the alias:

```powershell
git clone git@toy.github.com:weehong-1/multi-account-launcher.git
```

Or switch an existing clone to it:

```powershell
git remote set-url origin git@toy.github.com:weehong-1/multi-account-launcher.git
```

Check which account the alias signs in as. It should greet `weehong-1`:

```powershell
ssh -T git@toy.github.com
```

## Layout

| Path | What |
|---|---|
| `src/ClaudeMultiAccountLauncher.Core` | Everything testable: profiles, Claude lookup, process scan/close, launch, shortcuts, icons |
| `src/ClaudeMultiAccountLauncher` | WinUI 3 window and startup (`--launch`) |
| `tests/ClaudeMultiAccountLauncher.Core.Tests` | xUnit v3 tests |
| `installer` | Per-user MSI (WiX 5): `Package.wxs` |
| `tools/build-release.ps1` | Tests, publishes and builds the MSI into `artifacts/release` |
| `tools/make-icon.ps1` | Draws the launcher icon (`Assets/AppIcon.ico`) |

## Third-party

JetBrains Mono (Regular, v2.304) is bundled for paths. It is licensed
under the SIL Open Font License 1.1; see
[src/ClaudeMultiAccountLauncher/Assets/Fonts/JetBrainsMono-OFL.txt](src/ClaudeMultiAccountLauncher/Assets/Fonts/JetBrainsMono-OFL.txt).
