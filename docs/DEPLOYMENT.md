# Offline deployment

## Build once

Use PowerShell 7 on a Windows build machine. Run `scripts/Build-Release.ps1 -Runtime win-x64`. It publishes self-contained .NET and Windows App SDK dependencies and places the client, server and installers in an offline ZIP. Transfer it using approved company media/shares and extract it on company machines. Check the deployment's CPU architecture.

## Server

1. Choose an internal NIC's private IPv4 address and a company DNS name. Do not use a wildcard/public address. Restrict `LanSecurity:AllowedCidrRanges` to the actual company subnets.
2. Obtain an internal CA HTTPS certificate with a private key and a DNS/IP SAN matching the client address. Make the company CA chain available in each client's trusted certificate store. A root CA certificate alone is not the server certificate.
3. Generate the JWT RSA private key and restrict read access to administrators and the server service identity. Development auto-generation is disabled in production.
4. Configure SQLite storage on local disk or the SQL Server connection string. Startup applies only the selected provider's migrations. For SQL Server, grant schema migration permissions for initial setup, then apply your organization's least-privilege deployment procedure.
5. Set `Bootstrap__AdminUsername`, `Bootstrap__AdminPassword` and optionally `Bootstrap__AdminEmail` for first startup only. Remove them after the initial account is created. The server does not ship with a shared administrator password.
6. Configure the antivirus command and allowed extensions. The default fails closed if the scanner is missing. Validate the scanner's exit-code behavior on a harmless file and an approved AV test sample.
7. Install the Windows Service using the supplied server installer, or create a service pointing to `IFB.Server.exe`. The installer runs the service as the virtual account `NT SERVICE\IFBConnectServer` (override with `-ServiceAccount`, e.g. a gMSA). It removes inherited permissions from the installation folder so only Administrators, SYSTEM and the service can read it (the JWT signing key and the certificate password in `appsettings.json` must never be readable by ordinary users), gives the service write access only to `database`, `storage` and `logs`, and read access to the certificate file. Confirm the antivirus scanner works under the service account before starting the service. For manual setup apply the same permissions. Windows Service startup resolves its working directory to the application directory.
8. Restrict firewall rules to the internal local address and approved remote company subnets: TCP 5001 for HTTPS and UDP 5500 for optional discovery. The installer defaults to LocalSubnet; extend it deliberately for routed company VLANs.

Example from the extracted release package, in elevated PowerShell 7:

```powershell
./Install-Server.ps1 -BindAddress 192.168.1.10 -ServerHost ifbserver.ifb.local -CertificatePath C:\IFBCerts\server.pfx
```

For password-protected PFX files, pass `-CertificatePassword (Read-Host 'Certificate password' -AsSecureString)` to the installer. The server needs the password in its protected deployment configuration; restrict that file's permissions. Alternatively install the certificate in LocalMachine/My and select `Server:CertificateSubject` with manual service setup. Do not put certificate passwords in source control. The service installer is a setup helper; it does not sign binaries, install your CA, provision domain SPNs or apply your organization's service-account/ACL policy.

For Windows integrated sign-in, enable `Auth:WindowsIntegrated:Enabled`, set the AD domain and configure the service identity's HTTP SPN for the selected server hostname. Confirm domain users authenticate as expected, and that local-machine identities cannot become domain shadow accounts. Password-based AD login remains available with optional local fallback. Local account names must not collide with AD account names.

## Client

From the extracted offline package:

```powershell
./Install-Client.ps1 -ServerUrl https://ifbserver.ifb.local:5001
```

This installs per-user files under LocalAppData and creates a Start-menu shortcut. It does not modify CA trust or system security policy. The app's REST/WebSocket connector rejects public IP destinations and requires normal TLS certificate validation.

## Managed MSI/MSIX plan

The portable offline package and PowerShell installers are supplied and reproducible. For enterprise rollout, wrap the Client payload in your organization's signed WiX/MSIX package with the IFB publisher identity, shortcut, version/upgrade policy and per-user configuration. Bundle all runtime dependencies; avoid installer download/bootstrap URLs. Test installation, repair, uninstall, upgrades and notification activation in Intune/Group Policy staging. Signing credentials and publisher certificates must come from IFB; an unsigned placeholder package is not a production installer.

## Backup and upgrades

Stop the service or use a consistent database backup. For SQLite WAL, use a backup operation that captures committed database state rather than copying only an actively written `.db`. Preserve attachments, configuration, signing key, certificate material and logs. Back up before migrations; never switch database providers by simply pointing one provider at another provider's files.

Publish a new release into a separate folder, preserve operational data/configuration, stop the existing service, update its executable path or replace its binaries, run migrations with the selected provider and restart. Do not overwrite the JWT signing key during upgrades. Test login, two-user messaging, a verified download and an outside-subnet rejection after rollout.
