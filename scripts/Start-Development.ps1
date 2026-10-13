param([int]$Port = 5001)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Server__BindAddress = '127.0.0.1'
$env:Server__Port = "$Port"
$env:Auth__Mode = 'Local'
$env:Discovery__ServerHost = 'localhost'
$env:Discovery__ServerPort = "$Port"
if (-not (Test-Path (Join-Path $projectRoot 'src/IFB.Server/ifb_connect.db'))) {
    $env:Bootstrap__AdminUsername = Read-Host 'Initial administrator username'
    $bootstrapSecret = Read-Host 'Initial administrator password (12+ characters)' -AsSecureString
    $env:Bootstrap__AdminPassword = [System.Net.NetworkCredential]::new('', $bootstrapSecret).Password
}
Write-Host 'A trusted local HTTPS development certificate is required. If needed, run: dotnet dev-certs https --trust'
Push-Location (Join-Path $projectRoot 'src/IFB.Server')
try { dotnet run; if ($LASTEXITCODE -ne 0) { throw 'Server exited with an error. Check its logs.' } }
finally { Pop-Location; Remove-Item Env:Bootstrap__AdminPassword -ErrorAction SilentlyContinue; $bootstrapSecret = $null }

