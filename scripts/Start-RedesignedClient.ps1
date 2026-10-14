param([string]$ClientPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/IFBConnect-apple-design-complete/Client/IFB.Client.exe'))
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ClientPath)) { throw "Redesigned client missing: $ClientPath" }
Start-Process -FilePath $ClientPath -WindowStyle Hidden
