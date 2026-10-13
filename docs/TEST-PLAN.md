# Validation plan

## Automated suite

`dotnet run --project tests/IFB.Tests` starts an isolated HTTPS server, runs functional/security assertions and exits nonzero on failure. Temporary certificates, database and logs remain under `.work/tests`. Test passwords are generated at runtime and are never printed. Results are written to `test-results.json`.

Checks cover first-start migrations/bootstrap; anonymous and role-based rejection; local password compatibility; JWT signing/validation; direct chat and real SignalR persistence; offline queue delivery; unauthorized history/files/read receipts/reactions; file hash and extension restriction; default channels/joining; announcement broadcast and acknowledgement; hub rate limiting; single-use refresh rotation; access/refresh invalidation; audit export; external-address rejection and wildcard/public binding rejection.

`dotnet run --project tests/IFB.Tests -- --load` additionally creates 500 test identities/connections and measures 250 paired deliveries, with 25 concurrent send operations. It checks p95 delivery under one second and saves `load-results.json`. This measures the local host's SQLite configuration; record network/server specifications for deployment comparisons. It does not simulate every employee continuously sending at maximum rate or prove a production SLA.

## Desktop acceptance

Interactive desktop checking could not run because computer-use authorization was unavailable. Perform these checks on Windows 10 and Windows 11:

1. Launch from the build and from the offline installed package. Confirm the sign-in form, manual server configuration, successful/failed login and session restoration.
2. Create two local employees as administrator. Sign in from two machines, start a DM and send in both directions. Verify alignment, timestamps, scrolling and unread counts.
3. Test Ctrl+K, directory search/filter, new groups, department joining, membership changes and group-admin permissions.
4. Test typing, presence, reply quotes, reactions, edit window, deletion, read receipts, older history and message search. Include duplicate timestamps at a pagination boundary.
5. Upload an allowed file/image, preview an image, save a file and compare its hash. Reject disallowed types, oversize files, malicious scan results and missing scanner with fail-closed configuration.
6. Disconnect and reconnect networking; send while offline and ensure draft preservation. Confirm offline messages arrive once and history remains available.
7. Post Normal/Important/Urgent announcements; verify visibility until acknowledgement and next pending announcement selection.
8. Check tray hide/reopen/exit, notification activation, notification mute, Do Not Disturb and saved preferences.
9. Exercise administration: create/edit/disable accounts, change roles, reset local passwords, force logout, create/archive channels, change retention, manage members, inspect connections and export CSV.
10. Check Narrator, keyboard-only menus/dialogs, Windows high contrast, 125/150/200% display scaling, small screens, light/dark/system themes, personal accent, disabled animations/transparency and RDP/performance mode.

## LAN and deployment acceptance

- Verify the listening socket uses only the configured internal address. Confirm startup refuses `0.0.0.0`, `::` and a public address.
- From a source outside allowed CIDRs, expect HTTP 403 or firewall refusal. Test real routed subnets as well as the automated synthetic middleware check.
- Verify TLS fails for an untrusted certificate or wrong hostname. Do not bypass certificate errors in the client.
- Monitor the application process's network traffic while using all features with Internet access blocked; allow only company DNS/AD, the selected server and local discovery.
- Validate AD password login and optional Windows Negotiate against a real domain/SPN configuration.
- Apply the separate SQL Server migrations on a staging SQL Server, then repeat the functional checks and backup/restore procedure.
- Validate the real scanner, service-account permissions, restart behavior, retention cleanup and signed installer rollout.

Automated tests use SQLite and a pinned test certificate. Real AD, SQL Server, antivirus and desktop notification/UI acceptance require company infrastructure and interactive access; those results are not implied by a successful build.
