# Acceptance status: WhatsApp support

- Date: 2026-11-22
- Against: [spec section 9](spec-2026-11-22-whatsapp-support.md#9-acceptance-criteria)
- Build: `main` at `19d6fd3` (installed MSI built from `1bc1c02`, before the
  rename to Multi-Account Launcher), Windows 11 Pro 10.0.26300, WhatsApp
  Desktop (Store) installed, WebView2 runtime 157, .NET SDK 11.0.100,
  Windows App SDK 2.7.0
- Automated tests: 151 passing (`dotnet test tests/ClaudeMultiAccountLauncher.Core.Tests`)

How each item was checked:

- **Run**: on this PC by Claude Code, against the installed launcher
  (command line, process list, UI Automation read of the window)
- **Manual**: Vernon, by hand. Anything needing the phone, a second
  WhatsApp account or deleting real data
- **Tests**: unit or integration tests in the repo

| # | Criterion | Status | How |
|---|---|---|---|
| 1 | Existing `profiles.json` entries without `app` still show and launch as Claude profiles | Pass | Tests (`Entries_without_app_are_read_as_claude`); Run: the installed launcher lists "Claude Code" (created 2026-11-19, before `app` existed) under Claude |
| 2 | Add profile lets the user pick WhatsApp; the new profile shows in the WhatsApp section | Pass (manual) | Manual 2026-11-22 (step 1): added with the WhatsApp choice, shown in the WhatsApp section |
| 3 | Slugs shared across apps: WhatsApp "Personal" next to Claude "Personal" gets `personal-2`, and `--launch personal-2` opens WhatsApp | Pass | Tests (`Slugs_are_shared_across_apps`, `Whatsapp_profile_shortcut_starts_its_window_not_claude`); manual 2026-11-22 (step 1) |
| 4 | A new WhatsApp profile opens `web.whatsapp.com` and signs in by QR code | Pass (manual) | Phase 3 (Vernon): the WhatsApp Test profile showed the QR code and signed in |
| 5 | WhatsApp Default and two WhatsApp profiles running, each shows its own account | Pass (manual) | Manual 2026-11-22 (step 2) |
| 6 | Launching a running WhatsApp profile brings its window to the front, no second process (FR-W6) | Pass | Run: with the WhatsApp Test profile running (pid 17572), `claude-multi-account-launcher.exe --whatsapp whatsapp-test` again left exactly one `--whatsapp` process, the original. Also verified in the Phase 1 spike. Manual 2026-11-22 (step 3): from the Launch button and the shortcut |
| 7 | The WhatsApp Default starts the official WhatsApp Desktop | Pass | Run (Phase 2): `--launch whatsapp-default` started WhatsApp Desktop, also after its window had been closed; tests |
| 8 | WhatsApp Desktop not installed: Default not listed, profiles still work | **Partly** | Tests only (`Whatsapp_default_not_installed_fails_with_a_message`, locator tests). WhatsApp Desktop wasn't uninstalled to check it on this PC |
| 9 | Using the microphone (e.g. a voice note) shows WebView2's prompt | Pass (manual) | Manual 2026-11-22 (step 4) |
| 10 | The taskbar button shows the profile's unread count | Pass | Title format `(n) WhatsApp` verified in the spike; tests for reading it. Manual 2026-11-22 (step 5): badge seen with a real message |
| 11 | Closing the window ends the profile, and so does `Quit`; the running indicator clears | Pass (manual) | Manual 2026-11-22 (step 7) |
| 12 | A link in a chat opens in the default browser | Pass | Tests (`WhatsAppWebTests`); manual 2026-11-22 (step 6) |
| 13 | Desktop shortcut `WhatsApp - <name>` opens that profile in one click | Pass | Tests for the shortcut's target and arguments; manual 2026-11-22 (step 3) |
| 14 | Rename and Remove work as for Claude profiles; Remove keeps the data folder unless deletion is confirmed | Pass | Rename: Vernon renamed the WhatsApp Test profile in the installed launcher. Remove: tests (`Remove_whatsapp_with_delete_removes_its_folder`); manual 2026-11-22 (step 8) |
| 15 | Running indicator shows and clears for WhatsApp profiles and the WhatsApp Default (by window) | Pass | Run: UI Automation read of the launcher showed WhatsApp Default "Running" (its window open) and the WhatsApp Test profile "Running". Clearing for the Default when its window closes: Phase 2 run. Clearing for a profile: manual 2026-11-22 (step 7) |
| 16 | `Uninstall and remove everything…` lists and quits running WhatsApp profiles, removes their data and shortcuts, leaves WhatsApp Desktop untouched | Pass | Tests (`Plan_lists_whatsapp_data_shortcuts_and_the_local_folder`, the two leftover-folder tests); manual 2026-11-22 (step 9) |
| 17 | The launcher makes no network calls of its own; WebView2 only navigates to WhatsApp Web | Pass | Code: no HTTP client, web request or socket anywhere in `src`; tests for navigation routing (`WhatsAppWebTests`) |

## Manual steps (Vernon, all passed 2026-11-22)

Use the installed launcher. To see the new name, reinstall it first:
uninstall it in Settings → Apps, then run
`artifacts\release\claude-multi-account-launcher-0.1.0-x64.msi`.
Uninstalling the app keeps your profiles.

1. **#2, #3**: Add a Claude profile "Personal", then Add another profile
   "Personal" and choose WhatsApp. It should appear in the WhatsApp
   section. In PowerShell,
   `& "$env:LOCALAPPDATA\Programs\claude-multi-account-launcher\claude-multi-account-launcher.exe" --launch personal-2`
   should open a WhatsApp window, not Claude
2. **#5**: Sign the new WhatsApp profile in with a second account. With
   WhatsApp Desktop and both profiles open, each shows its own chats
3. **#13, #6**: Create its desktop shortcut from ⋯. It should have the
   WhatsApp icon. With the window open, double-click the shortcut and
   click Launch: the same window comes to the front, no second window
4. **#9**: Hold the microphone button for a voice note: WebView2 asks
   for microphone access
5. **#10**: Have someone message the account while its window isn't
   showing the chat: a green count appears on its taskbar button
6. **#12**: Click a link in a chat: it opens in your default browser
7. **#11, #15**: Close one profile's window, and use ⋯ → Quit on the
   other: both rows turn Not running
8. **#14**: Remove a WhatsApp profile, keep its data (the default): the
   folder under `%LOCALAPPDATA%\claude-multi-account-launcher\whatsapp`
   stays
9. **#16 (destructive, last)**: With a WhatsApp profile open, run ⋯ →
   Uninstall and remove everything. It lists the open profile and closes
   it after you confirm. Afterwards, the WhatsApp profiles' folders and
   `WhatsApp - *` shortcuts are gone, and WhatsApp Desktop still opens
   signed in
