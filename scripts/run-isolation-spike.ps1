$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Syltr\Syltr.csproj'
$output = Join-Path $repositoryRoot 'src\Syltr\bin\Debug\unpackaged'

dotnet build $project `
    -c Debug `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:EnableWinAppRunSupport=false `
    -p:EnableMsixTooling=false `
    -p:OutputPath=bin\Debug\unpackaged\ `
    -p:IntermediateOutputPath=obj\Debug\unpackaged\

if ($LASTEXITCODE -ne 0) {
    throw "The unpackaged Syltr build failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output 'Syltr.exe'
Start-Process -FilePath $executable -WorkingDirectory $output
