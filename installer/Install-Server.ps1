param(
    [Parameter(Mandatory)][string]$BindAddress,
    [Parameter(Mandatory)][string]$ServerHost,
    [Parameter(Mandatory)][string]$CertificatePath,
    [Security.SecureString]$CertificatePassword,
    [int]$Port = 5001,
    [string]$Destination = 'C:\IFBConnect\Server',
    [switch]$EnableWindowsSignIn
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
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath
New-Item -ItemType Directory -Path (Join-Path $Destination 'certs') -Force | Out-Null
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
New-NetFirewallRule -DisplayName 'IFB Connect HTTPS' -Direction Inbound -Protocol TCP -LocalAddress $BindAddress -LocalPort $Port -RemoteAddress LocalSubnet -Action Allow | Out-Null
New-NetFirewallRule -DisplayName 'IFB Connect Discovery' -Direction Inbound -Protocol UDP -LocalAddress $BindAddress -LocalPort 5500 -RemoteAddress LocalSubnet -Action Allow | Out-Null
Write-Host 'Before starting the service, follow docs/DEPLOYMENT.md to restrict folder/key permissions and configure its service account.'

