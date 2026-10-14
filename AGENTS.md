# AGENTS.md — IFB Connect

Read this first. Details live in `docs/` — open them only when a task needs them.

## What this is
On-prem LAN messenger for IFB Automotive. Windows only.
- `src/IFB.Server` — ASP.NET Core 8 API + SignalR hub (`/hubs/chat`), EF Core (SQLite default, SQL Server optional), JWT (RSA) + rotating hashed refresh tokens, AD/local/Negotiate auth.
- `src/IFB.Client` — WinUI 3 desktop app (MVVM Toolkit). Pages are mostly built in code (`Views/Pages/*.cs`).
- `src/IFB.Shared` — DTOs, enums, hub contracts (`Contracts/HubContracts.cs`).
- `tests/IFB.Tests` — self-hosted functional/security suite (custom `Check(...)` harness, not xUnit).
- `docs/` — ARCHITECTURE, API, DEPLOYMENT, TEST-PLAN, FIXES-2026-10-14 (latest change log + open items).

## Commands (PowerShell 7, repo root)
```powershell
dotnet build IFBConnect.sln -p:Platform=x64
dotnet run --project tests/IFB.Tests            # must stay green (57 checks)
dotnet run --project tests/IFB.Tests -- --load  # 500-client load check
./scripts/Start-Development.ps1                 # dev server https://localhost:5001
dotnet publish src/IFB.Client -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/IFBConnect-acceptance/Client
./scripts/Start-Acceptance.ps1                  # two client windows: qa-alice, qa-bob
./scripts/Build-Release.ps1 -Runtime win-x64    # offline release ZIP
```
Test-account passwords: `.work/acceptance/TEST-ACCOUNTS.md` (local only, never commit).

## Rules
- Add a check to `tests/IFB.Tests/Program.cs` for every server behaviour change.
- Never add a bare `data/` pattern to `.gitignore` or the handoff script: Git on Windows ignores case and it hides `src/IFB.Server/Data` (DbContext + migrations). Runtime attachments go to `storage/files`.
- Schema changes need migrations for BOTH providers: `Data/Migrations` (SQLite) and `Data/SqlServerMigrations`.
- Message content is plain text; never render HTML. Keep TLS validation, LAN guard, antivirus fail-closed.
- `ClaimTypes.Name` is the username (mapped from `unique_name`); read display names from the DB.
- Read receipts are "read-through": marking message X read marks all earlier incoming messages; the client sends one call for the newest visible message.
- Composer uses `PreviewKeyDown` (a TextBox with AcceptsReturn swallows Enter in KeyDown).
- Never commit `.work/`, `artifacts/`, `*.db`, `*.pem`, `*.pfx`, logs.

## State as of 14 Oct 2026
Done and pushed (commit ed6273d): review fixes listed in `docs/FIXES-2026-10-14.md`.
Not yet committed: client fixes from desktop testing (presence dots, emoji reactions, Enter to send, "Read" only on own messages, message list switched from ItemsRepeater to ItemsControl so the newest message is never hidden). The last one is unverified — rebuild the client and check before committing.

Open items:
1. Verify message-list scroll fix (newest message fully visible above the composer).
2. Settings → Availability combobox shows no selection.
3. Image previews re-download on every render (no cache).
4. Untested: auto-reconnect after server restart (client retries forever, 30 s after the first 20 quick tries), Windows toasts, presence going grey on sign-out.
5. Scalability: read receipts are one row per message per reader and SQLite hub writes are serialised; a per-member `LastReadAt` needs a migration on both providers.
