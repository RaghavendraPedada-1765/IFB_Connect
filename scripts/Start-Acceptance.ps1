param([string]$ClientPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/IFBConnect-acceptance/Client/IFB.Client.exe'))
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ClientPath)) { throw "Acceptance client is missing: $ClientPath" }
foreach ($profile in @('qa-alice', 'qa-bob')) {
    Start-Process -FilePath $ClientPath -ArgumentList @('--profile', $profile) -WindowStyle Hidden
}
Write-Host 'Two independent test profiles launched. Sign in using .work/acceptance/TEST-ACCOUNTS.md.'
