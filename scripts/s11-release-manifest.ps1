param(
    [Parameter(Mandatory = $true)][string]$SourceSha,
    [Parameter(Mandatory = $true)][string]$BuildRunUrl,
    [string]$OutputDirectory = "s11-release-artifacts"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ($SourceSha -notmatch '^[a-f0-9]{40}$') { throw "Invalid exact source SHA" }
if ($BuildRunUrl -notmatch '^https://github.com/[^/]+/[^/]+/actions/runs/[0-9]+$') { throw "Invalid run URL" }
$root = (Resolve-Path ".").Path
$target = Join-Path $root "apps/desktop/src-tauri/target/release"
$portable = Join-Path $target "data-m-desktop.exe"
if (-not (Test-Path -LiteralPath $portable -PathType Leaf)) { throw "Portable EXE missing" }
$installers = @(Get-ChildItem -LiteralPath (Join-Path $target "bundle/nsis") -Filter "*-setup.exe" -File)
if ($installers.Count -ne 1) { throw "Require exactly one NSIS installer" }
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory must be fresh" }
New-Item -Path $OutputDirectory -ItemType Directory | Out-Null
$items = @(
    @{ source = $portable; name = "data-m-desktop.exe"; kind = "portable-exe" },
    @{ source = $installers[0].FullName; name = $installers[0].Name; kind = "nsis-installer" }
)
$artifacts = @()
$lines = @()
foreach ($item in $items) {
    $destination = Join-Path $OutputDirectory $item.name
    Copy-Item -LiteralPath $item.source -Destination $destination -ErrorAction Stop
    $sha = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $destination).Length
    if ($size -le 0 -or $sha -notmatch '^[a-f0-9]{64}$') { throw "Invalid binary/hash" }
    $artifacts += [ordered]@{
        kind = $item.kind
        artifactName = $item.name
        artifactSha256 = $sha
        sizeBytes = $size
        signatureStatus = "unsigned"
    }
    $lines += "$sha  $($item.name)"
}
$manifest = [ordered]@{
    manifestVersion = 1
    releaseVersion = "0.1.0-s11-candidate"
    productVersion = "0.1.0"
    sourceSha = $SourceSha
    schemaSupported = @(1,2,3)
    schemaLatest = 3
    buildRunUrl = $BuildRunUrl
    signatureStatus = "unsigned"
    status = "machine-candidate-only"
    dataDirectoryPolicy = "Tauri app_local_data_dir, outside executable/installer"
    artifacts = $artifacts
}
$manifestPath = Join-Path $OutputDirectory "release-manifest.json"
($manifest | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $manifestPath -Encoding utf8
($lines -join [Environment]::NewLine) | Set-Content -LiteralPath (Join-Path $OutputDirectory "SHA256SUMS.txt") -Encoding ascii
$readBack = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($readBack.artifacts.Count -ne 2) { throw "Manifest incomplete" }
foreach ($item in $readBack.artifacts) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $item.artifactName) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $item.artifactSha256) { throw "SHA mismatch: $($item.artifactName)" }
}
Write-Host "S11_MANIFEST source=$SourceSha nsis_sha256=$($artifacts[1].artifactSha256) portable_sha256=$($artifacts[0].artifactSha256)"
