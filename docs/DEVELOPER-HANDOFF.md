# Developer handoff

## What this package contains

IFB Connect is a Windows WinUI 3 desktop client backed by an ASP.NET Core HTTPS API and SignalR hub. It includes the client/server source, shared contracts, SQLite and SQL Server migrations, branding assets, development/release/installation scripts, functional tests and architecture/API documentation.

The source ZIP intentionally excludes running databases, messages, uploaded employee files, private keys, certificates, logs, saved accounts/passwords, temporary test data, build intermediates and published binaries. It is a source handoff, not a copy of the current installation. The recipient must create their own accounts, certificates and signing keys.

Start with README.md, docs/ARCHITECTURE.md, docs/API.md and docs/DEPLOYMENT.md. Configuration files contain example deployment names; replace them for the recipient's environment.

## Rebuild and run locally

Use Windows 10 build 19041 or newer / Windows 11, PowerShell 7, the .NET 8 SDK and Windows SDK build dependencies. NuGet access is required to restore dependencies. Extract into a new folder, then open PowerShell in that folder:

```powershell
dotnet restore IFBConnect.sln -p:Platform=x64
dotnet build IFBConnect.sln --no-restore -p:Platform=x64
dotnet dev-certs https --trust
./scripts/Start-Development.ps1
```

The development script asks for a new administrator username/password and starts a loopback HTTPS server at https://localhost:5001. In Visual Studio select IFB.Client as the startup project, use x64, and run it. Configure the client server address as https://localhost:5001 and sign in using the new administrator. Create employee accounts through Administration. Do not reuse the sender's passwords or signing keys.

Run isolated checks with `dotnet run --project tests/IFB.Tests`. Their data is generated under .work/tests. The suite has 57 checks (59 with `--load`); `docs/test-results.json` holds the latest local run. The sender reports no issues after manually testing messaging, replies, reactions, image previews, announcements and notifications; this is user-reported acceptance, not independently automated desktop verification. Domain authentication, SQL Server and deployment in the receiving environment require separate checks.

For optional two-profile development testing, first start the development server, then:

```powershell
dotnet run --project tests/IFB.Acceptance -- --database ./src/IFB.Server/ifb_connect.db --server https://localhost:5001 --output ./.work/acceptance --image ./tests/fixtures/qa-preview.png
dotnet publish src/IFB.Client -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/IFBConnect-acceptance/Client
./scripts/Start-Acceptance.ps1
```

The acceptance helper deliberately creates local QA employees and writes temporary passwords into .work/acceptance. Run it only against the new local development database. Those passwords are not in this ZIP. Use the new TEST-ACCOUNTS.md generated on the receiving computer.

## Integration into another application

The lowest-coupling starting point is to run IFB.Server as a separate service and have the host application consume its HTTPS endpoints and SignalR events. Use src/IFB.Shared for DTOs, enums and hub contracts. See docs/API.md and src/IFB.Shared/Contracts/HubContracts.cs for exact method/event names.

IFB.Client is an executable, not an embeddable control library. Launching it beside the host application is straightforward. Embedding its pages directly depends on the host framework (WinUI, WPF, WinForms, web or another platform) and requires adaptation of navigation, application resources, dependency injection, authentication, notifications and lifetime handling. Do not assume its executable can be dropped into another application as a library.

Before integration, agree on the host framework, employee identity/account mapping, sign-in flow, ownership of notifications, server/database placement and deployment method. Do not share refresh tokens through command-line arguments or configuration files. Preserve HTTPS certificate validation, authorization and antivirus checks. Keep message rendering as plain text. Production uses one server instance unless shared presence and a SignalR backplane are added.

Review dependency licenses and confirm permission to use IFB branding in the receiving software. This handoff does not grant additional intellectual-property rights.

## Transfer and integrity

Use a private, access-controlled transfer addressed to the intended developer, with an expiring link if supported. Send the ZIP's SHA-256 checksum through a separately verified channel. A checksum detects changes; it does not encrypt the archive. If encrypted transfer is required, use the organization's approved service or an AES-encrypted archive and communicate its password separately.

The ZIP includes HANDOFF-MANIFEST.json containing SHA-256 hashes for each source file. The adjacent .sha256.txt is the hash of the entire ZIP. Compare the archive hash before extraction, using `Get-FileHash -Algorithm SHA256 <zip-path>`.
