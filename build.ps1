# Builds LMD VR and, unless -NoDeploy, copies it into the game.
#   -Restart   close a running game first (it can take ~30 s to exit with MelonLoader) and relaunch it through Steam
#   -Package   also write dist\LMD-VR-<version>.zip, laid out to be extracted into the game folder
#   -GameDir   where the game is installed (the project references MelonLoader's generated assemblies there)
param(
    [string]$Configuration = "Release",
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Lonely Mountains - Downhill",
    [switch]$NoDeploy,
    [switch]$Restart,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\LMDVR\LMDVR.csproj"
$loader = Join-Path $root ".tools\openxr\native\x64\release\bin\openxr_loader.dll"

# A local SDK in .dotnet\ is used if present, otherwise the one on the PATH.
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (Test-Path $dotnet) { $env:DOTNET_ROOT = Join-Path $root ".dotnet" } else { $dotnet = "dotnet" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

& $dotnet build $project -c $Configuration -nologo -v minimal "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$out = Join-Path $root "src\LMDVR\bin\$Configuration\net6.0"

if ($Package) {
    if (-not (Test-Path $loader)) { throw "openxr_loader.dll not found at $loader (see README, Building)." }
    $version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $stage = Join-Path $root "dist\LMD-VR-$version"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $stage "Mods"), (Join-Path $stage "UserLibs") | Out-Null
    Copy-Item (Join-Path $out "LMDVR.dll") (Join-Path $stage "Mods")
    Copy-Item $loader (Join-Path $stage "UserLibs")
    Copy-Item (Join-Path $root "README.md") (Join-Path $stage "LMDVR-README.md")
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LMDVR-LICENSE.txt")
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") (Join-Path $stage "LMDVR-THIRD-PARTY-NOTICES.md")
    # Entries written one by one: Windows PowerShell's zip helpers put backslashes in entry names,
    # which unzip tools outside Windows turn into odd file names.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path "$stage.zip") { Remove-Item "$stage.zip" -Force }
    $zip = [System.IO.Compression.ZipFile]::Open("$stage.zip", "Create")
    try {
        foreach ($file in Get-ChildItem $stage -Recurse -File) {
            $entry = $file.FullName.Substring($stage.Length + 1).Replace("\", "/")
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry)
        }
    }
    finally {
        $zip.Dispose()
    }
    Remove-Item $stage -Recurse -Force
    Write-Host "Packaged $stage.zip"
}

if ($NoDeploy) { return }

$game = Get-Process LMD_Win_x64 -ErrorAction SilentlyContinue
if ($game) {
    if (-not $Restart) {
        Write-Warning "The game is running, so the mod DLL is locked. Close the game or use -Restart."
        return
    }
    Write-Host "Closing the game ..."
    [void]$game.CloseMainWindow()
    # A minimized fullscreen Unity game ignores the close request; fall back to ending the process.
    if (-not $game.WaitForExit(30000)) {
        Write-Warning "The game did not close within 30 s (minimized?), ending the process."
        Stop-Process -Id $game.Id -Force
        $game.WaitForExit(15000) | Out-Null
    }
    Start-Sleep -Seconds 2
}

Copy-Item (Join-Path $out "LMDVR.dll") (Join-Path $GameDir "Mods") -Force
# The mod loads the native OpenXR loader from UserLibs.
if (Test-Path $loader) { Copy-Item $loader (Join-Path $GameDir "UserLibs") -Force }
Write-Host "Deployed LMDVR.dll to $GameDir\Mods"

if ($Restart) {
    Start-Process "steam://rungameid/711540"
    Write-Host "Game launched."
}
