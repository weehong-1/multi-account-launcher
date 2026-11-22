# Spec: WhatsApp multi-account support

- Date: 2026-11-22
- Owner: Vernon KOH
- Status: ready for implementation, pending Vernon's final review
- Implementer: Claude Code account
- Name: `claude-multi-account-launcher`. Only the display name and the
  GitHub repo name will change (decided 2026-11-22; section 6). The new
  name is open question 1; the repo is not renamed yet
- Repo: GitHub `weehong-1/claude-multi-account-launcher` (this repo, not
  a new one)
- Builds on: [spec-2026-11-19-claude-multi-account-launcher.md](spec-2026-11-19-claude-multi-account-launcher.md),
  which stays unchanged. "Claude spec FR-n" below refers to it

## 1. Problem

WhatsApp Desktop on Windows runs one instance and one signed-in account.
Vernon wants several WhatsApp accounts open side by side and to open any
of them with one click, the same way the launcher already does for
Claude.

### Spike: launching the official app per profile (approach A, dropped)

Tried on Vernon's PC, 2026-11-22:

- Installed: package `5319275A.WhatsAppDesktop` 2.2645.101.0, family
  `5319275A.WhatsAppDesktop_cv1g1gvanyjgm`, exe `WhatsApp.Root.exe`
- Since 2.2569.0.0 the Windows app is a WebView2 wrapper around
  `web.whatsapp.com` (reported in the press, July 2025; not verified
  here)
- No execution alias under `%LOCALAPPDATA%\Microsoft\WindowsApps`
- The AppxManifest declares no `SupportsMultipleInstances`
- Starting `WhatsApp.Root.exe` directly with `WEBVIEW2_USER_DATA_FOLDER`
  set, while WhatsApp was running, only brought the existing signed-in
  window to the front
- Getting past that would mean changing WhatsApp itself, which is out of
  scope. So approach A is dropped in favour of approach B (section 2)

## 2. How it works (approach B)

- The launcher hosts WhatsApp Web itself, in WebView2
- Each WhatsApp profile is its own process of the launcher exe, started
  with a profile argument (e.g. `--whatsapp <slug>`). That process shows
  one window with one WebView2 control on `https://web.whatsapp.com`
- Each profile has its own WebView2 user data folder, so each has its own
  sign-in, cookies and storage, separate from every other profile and from
  the official app
- **Default** is the official WhatsApp Desktop, the account the user
  already has. The launcher starts it via
  `shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App` and never
  hosts it itself. Decided 2026-11-22:
  - Built in and can't be removed, like the Claude Default
  - Reserved slug `whatsapp-default`, alongside Claude's `default`
  - If the official WhatsApp Desktop isn't installed, the WhatsApp
    Default isn't listed. WhatsApp profiles still work
- WebView2 runtime (decided 2026-11-22): the launcher uses the Evergreen
  WebView2 runtime already on the system. If it's missing, the launcher
  tells the user where to install it. No automatic download (the
  launcher makes no network calls)
- This is an unofficial wrapper, not a Meta product (section 8)

## 3. Profile model

- New field per profile in `profiles.json`: `app`, value `claude` or
  `whatsapp`
- Existing entries without `app` are read as `claude`, so current
  profiles keep working with no migration step
- Slugs (decided 2026-11-22): one slug space shared by Claude and
  WhatsApp, unique across both. A second "Personal" gets `personal-2`,
  whichever app it is for. Reserved: `default` and `whatsapp-default`
- Profile list (decided 2026-11-22): two sections, Claude and WhatsApp,
  each with its Default first
- Add profile: the user picks the app (Claude or WhatsApp) as well as the
  name
- Fields per profile otherwise unchanged (`name`, `slug`, `createdAt`;
  Claude spec section 5). No secrets, tokens or passwords stored

## 4. Reused as is

These work for WhatsApp profiles the same way they work for Claude
profiles:

- **One-click launch** (Claude spec FR-3): Launch opens the profile.
  Bringing an already running profile to the front is not reused: Claude
  gets it from Chromium's single-instance lock, WhatsApp profiles need
  their own (FR-W6)
- **Desktop shortcut** (Claude spec FR-8): named `WhatsApp - <name>`,
  opens that profile in one click without showing the launcher. Decided
  2026-11-22:
  - It runs `claude-multi-account-launcher.exe --launch <slug>`, the same
    as Claude shortcuts; the launcher works out the app from the profile
  - Icon: the official WhatsApp icon, copied once to a stable `.ico` next
    to `profiles.json` (as in Claude spec FR-8). If WhatsApp Desktop
    isn't installed, the launcher's own icon is used
