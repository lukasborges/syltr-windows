[CmdletBinding()]
param(
    [switch] $SkipBuild,

    [switch] $Launch
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $repositoryRoot 'src\Syltr\AppPackages'
$localArtifactDirectory = Join-Path $repositoryRoot 'artifacts\local'
$signedPackagePath = Join-Path $localArtifactDirectory 'Syltr-x64.msix'
$certificateFriendlyName = 'Syltr local development signing'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-msix.ps1') | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "The MSIX build failed with exit code $LASTEXITCODE."
    }
}

$unsignedPackage = Get-ChildItem $packageRoot -Recurse -Filter '*.msix' -File |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $unsignedPackage) {
    throw "No MSIX package was found below $packageRoot."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($unsignedPackage.FullName)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $manifestEntry) {
        throw 'The package does not contain AppxManifest.xml.'
    }

    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml] $manifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$manifestNamespace = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$manifestNamespace.AddNamespace(
    'a',
    'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identity = $manifest.SelectSingleNode('/a:Package/a:Identity', $manifestNamespace)
if ($null -eq $identity) {
    throw 'The package manifest does not contain a package identity.'
}

$publisher = [string] $identity.Publisher
if ([string]::IsNullOrWhiteSpace($publisher)) {
    throw 'The package identity does not declare a publisher.'
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $publisher -and
        $_.FriendlyName -eq $certificateFriendlyName -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt (Get-Date).AddDays(30)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $publisher `
        -FriendlyName $certificateFriendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(2)
}

$machineTrustedCertificate = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object Thumbprint -eq $certificate.Thumbprint |
    Select-Object -First 1
if ($null -eq $machineTrustedCertificate) {
    $windowsIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $windowsPrincipal = [System.Security.Principal.WindowsPrincipal]::new($windowsIdentity)
    $isAdministrator = $windowsPrincipal.IsInRole(
        [System.Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdministrator) {
        $elevatedArguments = @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            ('"{0}"' -f $PSCommandPath),
            '-SkipBuild'
        )
        if ($Launch) {
            $elevatedArguments += '-Launch'
        }

        $elevatedProcess = Start-Process `
            -FilePath powershell.exe `
            -ArgumentList $elevatedArguments `
            -Verb RunAs `
            -Wait `
            -PassThru
        exit $elevatedProcess.ExitCode
    }
}

if ($null -eq $machineTrustedCertificate) {
    $trustedPeople = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    $trustedPeople.Open(
        [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $trustedPeople.Add($certificate)
    }
    finally {
        $trustedPeople.Dispose()
    }
}

$signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Source -First 1
if ([string]::IsNullOrWhiteSpace($signTool)) {
    $signTool = Get-ChildItem `
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') `
        -Recurse `
        -Filter signtool.exe `
        -File `
        -ErrorAction SilentlyContinue |
        Where-Object FullName -Match '\\x64\\signtool\.exe$' |
        Sort-Object FullName -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

if ([string]::IsNullOrWhiteSpace($signTool)) {
    throw 'SignTool was not found in PATH or the restored Windows SDK Build Tools package.'
}

[System.IO.Directory]::CreateDirectory($localArtifactDirectory) | Out-Null
Copy-Item -LiteralPath $unsignedPackage.FullName -Destination $signedPackagePath -Force

& $signTool sign `
    /fd SHA256 `
    /s My `
    /sha1 $certificate.Thumbprint `
    $signedPackagePath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE."
}

& $signTool verify /pa /all $signedPackagePath
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -LiteralPath $signedPackagePath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "The signed package is not trusted: $($signature.StatusMessage)"
}

$existingPackages = Get-AppxPackage -Name $identity.Name
Get-Process -Name Syltr -ErrorAction SilentlyContinue |
    Stop-Process -Force
foreach ($existingPackage in $existingPackages) {
    Remove-AppxPackage `
        -Package $existingPackage.PackageFullName `
        -Confirm:$false
}

Add-AppxPackage `
    -Path $signedPackagePath `
    -ForceApplicationShutdown `
    -ForceUpdateFromAnyVersion

$installedPackage = Get-AppxPackage -Name $identity.Name |
    Sort-Object Version -Descending |
    Select-Object -First 1
if ($null -eq $installedPackage) {
    throw 'Windows did not report the Syltr package after installation.'
}

if ($Launch) {
    Start-Process explorer.exe "shell:AppsFolder\$($installedPackage.PackageFamilyName)!App"
}

[pscustomobject]@{
    Package = $signedPackagePath
    SignatureStatus = $signature.Status
    CertificateThumbprint = $certificate.Thumbprint
    CertificateExpires = $certificate.NotAfter
    InstalledPackage = $installedPackage.PackageFullName
    InstallLocation = $installedPackage.InstallLocation
}
