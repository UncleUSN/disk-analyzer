# Build, test, and publish Disk Analyzer as one self-contained exe.
# Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet SDK 8 was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

Write-Host "dotnet $(dotnet --version)"
Write-Host 'Building the solution...'
dotnet build DiskAnalyzer.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Running tests...'
dotnet test DiskAnalyzer.sln -c Release --nologo --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $root 'artifacts\publish'
if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}

Write-Host 'Publishing a single self-contained exe...'
dotnet publish (Join-Path $root 'src\DiskAnalyzer.App\DiskAnalyzer.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out 'DiskAnalyzer.exe'
$json = Join-Path $out 'exclusions.json'
if (-not (Test-Path -LiteralPath $json)) {
    $found = Get-ChildItem -LiteralPath $out -Filter 'exclusions.json' -Recurse -File | Select-Object -First 1
    if ($null -ne $found) {
        Copy-Item -LiteralPath $found.FullName -Destination $json
    }
    else {
        Copy-Item -LiteralPath (Join-Path $root 'config\exclusions.json') -Destination $json
    }
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Published exe was not found: $exe"
}

$info = Get-Item -LiteralPath $exe
Write-Host ("Ready: {0} ({1:N1} MB)" -f $exe, ($info.Length / 1MB))
Write-Host "Exclusions: $json"