- **Rename and remove** (Claude spec FR-5): rename changes the display
  name only; remove asks a second time before deleting the profile's data
  folder (default: keep). A running profile can't be removed; quit it
  first (FR-W4)
  - Removing a WhatsApp profile also removes its Start at sign-in entry
    (FR-W8), so a removed profile is never started at sign-in (decided
    2026-11-22)
- **Running indicator** (Claude spec FR-7): detected by process command
  line, here launcher processes carrying the WhatsApp profile argument
  for that slug. Updates live, the same way as for Claude
  - WhatsApp Default (decided 2026-11-22): found as `WhatsApp.Root.exe`
    by package folder, `5319275A.WhatsAppDesktop_*__cv1g1gvanyjgm`, any
    version, the same way as Claude MSIX in Claude spec FR-7. The
    background push process (arguments `-RegisterForBGTaskServer
    /nowindow`) does not count
- **Uninstall and remove everything** (Claude spec FR-9): also lists and
  deletes each WhatsApp profile's data folder, every `WhatsApp - *`
  shortcut the launcher made, and the WhatsApp profiles' startup entries
  (FR-W8). Never touches the official WhatsApp Desktop or its data (the
  WhatsApp Default). Running profiles (decided 2026-11-22):
  - Running WhatsApp profiles are listed, and after the user confirms,
    the launcher quits them itself
  - Claude profiles unchanged: the user still closes them first
  - Leftover folders (decided 2026-11-22): folders under
    `%LOCALAPPDATA%\claude-multi-account-launcher\whatsapp\` that aren't
    in the profile list (e.g. kept after Remove) are listed separately
    and deleted only if the user ticks them, as Claude spec FR-9 does for
    Claude folders
  - The `%LOCALAPPDATA%\claude-multi-account-launcher` folder itself is
    removed too
- **MSI** (Claude spec section 6): same installer, same per-user install,
  no admin rights. The install folder keeps its name, so upgrading is
  unaffected (section 6)

## 5. New for the WhatsApp window

### FR-W1 QR sign-in

- A new profile opens on WhatsApp Web's own sign-in page. The user scans
  the QR code with WhatsApp on their phone
- The launcher does not sign in for the user and stores nothing about the
  account; the session lives only in the profile's WebView2 data

### FR-W2 Notifications

- Web notifications from WhatsApp Web are shown as Windows toasts
- Decided 2026-11-22:
  - The notification permission is granted automatically for WhatsApp
    Web (FR-W7)
  - Each toast shows the profile name (e.g. "WhatsApp - Resetrix")
  - Clicking a toast brings that profile's window to the front, also
    when it is hidden in the tray

### FR-W3 Unread count

- WhatsApp Web shows the unread count in the page title. The launcher
  reads the title and shows the count on the profile window's taskbar
  button
- The title format (e.g. a leading `(3)`) is to be verified against the
  current WhatsApp Web before implementation

### FR-W4 Close to tray

- Closing the window hides it to the notification area instead of
  exiting, so the profile keeps receiving notifications
- A hidden window still counts as running (FR-7 behaviour, section 4)
- Quit (decided 2026-11-22): a `Quit` item in the tray icon's menu, and
  also in the profile row's ⋯ menu in the launcher. Quitting ends the
  profile's process
- So a WhatsApp profile stays running until it is quit. Remove needs the
  profile quit first; Uninstall and remove everything quits running
  WhatsApp profiles itself after confirmation (section 4)

### FR-W5 External links

- Links that leave WhatsApp Web open in the user's default browser, not
  inside the profile window

### FR-W6 Single instance per profile

- At most one process per WhatsApp profile. A WebView2 user data folder
  can't be shared by two processes, and Chromium's lock, which gives
  Claude this for free, doesn't apply to launcher processes
- The launcher keeps its own single-instance lock per slug. A second
  launch of a running profile (from the launcher or a shortcut) hands
  off to the running process, which brings its window to the front (also
  when hidden in the tray, FR-W4), and then exits without opening a
  window of its own

### FR-W7 Permission requests (decided 2026-11-22)

- Notifications: granted automatically for WhatsApp Web
- Every other request (microphone, camera, clipboard, etc.) shows
  WebView2's own prompt, so the user decides

### FR-W8 Start at sign-in (decided 2026-11-22)

- Per WhatsApp profile, off by default
- Turned on and off with a `Start at sign-in` option in the profile row's
  ⋯ menu
- The startup entry itself is the setting; no new field in
  `profiles.json` (section 3) (decided 2026-11-22)
- Removing the profile removes its startup entry (section 4)
- A profile started this way starts hidden in the tray
- Uninstall and remove everything removes these startup entries too
  (section 4)

### FR-W9 Downloads (decided 2026-11-22)

- Downloaded attachments go where WebView2 puts them by default: the
  Windows Downloads folder

All of FR-W2 and FR-W3 work only from what WebView2 hands the host (the
notification and the page title). Nothing is injected into the page
(section 7).

## 6. Storage

- Profile list: unchanged, `%APPDATA%\claude-multi-account-launcher\profiles.json`
  with the new `app` field (section 3)
- Each WhatsApp profile's WebView2 user data folder (decided 2026-11-22):
  `%LOCALAPPDATA%\claude-multi-account-launcher\whatsapp\<slug>`
  - Why `%LOCALAPPDATA%`: WebView2 data is mostly cache and grows large;
    it belongs to this PC and should not roam
  - Why under the launcher's own folder: one place to find and delete,
    and no clash with other apps' folders. The Cowork rule that forces
    Claude data directly under `%APPDATA%` (Claude spec section 8) does
    not apply here
- Internal names stay (decided 2026-11-22): the rename (open question 1)
  changes only the display name and the GitHub repo name. The install
  folder, `%APPDATA%\claude-multi-account-launcher`, `profiles.json` and
  the folder above keep their names. No migration, and MSI upgrades are
  unaffected

## 7. Out of scope

- Injecting script into, or altering, the WhatsApp Web page
- Auto-reply
- Reading messages
- Network calls by the launcher itself. The launcher makes no network
  calls of its own; WebView2 only navigates to WhatsApp Web, and other
  links go to the default browser (FR-W5)
- Changing the official WhatsApp Desktop app (section 1)

## 8. Known risks

- Unofficial wrapper: Meta may flag accounts that use it. No guarantee it
  keeps working or that accounts are left alone
- WhatsApp Web changes can break notifications or the unread count (for
  example, a different page title format)
- RAM: each WhatsApp window is its own WebView2 instance, a few hundred
  MB each

## 9. Acceptance criteria

- [ ] Existing `profiles.json` entries without `app` still show and launch
      as Claude profiles
- [ ] Add profile lets the user pick WhatsApp; the new profile shows in
      the WhatsApp section of the list
- [ ] Slugs are shared across apps: with a Claude "Personal" (`personal`),
      adding a WhatsApp "Personal" gives `personal-2`, and
      `--launch personal-2` opens the WhatsApp profile
- [ ] A new WhatsApp profile opens a window on `web.whatsapp.com` and
      signs in by QR code
- [ ] With the WhatsApp Default and two WhatsApp profiles running, each
      window shows its own account
- [ ] Launching a running WhatsApp profile, from the launcher or its
      shortcut, brings its window to the front and does not start a
      second process (FR-W6)
- [ ] The WhatsApp Default starts the official WhatsApp Desktop
- [ ] With WhatsApp Desktop not installed, the WhatsApp Default isn't
      listed and WhatsApp profiles still work
- [ ] A new message in a profile shows a Windows toast with the profile
      name; clicking it brings that profile's window to the front, also
      from the tray
- [ ] The notification permission is granted without a prompt; using the
      microphone (e.g. a voice note) shows WebView2's prompt
- [ ] The taskbar button shows the profile's unread count
- [ ] Closing the window hides it to the tray; notifications still arrive
- [ ] `Quit` in the tray menu ends the profile; `Quit` in the profile
      row's ⋯ menu does the same; the running indicator clears
- [ ] With `Start at sign-in` on, the profile starts hidden in the tray
      after signing in to Windows; with it off (the default), it doesn't
- [ ] A link in a chat opens in the default browser
- [ ] Desktop shortcut `WhatsApp - <name>` opens that profile in one click
- [ ] Rename and Remove work as for Claude profiles; Remove keeps the data
      folder unless deletion is confirmed, and removes the profile's
      Start at sign-in entry, so it isn't started at the next Windows
      sign-in
- [ ] Running indicator shows and clears for WhatsApp profiles and for
      the WhatsApp Default; WhatsApp's background push process alone
      doesn't count as running
- [ ] `Uninstall and remove everything…` lists running WhatsApp profiles,
      quits them after confirmation, removes WhatsApp profiles' data,
      shortcuts and startup entries, and leaves the official WhatsApp
      Desktop and its data untouched
- [ ] The launcher makes no network calls of its own; the WebView2
      control only navigates to WhatsApp Web, and other links open in the
      default browser

## 10. Open questions

1. The new display name and GitHub repo name (only those change; section
   6). The repo is not renamed until this is decided
