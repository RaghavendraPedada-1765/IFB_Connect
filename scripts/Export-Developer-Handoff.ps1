$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$handoffName = 'IFBConnect-Source-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$stageRoot = Join-Path $projectRoot ('artifacts/handoff/' + $handoffName)
New-Item -ItemType Directory -Path $stageRoot | Out-Null
$blockedDirectories = @('bin','obj','.work','artifacts','certs','data','logs','.git','.vs','node_modules')
$allowedExtensions = @('.cs','.csproj','.sln','.xaml','.manifest','.json','.md','.ps1','.png','.jpg','.jpeg','.svg','.txt','.props','.targets','.resx','.config','.xml')
$manifest = [Collections.Generic.List[object]]::new()
function Copy-HandoffFile([IO.FileInfo]$file) {
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked file is not supported: $($file.Name)" }
    $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    if ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative)) { throw 'Source is outside the project.' }
    $destination = Join-Path $stageRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $manifest.Add([pscustomobject]@{ path = $relative.Replace('\','/'); bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash })
}
function Copy-HandoffDirectory([string]$directory) {
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked paths are not supported: $($item.Name)" }
        if ($item.PSIsContainer) {
            if ($item.Name -notin $blockedDirectories) { Copy-HandoffDirectory $item.FullName }
        } elseif ($item.Extension.ToLowerInvariant() -in $allowedExtensions -and $item.Name -notmatch '(?i)(^\.env|credentials|secrets|accounts\.json|startup\.log)') {
            Copy-HandoffFile $item
        }
    }
}
foreach ($rootFile in @('IFBConnect.sln','README.md','.gitignore')) { Copy-HandoffFile (Get-Item -LiteralPath (Join-Path $projectRoot $rootFile) -Force) }
foreach ($directory in @('src','tests','scripts','installer','docs','assets')) { Copy-HandoffDirectory (Join-Path $projectRoot $directory) }
# Reject recognizable private keys or saved tokens instead of packaging them.
foreach ($file in Get-ChildItem -LiteralPath $stageRoot -File -Recurse | Where-Object { $_.Extension -in '.cs','.json','.ps1','.md','.txt','.xml','.config' }) {
    if (Select-String -LiteralPath $file.FullName -Pattern '-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----','"(?:accessToken|refreshToken)"\s*:\s*"[A-Za-z0-9_+./=-]{40,}"' -Quiet) {
        throw "Potential private key or saved token detected. Archive was not created: $($file.Name)"
    }
}
[pscustomobject]@{ createdAtUtc = [DateTime]::UtcNow.ToString('O'); kind = 'Source-only developer handoff'; files = @($manifest.ToArray()) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageRoot 'HANDOFF-MANIFEST.json') -Encoding utf8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = $stageRoot + '.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stageRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
# Reopen the final archive and verify every payload against the manifest.
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries.Count -ne $manifest.Count + 1) { throw 'Unexpected ZIP entry count.' }
    foreach ($entry in $manifest) {
        $archived = $archive.GetEntry($handoffName + '/' + $entry.path)
        if (!$archived -or $archived.Length -ne $entry.bytes) { throw "Archive entry missing or truncated: $($entry.path)" }
        $stream = $archived.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne $entry.sha256) { throw "Archive hash mismatch: $($entry.path)" }
    }
} finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
($zipHash + '  ' + [IO.Path]::GetFileName($zipPath)) | Set-Content -LiteralPath ($zipPath + '.sha256.txt') -Encoding utf8
Write-Output "Verified source ZIP: $zipPath"
Write-Output "Files: $($manifest.Count) source files + manifest"
Write-Output "SHA-256: $zipHash"
