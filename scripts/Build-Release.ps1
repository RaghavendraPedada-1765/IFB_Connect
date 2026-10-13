param([ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $projectRoot ('artifacts/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Runtime)
$clientOutput = Join-Path $releaseRoot 'Client'
$serverOutput = Join-Path $releaseRoot 'Server'
New-Item -ItemType Directory -Path $clientOutput,$serverOutput -Force | Out-Null
$platform = $Runtime.Replace('win-', '')
dotnet publish (Join-Path $projectRoot 'src/IFB.Client') -c Release -r $Runtime --self-contained true -p:Platform=$platform -o $clientOutput
if ($LASTEXITCODE -ne 0) { throw 'Client publishing failed.' }
dotnet publish (Join-Path $projectRoot 'src/IFB.Server') -c Release -r $Runtime --self-contained true -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Server publishing failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer/Install-Client.ps1') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer/Install-Server.ps1') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $releaseRoot -Recurse
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath ($releaseRoot + '.zip')
Write-Host "Offline deployment package: $releaseRoot.zip"

