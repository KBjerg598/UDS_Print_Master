[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $repositoryRoot "UDS_Print_Master\UDS_Print_Master.csproj"
$distRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist"))
$packageName = "UISI PrintMaster-$Runtime"
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $distRoot $packageName))
$archivePath = [IO.Path]::GetFullPath((Join-Path $distRoot "$packageName.zip"))
$checksumPath = [IO.Path]::GetFullPath((Join-Path $distRoot "$packageName.sha256.txt"))

foreach ($path in @($distRoot, $publishDirectory, $archivePath, $checksumPath)) {
    if (-not $path.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package path is outside the repository: $path"
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) {
    throw "Failed to publish UISI PrintMaster."
}

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "Packaging\README.txt") `
    -Destination (Join-Path $publishDirectory "README.txt")

Compress-Archive `
    -Path (Join-Path $publishDirectory "*") `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($archivePath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ASCII

Write-Host ""
Write-Host "Done:"
Write-Host "  Archive: $archivePath"
Write-Host "  SHA-256: $checksumPath"
