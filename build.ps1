param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice",
    [string]$GameManagedDir = "",
    [string]$UmmDir = "",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GameManagedDir)) {
    $GameManagedDir = Join-Path $GameDir "A Dance of Fire and Ice_Data\Managed"
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) {
    $candidates = @(
        (Join-Path $GameManagedDir "UnityModManager"),
        (Join-Path $GameDir "UnityModManager"),
        $GameManagedDir
    )
    $UmmDir = $candidates | Where-Object { Test-Path (Join-Path $_ "UnityModManager.dll") } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($UmmDir)) { throw "UnityModManager.dll not found. Pass -UmmDir." }

$required = @(
    "Assembly-CSharp.dll",
    "RDTools.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll"
) | ForEach-Object { Join-Path $GameManagedDir $_ }
$required += Join-Path $UmmDir "UnityModManager.dll"
foreach ($p in $required) { if (-not (Test-Path $p)) { throw "Required reference not found: $p" } }

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Path
if (-not $msbuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
}
if (-not $msbuild) { throw "msbuild.exe not found. Install Visual Studio Build Tools (.NET desktop build tools)." }

$project = Join-Path $PSScriptRoot "src\ADOFAIMultiTileEditor.csproj"
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:UmmDir="$UmmDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$dll = Join-Path $PSScriptRoot "src\bin\$Configuration\ADOFAIMultiTileEditor.dll"
if (-not (Test-Path $dll)) { throw "Expected DLL was not produced: $dll" }

$out = Join-Path $PSScriptRoot "release\ADOFAIMultiTileEditor"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $dll $out
Copy-Item (Join-Path $PSScriptRoot "Info.json") $out

$zip = Join-Path $PSScriptRoot "ADOFAIMultiTileEditor-v0.6.0.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Built: $zip"
