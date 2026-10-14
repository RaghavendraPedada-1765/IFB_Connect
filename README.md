# IFB Connect

An on-premises Windows 10/11 messaging app for IFB Automotive. The client uses WinUI 3 and the server uses ASP.NET Core, SignalR and .NET 8. Runtime communication stays on the company LAN; package restoration is needed only on the build machine.

## What is included

- Company-password sign-in through AD, optional Windows integrated sign-in, and local accounts with Argon2id password hashing.
- Employee directory, direct messages, groups, department channels, presence, history search, replies, reactions, typing indicators and read receipts.
- File uploads with extension/size restrictions and a configurable antivirus command; downloads verify SHA-256 before saving.
- Persistent offline delivery, automatic SignalR reconnection and a manual reconnect action.
- Priority announcements which remain visible until acknowledged.
- Desktop notifications, system tray controls, Do Not Disturb, saved settings, light/dark/system themes and solid-surface performance mode.
- Administration for users, roles, groups, channels, retention, connected clients, session invalidation, announcements and CSV audit export.
- Separate SQLite and SQL Server migrations (in `src/IFB.Server/Data`, which must stay under source control), offline release packaging and installation scripts.

## Development setup

Use Windows 10 build 19041 or newer, PowerShell 7, and a .NET 8 SDK with the Windows SDK build dependencies. The client build bundles the Windows App SDK. The validated SDK here is 8.0.425; Microsoft ASP.NET/EF/SignalR packages use 8.0.31.

```powershell
cd X:\IFB
dotnet restore IFBConnect.sln -p:Platform=x64
dotnet build IFBConnect.sln --no-restore -p:Platform=x64
dotnet dev-certs https --trust
./scripts/Start-Development.ps1
```

The development script asks for an initial administrator account if the database does not exist. Use a password of at least 12 characters. It selects local authentication and binds the server to `127.0.0.1:5001`. Development startup generates a persistent JWT signing key in `certs`; production refuses to start without an existing key.

Run the client executable from `src/IFB.Client/bin/x64/Debug/net8.0-windows10.0.19041.0/IFB.Client.exe`, or use Visual Studio with IFB.Client as the startup project. Select **Configure server** on the login screen and enter `https://localhost:5001`. Sign in with the administrator account, then open **Settings → Administration** to create employees. The Production, Quality, Maintenance, HR and IT channels are created automatically.

## Using the app

- Select an employee in Directory to open a direct chat. **New group** allows multiple members.
- Select a department channel to join it. Administrators can create additional channels.
- Enter sends a message; Shift+Enter adds a newline. The attachment button opens the Windows file picker.
- Right-click a message for reply, edit, delete and reactions. An action button provides keyboard access to the same menu. Editing is available for 15 minutes.
- Search searches the current conversation. **Older messages** loads earlier history.
- Files shows attachments from conversations you belong to. Saving verifies file length and hash.
- Ctrl+K searches live conversations and employees.
- Closing can hide the app in the tray. Double-click its tray icon to reopen; right-click and choose **Exit** to stop it.
- Click the connection status to retry a disconnected connection.

## Configuration

Server settings are in `src/IFB.Server/appsettings.json`; client defaults are in `src/IFB.Client/appsettings.client.json`. Environment variables use double underscores, such as `Server__BindAddress`.

| Setting | Purpose |
|---|---|
| `Server:BindAddress` | Explicit private internal NIC address. Wildcard and public addresses are rejected; default is loopback. |
| `Server:Port` | HTTPS port, default 5001. |
| `Server:CertificatePath` / `CertificatePassword` | Internal CA server PFX; alternatively use `CertificateSubject` in LocalMachine/My. |
| `LanSecurity:AllowedCidrRanges` | Request allowlist, including loopback for development. |
| `Database:Provider` | `SQLite` or `SqlServer`, with the matching connection string. |
| `Jwt:RsaPrivateKeyPath` | Persistent RSA private key shared by token issuance and validation. |
| `Bootstrap:AdminUsername` / `AdminPassword` | First-database administrator setup; supply through environment variables and remove afterwards. |
| `Auth:Mode` | `ActiveDirectory` or `Local`. |
| `Auth:WindowsIntegrated:Enabled` | Enables the optional Negotiate endpoint; requires domain deployment and SPN configuration. |
| `FileStorage:AllowedExtensions` / `MaxFileSizeBytes` | Upload restrictions. |
| `FileStorage:AntivirusCommand` / `AntivirusArgs` | Local scanner invocation; `{filePath}` is substituted. Nonzero exit codes reject the file. |
| `FileStorage:SkipAvScanIfMissing` | Defaults to false. Explicitly enabling it permits unscanned files and logs that decision. |
| `Retention:DefaultDays` | Default channel retention; administrators can change it per conversation. |
| `Retention:AuditDays` | Audit entries older than this are deleted by the nightly retention job (default 365; 0 keeps them forever). Only state-changing calls, administration and file downloads are audited. |
| `FileStorage:BasePath` | Attachment storage, default `storage/files` relative to the server folder. Never point it inside the source tree. |

Production setup is described in [DEPLOYMENT.md](docs/DEPLOYMENT.md). Architecture, schema, design system and validation details are in [ARCHITECTURE.md](docs/ARCHITECTURE.md), [DESIGN-SYSTEM.md](docs/DESIGN-SYSTEM.md) and [TEST-PLAN.md](docs/TEST-PLAN.md).

## Tests and release packages

```powershell
dotnet run --project tests/IFB.Tests
dotnet run --project tests/IFB.Tests -- --load
./scripts/Build-Release.ps1 -Runtime win-x64
```

Tests use a separate database, generated certificate and temporary accounts under `.work/tests`; they do not touch the operational database. The load run opens 500 authenticated SignalR clients and measures 250 deliveries with 25 concurrent send operations. JSON reports are written to `docs/test-results.json` and `docs/load-results.json`. These are local benchmarks, not a guarantee for a different network or server.

Release packaging publishes self-contained client/server binaries and an offline ZIP under `artifacts`. Installation does not require downloading a runtime or calling a cloud service.

## Deployment validation still needed

The desktop build and automated server tests are provided. The user reports no issues after manually checking messaging, replies, reactions, image previews, announcements and notifications. Desktop automation was unavailable, so this is user-reported acceptance. Validate accessibility, target-computer behavior, domain/Negotiate integration and SQL Server deployment in your environment using the test plan. SQL Server migrations are generated separately; the automated functional suite runs against SQLite.

For a source-only transfer to another developer, run `./scripts/Export-Developer-Handoff.ps1` and read [DEVELOPER-HANDOFF.md](docs/DEVELOPER-HANDOFF.md). Do not ZIP the working folder directly: it contains local account/message data and private signing keys.

Voice/video calls are outside this messaging application's implemented scope; inactive call buttons were removed. Reply references provide quoted replies within the conversation rather than separate threaded discussion panes.
