param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice",
    [string]$GameManagedDir = "",
    [string]$UmmDir = "",
    [string]$EditorToolkitRoot = "",
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

if ([string]::IsNullOrWhiteSpace($EditorToolkitRoot)) {
    $EditorToolkitRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "AdofaiEditorToolkit"
}
$toolkitCoreProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit\ADOFAI.EditorToolkit.csproj"
$toolkitGameProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\ADOFAI.EditorToolkit.ADOFAI.csproj"
if (-not (Test-Path $toolkitCoreProject) -or -not (Test-Path $toolkitGameProject)) {
    throw "AdofaiEditorToolkit source not found at '$EditorToolkitRoot'. Clone https://github.com/kineticnapier/AdofaiEditorToolkit next to this repository or pass -EditorToolkitRoot."
}

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
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:UmmDir="$UmmDir" /p:EditorToolkitRoot="$EditorToolkitRoot"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$binDir = Join-Path $PSScriptRoot "src\bin\$Configuration"
$dll = Join-Path $binDir "ADOFAIMultiTileEditor.dll"
$toolkitCoreDll = Join-Path $binDir "ADOFAI.EditorToolkit.dll"
$toolkitGameDll = Join-Path $binDir "ADOFAI.EditorToolkit.ADOFAI.dll"
foreach ($p in @($dll, $toolkitCoreDll, $toolkitGameDll)) {
    if (-not (Test-Path $p)) { throw "Expected DLL was not produced/copied: $p" }
}

$infoPath = Join-Path $PSScriptRoot "Info.json"
$info = Get-Content $infoPath -Raw | ConvertFrom-Json
$version = [string]$info.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Info.json does not contain a valid Version." }

$out = Join-Path $PSScriptRoot "release\ADOFAIMultiTileEditor"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item $dll $out
Copy-Item $toolkitCoreDll $out
Copy-Item $toolkitGameDll $out
Copy-Item $infoPath $out

$zip = Join-Path $PSScriptRoot ("ADOFAIMultiTileEditor-v{0}.zip" -f $version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Built: $zip"
