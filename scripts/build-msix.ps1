[CmdletBinding()]
param(
    [ValidateSet('x64')]
    [string] $Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Syltr\Syltr.csproj'
$runtimeIdentifier = "win-$Platform"

& dotnet msbuild $project `
    -restore `
    -p:Configuration=Release `
    -p:Platform=$Platform `
    -p:RuntimeIdentifier=$runtimeIdentifier `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxBundle=Never

if ($LASTEXITCODE -ne 0) {
    throw "The Release MSIX build failed with exit code $LASTEXITCODE."
}

$packageRoot = Join-Path $repositoryRoot 'src\Syltr\AppPackages'
$package = Get-ChildItem $packageRoot -Recurse -Filter '*.msix' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $package) {
    throw "The build completed but no MSIX package was found below $packageRoot."
}

$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
$hash = Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256

[pscustomobject]@{
    Package = $package.FullName
    Architecture = $Platform
    SizeBytes = $package.Length
    SignatureStatus = $signature.Status
    Sha256 = $hash.Hash
}
