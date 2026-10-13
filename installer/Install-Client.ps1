param([Parameter(Mandatory)][string]$ServerUrl)
$ErrorActionPreference = 'Stop'
$serverAddress = [Uri]$ServerUrl
if ($serverAddress.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($serverAddress.UserInfo) -or $serverAddress.AbsolutePath -ne '/' -or $serverAddress.Query -or $serverAddress.Fragment) { throw 'Use an HTTPS company-server address without a path, credentials, query or fragment.' }
$payload = Join-Path $PSScriptRoot 'Client'
if (-not (Test-Path -LiteralPath (Join-Path $payload 'IFB.Client.exe'))) { throw 'Run this installer from an extracted release package.' }
$destination = Join-Path $env:LOCALAPPDATA 'IFBConnect/App'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $payload '*') -Destination $destination -Recurse -Force
$configPath = Join-Path $destination 'appsettings.client.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.Server.Host = $serverAddress.Host
$config.Server.Port = $serverAddress.Port
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath
$preferencesPath = Join-Path $env:LOCALAPPDATA 'IFBConnect/preferences.json'
if (Test-Path -LiteralPath $preferencesPath) {
    $preferences = Get-Content -LiteralPath $preferencesPath -Raw | ConvertFrom-Json
    $preferences.Server = $serverAddress.AbsoluteUri
    $preferences | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $preferencesPath
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'IFB Connect.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $destination 'IFB.Client.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.Save()
Write-Host "Installed IFB Connect at $destination. Open it from the Start menu."

