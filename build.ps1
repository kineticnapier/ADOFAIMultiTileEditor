param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice",
    [string]$GameManagedDir = "",
    [string]$UmmDir = "",
    [string]$EditorToolkitRoot = "",
    [string]$WorkbenchDir = "",
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
    $parent = Split-Path $PSScriptRoot -Parent
    $toolkitCandidates = @(
        (Join-Path $parent "AdofaiEditorToolkit"),
        (Join-Path $parent "ADOFAI.EditorToolkit")
    )
    $EditorToolkitRoot = $toolkitCandidates | Where-Object {
        Test-Path (Join-Path $_ "src\ADOFAI.EditorToolkit\ADOFAI.EditorToolkit.csproj")
    } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($EditorToolkitRoot)) {
    throw "AdofaiEditorToolkit source not found next to this repository. Clone https://github.com/kineticnapier/AdofaiEditorToolkit or pass -EditorToolkitRoot."
}

if ([string]::IsNullOrWhiteSpace($WorkbenchDir)) {
    $parent = Split-Path $PSScriptRoot -Parent
    $workbenchCandidates = @(
        (Join-Path $GameDir "Mods\ADOFAIWorkbench"),
        (Join-Path $parent "ADOFAIWorkbench\src\bin\$Configuration")
    )
    $WorkbenchDir = $workbenchCandidates | Where-Object {
        Test-Path (Join-Path $_ "ADOFAIWorkbench.dll")
    } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($WorkbenchDir)) {
    throw "ADOFAIWorkbench.dll not found. Install/build ADOFAIWorkbench or pass -WorkbenchDir."
}

$toolkitCoreProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit\ADOFAI.EditorToolkit.csproj"
$toolkitGameProject = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\ADOFAI.EditorToolkit.ADOFAI.csproj"
if (-not (Test-Path $toolkitCoreProject) -or -not (Test-Path $toolkitGameProject)) {
    throw "AdofaiEditorToolkit source is incomplete at '$EditorToolkitRoot'."
}

$required = @(
    "Assembly-CSharp.dll",
    "RDTools.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll"
) | ForEach-Object { Join-Path $GameManagedDir $_ }
$required += Join-Path $UmmDir "UnityModManager.dll"
$required += Join-Path $WorkbenchDir "ADOFAIWorkbench.dll"
foreach ($p in $required) { if (-not (Test-Path $p)) { throw "Required reference not found: $p" } }

$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Path
if (-not $dotnet) { throw "dotnet.exe not found. Install .NET SDK 8 or later." }

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Path
if (-not $msbuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    }
}
if (-not $msbuild) { throw "msbuild.exe not found. Install Visual Studio Build Tools (.NET desktop build tools)." }

Write-Host "EditorToolkit: $EditorToolkitRoot"
Write-Host "Workbench API: $WorkbenchDir"
Write-Host "Building EditorToolkit core for .NET Framework 4.8..."
& $dotnet build $toolkitCoreProject -c $Configuration -f net48 --nologo
if ($LASTEXITCODE -ne 0) { throw "EditorToolkit core build failed with exit code $LASTEXITCODE" }

$toolkitCoreDll = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit\bin\$Configuration\net48\ADOFAI.EditorToolkit.dll"
if (-not (Test-Path $toolkitCoreDll)) { throw "EditorToolkit core DLL was not produced: $toolkitCoreDll" }

Write-Host "Building EditorToolkit ADOFAI adapter with MSBuild..."
& $msbuild $toolkitGameProject /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:EditorToolkitCoreDll="$toolkitCoreDll"
if ($LASTEXITCODE -ne 0) { throw "EditorToolkit adapter build failed with exit code $LASTEXITCODE" }

$toolkitGameDll = Join-Path $EditorToolkitRoot "src\ADOFAI.EditorToolkit.ADOFAI\bin\$Configuration\ADOFAI.EditorToolkit.ADOFAI.dll"
if (-not (Test-Path $toolkitGameDll)) { throw "EditorToolkit adapter DLL was not produced: $toolkitGameDll" }

Write-Host "Building MultiTileEditor..."
$project = Join-Path $PSScriptRoot "src\ADOFAIMultiTileEditor.csproj"
& $msbuild $project /t:Rebuild /p:Configuration=$Configuration /p:GameManagedDir="$GameManagedDir" /p:UmmDir="$UmmDir" /p:EditorToolkitRoot="$EditorToolkitRoot" /p:EditorToolkitCoreDll="$toolkitCoreDll" /p:EditorToolkitGameDll="$toolkitGameDll" /p:WorkbenchDir="$WorkbenchDir"
if ($LASTEXITCODE -ne 0) { throw "MultiTileEditor build failed with exit code $LASTEXITCODE" }

$binDir = Join-Path $PSScriptRoot "src\bin\$Configuration"
$dll = Join-Path $binDir "ADOFAIMultiTileEditor.dll"
if (-not (Test-Path $dll)) { throw "Expected DLL was not produced: $dll" }

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
