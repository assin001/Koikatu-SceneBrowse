param(
    [string]$GameRoot = 'E:\game\GDAkk\Koikatu',
    [string]$OutDir = '',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $root 'out' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$csc = Join-Path $root '_tools\roslyn\tasks\net472\csc.exe'
if (-not (Test-Path $csc)) { throw "csc not found: $csc" }

$managed = Join-Path $GameRoot 'CharaStudio_Data\Managed'
$core = Join-Path $GameRoot 'BepInEx\core'
$refNames = @(
    "$managed\mscorlib.dll",
    "$managed\System.dll",
    "$managed\System.Core.dll",
    "$managed\UnityEngine.dll",
    "$managed\UnityEngine.UI.dll",
    "$managed\Assembly-CSharp.dll",
    "$core\BepInEx.dll",
    "$core\0Harmony.dll"
)
foreach ($r in $refNames) { if (-not (Test-Path $r)) { throw "missing reference: $r" } }

$sources = Get-ChildItem (Join-Path $root 'src\KK_SceneBrowser') -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $OutDir 'KK_SceneBrowser.dll'

$args = @('/nologo', '/noconfig', '/target:library', '/nostdlib+', '/langversion:11', '/optimize+',
    '/debug:portable', "/out:$out") + ($refNames | ForEach-Object { "/r:$_" }) + $sources

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }
Write-Host "OK -> $out"

if ($Deploy) {
    $plugins = Join-Path $GameRoot 'BepInEx\plugins'
    $target = Join-Path $plugins 'KK_SceneBrowser.dll'
    if (Test-Path $target) {
        $bak = "$target.bak_" + (Get-Date -Format 'yyyyMMdd_HHmmss')
        Copy-Item $target $bak -Force
        Write-Host "backup -> $bak"
    }
    Copy-Item $out $target -Force
    Write-Host "deployed -> $target"
}

