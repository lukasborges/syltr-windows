[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $repositoryRoot 'src\Syltr\Assets'
$sourcePath = Join-Path $assetDirectory 'Syltr.svg'
$edgeCandidates = @(
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
)
$edgePath = $edgeCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($edgePath)) {
    throw 'Microsoft Edge is required to render the source SVG.'
}

$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $systemTemp "Syltr-assets-$([System.Guid]::NewGuid().ToString('N'))"))
if (-not $temporaryDirectory.StartsWith(
    $systemTemp,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The temporary asset path resolved outside the system temporary directory.'
}

[System.IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
$renderPath = Join-Path $temporaryDirectory 'source.png'
$edgeProfile = Join-Path $temporaryDirectory 'edge-profile'

try {
    $sourceUri = [System.Uri]::new((Resolve-Path -LiteralPath $sourcePath).Path).AbsoluteUri
    $edgeArguments = @(
        '--headless',
        '--disable-gpu',
        '--hide-scrollbars',
        '--force-device-scale-factor=1',
        '--default-background-color=00000000',
        '--window-size=512,512',
        "--user-data-dir=$edgeProfile",
        "--screenshot=$renderPath",
        $sourceUri
    )
    $edgeProcess = Start-Process `
        -FilePath $edgePath `
        -ArgumentList $edgeArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($edgeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $renderPath)) {
        throw "Microsoft Edge failed to render the source SVG: $($edgeProcess.ExitCode)."
    }

    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Bitmap]::new($renderPath)
    try {
        if ($source.Width -ne 512 -or $source.Height -ne 512) {
            throw "The rendered source has unexpected dimensions: $($source.Width)x$($source.Height)."
        }

        function Write-Asset {
            param(
                [Parameter(Mandatory)]
                [string] $Path,

                [Parameter(Mandatory)]
                [int] $Width,

                [Parameter(Mandatory)]
                [int] $Height,

                [Parameter(Mandatory)]
                [int] $IconSize
            )

            $bitmap = [System.Drawing.Bitmap]::new(
                $Width,
                $Height,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.CompositingMode =
                        [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.CompositingQuality =
                        [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode =
                        [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode =
                        [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.SmoothingMode =
                        [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $left = [int] (($Width - $IconSize) / 2)
                    $top = [int] (($Height - $IconSize) / 2)
                    $graphics.DrawImage(
                        $source,
                        [System.Drawing.Rectangle]::new($left, $top, $IconSize, $IconSize),
                        0,
                        0,
                        $source.Width,
                        $source.Height,
                        [System.Drawing.GraphicsUnit]::Pixel)
                }
                finally {
                    $graphics.Dispose()
                }

                $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $bitmap.Dispose()
            }
        }

        $assets = @(
            @{ Name = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48; IconSize = 46 },
            @{ Name = 'SplashScreen.scale-200.png'; Width = 1240; Height = 600; IconSize = 441 },
            @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300; IconSize = 287 },
            @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88; IconSize = 84 },
            @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24; IconSize = 24 },
            @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png'; Width = 48; Height = 48; IconSize = 46 },
            @{ Name = 'StoreLogo.png'; Width = 50; Height = 50; IconSize = 48 },
            @{ Name = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300; IconSize = 221 }
        )
        foreach ($asset in $assets) {
            Write-Asset `
                -Path (Join-Path $assetDirectory $asset.Name) `
                -Width $asset.Width `
                -Height $asset.Height `
                -IconSize $asset.IconSize
        }

        $iconSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
        $iconFrames = foreach ($iconSize in $iconSizes) {
            $framePath = Join-Path $temporaryDirectory "icon-$iconSize.png"
            Write-Asset `
                -Path $framePath `
                -Width $iconSize `
                -Height $iconSize `
                -IconSize $iconSize
            [pscustomobject]@{
                Size = $iconSize
                Bytes = [System.IO.File]::ReadAllBytes($framePath)
            }
        }

        $iconPath = Join-Path $assetDirectory 'AppIcon.ico'
        $iconStream = [System.IO.File]::Create($iconPath)
        $writer = [System.IO.BinaryWriter]::new($iconStream)
        try {
            $writer.Write([uint16] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] $iconFrames.Count)
            $offset = 6 + (16 * $iconFrames.Count)
            foreach ($frame in $iconFrames) {
                $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
                $writer.Write([byte] $dimension)
                $writer.Write([byte] $dimension)
                $writer.Write([byte] 0)
                $writer.Write([byte] 0)
                $writer.Write([uint16] 1)
                $writer.Write([uint16] 32)
                $writer.Write([uint32] $frame.Bytes.Length)
                $writer.Write([uint32] $offset)
                $offset += $frame.Bytes.Length
            }
            foreach ($frame in $iconFrames) {
                $writer.Write($frame.Bytes)
            }
        }
        finally {
            $writer.Dispose()
            $iconStream.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

Get-ChildItem $assetDirectory -File |
    Where-Object Extension -In '.png', '.ico' |
    Select-Object Name, Length
