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
  - Where the icon comes from (verified 2026-11-22): `WhatsApp.Root.exe`
    has no icon resource. Like other Store apps, the package keeps its
    icon as PNGs, `Assets\AppList.targetsize-<n>_altform-unplated.png`
    (transparent; 16 to 256 px). The `.ico` holds those PNGs as they are.
    WhatsApp profile windows use the same `.ico`
- **Rename and remove** (Claude spec FR-5): rename changes the display
  name only; remove asks a second time before deleting the profile's data
  folder (default: keep). A running profile can't be removed; quit it
  first (FR-W4)
- **Running indicator** (Claude spec FR-7): detected by process command
  line, here launcher processes carrying the WhatsApp profile argument
  for that slug. Updates live, the same way as for Claude
  - WhatsApp Default (decided 2026-11-22, revised the same day): found
    as `WhatsApp.Root.exe` by package folder,
    `5319275A.WhatsAppDesktop_*__cv1g1gvanyjgm`, any version, the same
    way as Claude MSIX in Claude spec FR-7. It counts as running only
    while that process has a visible window
  - Why by window, not by command line (verified 2026-11-22): WhatsApp
    opens its window inside its background push process, started as
    `WhatsApp.Root.exe -RegisterForBGTaskServer /nowindow
    /pushnotification -Embedding`, and there is no other process. The
    first rule (the push process doesn't count) would have shown an open
    WhatsApp as not running. Also seen the same day: closing the window
    ended the process entirely, and `--launch whatsapp-default` then
    started a new one with no arguments
- **Uninstall and remove everything** (Claude spec FR-9): also lists and
  deletes each WhatsApp profile's data folder and every `WhatsApp - *`
  shortcut the launcher made. Never touches the official WhatsApp
  Desktop or its data (the WhatsApp Default). Running profiles (decided 2026-11-22):
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

### FR-W2 Notifications (dropped 2026-11-22)

- No Windows toasts for WhatsApp messages (section 7). In the Phase 1
  spike, toasts from the unpackaged spike exe were accepted by Windows
  (they appear in its toast history) but never displayed, while toasts
  from other apps were. Vernon decided toasts aren't needed

### FR-W3 Unread count

- WhatsApp Web shows the unread count in the page title. The launcher
  reads the title and shows the count on the profile window's taskbar
  button
- Title format (verified 2026-11-22, WebView2 157): `WhatsApp` with no
  unread, `(n) WhatsApp` with unread, where n is the number of **unread
  chats**, not messages (2 chats holding 3 unread messages showed
  `(2) WhatsApp`). While loading, the title is `WhatsApp Web`

### FR-W4 Close and Quit

- Close to tray is dropped (decided 2026-11-22): there is no tray icon.
  Closing the window quits the profile, like Quit
- Quit (decided 2026-11-22): a `Quit` item in the profile row's ⋯ menu
  in the launcher. Quitting ends the profile's process
- Remove needs the profile quit first; Uninstall and remove everything
  quits running WhatsApp profiles itself after confirmation (section 4)
- The launcher's WhatsApp section (section 3) is the panel for opening
  several WhatsApp windows: Launch on each row opens that profile's
  window, or brings it to the front if it's open (FR-W6)

### FR-W5 External links

- Links that leave WhatsApp Web open in the user's default browser, not
  inside the profile window

### FR-W6 Single instance per profile

- At most one process per WhatsApp profile. A WebView2 user data folder
  can't be shared by two processes, and Chromium's lock, which gives
  Claude this for free, doesn't apply to launcher processes
- The launcher keeps its own single-instance lock per slug. A second
  launch of a running profile (from the launcher or a shortcut) hands
  off to the running process, which brings its window to the front, and
  then exits without opening a window of its own
- Verified 2026-11-22 in the Phase 1 spike: a Windows App SDK
  `AppInstance` key per slug (`whatsapp-<slug>`) with activation
  redirect. The second process handed off and exited within about 25 ms;
  the running window came to the front (also when it was hidden), and
  Windows allowed it to take the foreground

### FR-W7 Permission requests (decided 2026-11-22)

- Microphone, camera, clipboard and other requests show WebView2's own
  prompt, so the user decides
- The notification permission is open question 2 (it was granted
  automatically only for toasts, FR-W2)
- Seen in the spike (2026-11-22): on every page load WhatsApp Web makes
  one permission request that WebView2 157 reports as kind 13, a value
  the WebView2 SDK in use (1.0.3719.77) doesn't name. It goes to
  WebView2's default handling like any other request; no prompt was
  seen

### FR-W8 Start at sign-in (dropped 2026-11-22)

- Dropped with close to tray (FR-W4): a profile started at sign-in would
  only be useful running hidden

### FR-W9 Downloads (decided 2026-11-22)

- Downloaded attachments go where WebView2 puts them by default: the
  Windows Downloads folder

FR-W3 works only from what WebView2 hands the host (the page title).
Nothing is injected into the page (section 7).

### Spike results (Phase 1, 2026-11-22)

Throwaway WinUI 3 app, same SDKs as the launcher (Windows App SDK 2.7,
WebView2 SDK 1.0.3719.77), WebView2 runtime 157.0.4341.60 on Vernon's PC:

- WhatsApp Web loads and signs in by QR code in WebView2 with the
  default user agent (Edge 157); no user agent change needed
- The sign-in survives closing and restarting the profile's process
  (the session lives in the WebView2 data folder, FR-W1)
- Two profiles ran side by side, each with its own data folder
- Unread title format: FR-W3. Single instance: FR-W6. Toasts: FR-W2

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
- Dropped 2026-11-22: Windows toasts (FR-W2), close to tray (FR-W4) and
  start at sign-in (FR-W8)

## 8. Known risks

- Unofficial wrapper: Meta may flag accounts that use it. No guarantee it
  keeps working or that accounts are left alone
- WhatsApp Web changes can break the unread count (for example, a
  different page title format)
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
- [ ] Using the microphone (e.g. a voice note) shows WebView2's prompt
- [ ] The taskbar button shows the profile's unread count
- [ ] Closing the window ends the profile, and so does `Quit` in the
      profile row's ⋯ menu; the running indicator clears
- [ ] A link in a chat opens in the default browser
- [ ] Desktop shortcut `WhatsApp - <name>` opens that profile in one click
- [ ] Rename and Remove work as for Claude profiles; Remove keeps the data
      folder unless deletion is confirmed
- [ ] Running indicator shows and clears for WhatsApp profiles and for
      the WhatsApp Default; the WhatsApp Default shows Running while its
      window is open and clears when the window is closed, even though
      its background process keeps running
- [ ] `Uninstall and remove everything…` lists running WhatsApp profiles,
      quits them after confirmation, removes WhatsApp profiles' data
      and shortcuts, and leaves the official WhatsApp Desktop and its
      data untouched
- [ ] The launcher makes no network calls of its own; the WebView2
      control only navigates to WhatsApp Web, and other links open in the
      default browser

## 10. Open questions

1. The new display name and GitHub repo name (only those change; section
   6). The repo is not renamed until this is decided
2. Notification permission now that toasts are dropped (FR-W7): deny it,
   or leave it to WebView2's prompt? WhatsApp Web only asks when the user
   clicks "Turn on" in its "Message and call notifications are off"
   banner (seen in the spike)
