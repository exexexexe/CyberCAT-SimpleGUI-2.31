<#
    Builds CP2077SaveEditor (CyberCAT-SimpleGUI) for Cyberpunk 2077 patch 2.31.

    Prerequisites
      - .NET 10 SDK (this tree pins 10.0.302 via WolvenKit/global.json).
        If it lives under %USERPROFILE%\.dotnet, this script finds it.
      - The WolvenKit submodule must be populated:
            git submodule update --init --recursive
        The zipped source ships WolvenKit/ EMPTY, and the 2024-era "develop"
        branch no longer exists upstream -- .gitmodules now tracks "main".

    Output (default): ONE self-contained CP2077SaveEditor.exe that needs no
    installed .NET runtime, plus the data files the app reads from its working
    directory (config.json, previews\, licenses\).

    -Layout Folder produces the unbundled build instead (faster startup, no
    self-extract to temp).
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('SingleFile','Folder')][string]$Layout = 'SingleFile'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $OutDir) {
    $suffix = if ($Layout -eq 'SingleFile') { '-single' } else { '' }
    $OutDir = Join-Path (Split-Path $root -Parent) "CP2077SaveEditor-2.31$suffix"
}

# --- locate the SDK -----------------------------------------------------------
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $dotnet) {
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) { $dotnet = $local } else { throw "No .NET SDK found. Install the .NET 10 SDK." }
}
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
Write-Host "Using SDK : $dotnet"
Write-Host "Layout    : $Layout"

if (-not (Test-Path "$root\WolvenKit\WolvenKit.RED4\WolvenKit.RED4.csproj")) {
    throw "WolvenKit submodule is empty. Run: git submodule update --init --recursive"
}

if (Test-Path $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }

# NOTE: never enable PublishTrimmed. WolvenKit resolves RED4 types by
# reflection (RedTypeManager/Activator), so trimming breaks save parsing
# at runtime rather than at build time.
$common = @(
    "$root\CP2077SaveEditor\CP2077SaveEditor.csproj",
    '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
    '-p:Platform=x64',
    # SolutionDir must be passed explicitly: the csproj PostBuild step
    # interpolates it, and it is undefined when building the .csproj directly.
    "-p:SolutionDir=$root\",
    '-p:PublishTrimmed=false',
    '-p:DebugType=none', '-p:DebugSymbols=false',
    '--nologo'
)
if ($Layout -eq 'SingleFile') {
    $common += @('-p:PublishSingleFile=true',
                 '-p:IncludeNativeLibrariesForSelfExtract=true',
                 '-p:EnableCompressionInSingleFile=true')
}

# Compile and publish as two steps on purpose.
#
# "dotnet publish" alone has been observed to reuse a stale RID-specific
# (bin\...\win-x64) assembly while the plain build output was current, which
# silently ships an exe built from older source -- the kind of thing you only
# notice when a feature is missing from a release. Forcing a non-incremental
# build first, then publishing with --no-build, makes the published binary
# provably the one just compiled. ("publish" rejects --no-incremental, which
# is why this cannot be one command.)
$buildArgs = @('build') + $common + @('--no-incremental')
& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

$publishArgs = @('publish') + $common + @('--no-build', '-o', $OutDir)
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

# The csproj PostBuild copies these next to the *build* output, not the
# publish output, so do it here. items.bin is deliberately NOT copied: no
# code reads it (item/fact data is compiled in as embedded resources), which
# is why the upstream 0.27d release did not ship it either.
Copy-Item "$root\config.json" $OutDir -Force
foreach ($d in 'previews','licenses') {
    if (Test-Path "$root\$d") {
        Copy-Item "$root\$d" $OutDir -Recurse -Force
    } else {
        # previews\ is not tracked in git (108 MB of game-derived PNGs); the
        # appearance tab falls back to blank thumbnails without it. See README.
        Write-Warning "$d\ is missing, not copied. Take it from the upstream 0.27d release zip."
    }
}
Remove-Item "$OutDir\previews\info.txt" -Force -ErrorAction SilentlyContinue

# Non-Windows native payloads shipped by WolvenKit.Common; dead weight here.
foreach ($f in 'libkraken.so','libkraken.dylib','libwwtools.so') {
    Remove-Item (Join-Path $OutDir $f) -Force -ErrorAction SilentlyContinue
}

$exe = Join-Path $OutDir 'CP2077SaveEditor.exe'
$mb  = [math]::Round((Get-Item $exe).Length/1MB,1)
Write-Host ""
Write-Host "Built -> $OutDir"
Write-Host "  CP2077SaveEditor.exe  $mb MB"
Write-Host "  loose files: $((Get-ChildItem -LiteralPath $OutDir -File).Count), total $([math]::Round(((Get-ChildItem -LiteralPath $OutDir -Recurse -File | Measure-Object -Sum Length).Sum)/1MB,1)) MB"
