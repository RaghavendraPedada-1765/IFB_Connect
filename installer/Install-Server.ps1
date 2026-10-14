param(
    [Parameter(Mandatory)][string]$BindAddress,
    [Parameter(Mandatory)][string]$ServerHost,
    [Parameter(Mandatory)][string]$CertificatePath,
    [Security.SecureString]$CertificatePassword,
    [int]$Port = 5001,
    [string]$Destination = 'C:\IFBConnect\Server',
    [switch]$EnableWindowsSignIn,
    # Least-privilege identity for the service. The default virtual account needs no password.
    # Use a gMSA ('IFB\svc-ifbconnect$') or a domain account (with -ServiceAccountPassword) if required.
    [string]$ServiceAccount = 'NT SERVICE\IFBConnectServer',
    [Security.SecureString]$ServiceAccountPassword
)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the server installer from an elevated PowerShell window.' }
$ip = [Net.IPAddress]::Parse($BindAddress); $bytes = $ip.GetAddressBytes()
if ($bytes.Length -ne 4 -or -not ($bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168))) { throw 'Specify the private IPv4 address of the internal network interface.' }
if (Get-Service IFBConnectServer -ErrorAction SilentlyContinue) { throw 'An existing service is present. Stop it and follow the upgrade guide before replacing files.' }
$payload = Join-Path $PSScriptRoot 'Server'
if (-not (Test-Path -LiteralPath (Join-Path $payload 'IFB.Server.exe'))) { throw 'Run this installer from an extracted release package.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $payload '*') -Destination $Destination -Recurse -Force
$configPath = Join-Path $Destination 'appsettings.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.Server.BindAddress = $BindAddress; $config.Server.Port = $Port
$config.Server | Add-Member -NotePropertyName CertificatePath -NotePropertyValue ([IO.Path]::GetFullPath($CertificatePath)) -Force
if ($CertificatePassword) { $config.Server | Add-Member -NotePropertyName CertificatePassword -NotePropertyValue ([Net.NetworkCredential]::new('', $CertificatePassword).Password) -Force }
$config.Discovery.ServerHost = $ServerHost; $config.Discovery.ServerPort = $Port
$config.Auth.WindowsIntegrated.Enabled = [bool]$EnableWindowsSignIn
# Keep everything the service writes in dedicated folders so the program folder can stay read-only to it.
$config.Database.ConnectionStrings.SQLite = 'Data Source=database\ifb_connect.db;Cache=Shared;Mode=ReadWriteCreate;'
$config.FileStorage.BasePath = 'storage\files'
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath
foreach ($folder in 'certs','database','storage','logs') { New-Item -ItemType Directory -Path (Join-Path $Destination $folder) -Force | Out-Null }
$jwtKey = [Security.Cryptography.RSA]::Create(2048)
try { [IO.File]::WriteAllText((Join-Path $Destination 'certs/jwt_rsa_private.pem'), $jwtKey.ExportRSAPrivateKeyPem()) } finally { $jwtKey.Dispose() }
$env:Bootstrap__AdminUsername = Read-Host 'Initial administrator username'
$bootstrapSecret = Read-Host 'Initial administrator password (12+ characters)' -AsSecureString
$env:Bootstrap__AdminPassword = [Net.NetworkCredential]::new('', $bootstrapSecret).Password
Write-Host 'Start the server once to initialize it, then press Ctrl+C after it is listening.'
Push-Location $Destination
try { & (Join-Path $Destination 'IFB.Server.exe') }
finally { Pop-Location; Remove-Item Env:Bootstrap__AdminPassword -ErrorAction SilentlyContinue; $bootstrapSecret = $null }
New-Service -Name IFBConnectServer -BinaryPathName ('"' + (Join-Path $Destination 'IFB.Server.exe') + '"') -DisplayName 'IFB Connect Server' -StartupType Automatic | Out-Null

# Run as a least-privilege account instead of LocalSystem.
if ($ServiceAccountPassword) { & sc.exe config IFBConnectServer obj= $ServiceAccount password= ([Net.NetworkCredential]::new('', $ServiceAccountPassword).Password) | Out-Null }
else { & sc.exe config IFBConnectServer obj= $ServiceAccount | Out-Null }
if ($LASTEXITCODE -ne 0) { throw "Could not set the service account to $ServiceAccount." }

# Lock down the installation. The JWT signing key, certificate password (in appsettings.json) and
# database must not be readable by ordinary users: anyone holding the signing key can mint admin tokens.
$administrators = '*S-1-5-32-544'; $localSystem = '*S-1-5-18'
& icacls $Destination /inheritance:r /grant:r "${administrators}:(OI)(CI)F" "${localSystem}:(OI)(CI)F" "${ServiceAccount}:(OI)(CI)RX" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not restrict permissions on the installation folder.' }
& icacls (Join-Path $Destination '*') /reset /T /C /Q | Out-Null
foreach ($folder in 'database','storage','logs') {
    & icacls (Join-Path $Destination $folder) /grant "${ServiceAccount}:(OI)(CI)M" /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not grant the service write access to $folder." }
}
& icacls ([IO.Path]::GetFullPath($CertificatePath)) /grant "${ServiceAccount}:R" /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not grant the service read access to the TLS certificate file.' }
New-NetFirewallRule -DisplayName 'IFB Connect HTTPS' -Direction Inbound -Protocol TCP -LocalAddress $BindAddress -LocalPort $Port -RemoteAddress LocalSubnet -Action Allow | Out-Null
New-NetFirewallRule -DisplayName 'IFB Connect Discovery' -Direction Inbound -Protocol UDP -LocalAddress $BindAddress -LocalPort 5500 -RemoteAddress LocalSubnet -Action Allow | Out-Null
Write-Host "Service account: $ServiceAccount. Folder permissions restricted to Administrators, SYSTEM and the service."
Write-Host 'Before starting the service, confirm the antivirus scanner works under this account (docs/DEPLOYMENT.md, step 6), then run: Start-Service IFBConnectServer'

