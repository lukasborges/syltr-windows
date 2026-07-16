[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [string] $Repository = 'lukasborges/syltr-windows',

    [string] $OutputPath = (Join-Path (Get-Location) 'Syltr.appinstaller')
)

$ErrorActionPreference = 'Stop'

$packagePath = (Resolve-Path -LiteralPath $Package).Path
if ([System.IO.Path]::GetExtension($packagePath) -ne '.msix') {
    throw 'The package must be an MSIX file.'
}

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'Repository must use the owner/name format.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Xml.Linq
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
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

$architecture = $identity.ProcessorArchitecture
if ([string]::IsNullOrWhiteSpace($architecture)) {
    throw 'The package identity does not declare ProcessorArchitecture.'
}

$releaseBase = "https://github.com/$Repository/releases/latest/download"
$packageFileName = "Syltr-$architecture.msix"
$appInstallerUri = "$releaseBase/Syltr.appinstaller"
$packageUri = "$releaseBase/$packageFileName"
$appInstallerNamespace = [System.Xml.Linq.XNamespace]::Get(
    'http://schemas.microsoft.com/appx/appinstaller/2017/2')

$document = [System.Xml.Linq.XDocument]::new(
    [System.Xml.Linq.XDeclaration]::new('1.0', 'utf-8', $null),
    [System.Xml.Linq.XElement]::new(
        $appInstallerNamespace + 'AppInstaller',
        [System.Xml.Linq.XAttribute]::new('Version', $identity.Version),
        [System.Xml.Linq.XAttribute]::new('Uri', $appInstallerUri),
        [System.Xml.Linq.XElement]::new(
            $appInstallerNamespace + 'MainPackage',
            [System.Xml.Linq.XAttribute]::new('Name', $identity.Name),
            [System.Xml.Linq.XAttribute]::new('Publisher', $identity.Publisher),
            [System.Xml.Linq.XAttribute]::new('Version', $identity.Version),
            [System.Xml.Linq.XAttribute]::new('ProcessorArchitecture', $architecture),
            [System.Xml.Linq.XAttribute]::new('Uri', $packageUri)),
        [System.Xml.Linq.XElement]::new(
            $appInstallerNamespace + 'UpdateSettings',
            [System.Xml.Linq.XElement]::new($appInstallerNamespace + 'OnLaunch'))))

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($outputFullPath, $settings)
try {
    $document.Save($writer)
}
finally {
    $writer.Dispose()
}

[pscustomobject]@{
    AppInstaller = $outputFullPath
    PackageName = $identity.Name
    Publisher = $identity.Publisher
    Version = $identity.Version
    Architecture = $architecture
    PackageUri = $packageUri
    UpdateUri = $appInstallerUri
}
