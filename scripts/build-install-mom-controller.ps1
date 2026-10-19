[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$RestartGame,
    [switch]$SkipGameInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestRoot = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
$manifest = $null
foreach ($item in Get-ChildItem -LiteralPath $manifestRoot -Filter '*.item') {
    $candidate = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json
    if ($candidate.LaunchExecutable -ne 'KINGDOM HEARTS Melody of Memory.exe') { continue }
    if ($GameDir -and [IO.Path]::GetFullPath($candidate.InstallLocation) -ne [IO.Path]::GetFullPath($GameDir)) { continue }
    $manifest = $candidate
    break
}
if (!$GameDir) {
    if (!$manifest) { throw 'No EGS Melody of Memory installation found. Supply -GameDir.' }
    $GameDir = $manifest.InstallLocation
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$gameExe = Join-Path $GameDir 'KINGDOM HEARTS Melody of Memory.exe'
if (!(Test-Path -LiteralPath $gameExe)) { throw "Not a MoM installation: $GameDir" }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies\Unity.InputSystem.dll'))) {
    throw 'Install MelonLoader x64 and launch MoM once to generate the interop assemblies first.'
}
if ($RestartGame -and $SkipGameInstall) { throw '-RestartGame and -SkipGameInstall cannot be combined.' }
if ($RestartGame -and !$manifest) { throw 'Restart requires the matching Epic manifest; direct EXE launch is not used.' }

& dotnet build (Join-Path $repoRoot 'MoMController.csproj') -c Release --nologo "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'MoM controller build failed.' }
$built = Join-Path $repoRoot 'bin\Release\net6.0\MoMController.dll'
$runtime = Join-Path $repoRoot 'artifacts\MoM'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
Copy-Item -LiteralPath $built -Destination (Join-Path $runtime 'MoMController.dll') -Force
$source = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'ControllerMod.cs')
if ($source -notmatch 'MelonInfo\(typeof\(MoMController.ControllerMod\), "MoM Direct Controller", "([^"]+)"') { throw 'Cannot read MoM mod version.' }
$payloadVersion = $Matches[1]
@{ version = $payloadVersion; sha256 = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash.ToLowerInvariant(); loader_version = '0.7.3' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $runtime -Force
& python (Join-Path $repoRoot 'tools\package_release.py') stage-notices --output $runtime
if ($LASTEXITCODE -ne 0) { throw 'MoM notice/license staging failed.' }
if (!$SkipGameInstall) {
    $running = @(Get-Process 'KINGDOM HEARTS Melody of Memory' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $gameExe })
    if ($running.Count -gt 0 -and !$RestartGame) {
        throw 'MoM is running. Close it, or use -RestartGame to explicitly stop and relaunch it.'
    }
    foreach ($gameProcess in $running) {
        Stop-Process -Id $gameProcess.Id
        if (!$gameProcess.WaitForExit(10000)) { throw 'MoM did not exit.' }
    }
    $mods = Join-Path $GameDir 'Mods'
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    $installed = Join-Path $mods 'MoMController.dll'
    if (Test-Path -LiteralPath $installed) {
        $backupRoot = Join-Path $GameDir 'UserData\MoMController-backups'
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Copy-Item -LiteralPath $installed -Destination (Join-Path $backupRoot "MoMController-$(Get-Date -Format yyyyMMdd-HHmmss-fff).dll.bak")
    }
    Copy-Item -LiteralPath $built -Destination $installed -Force
    Get-FileHash -LiteralPath $installed -Algorithm SHA256
}
if ($RestartGame) {
    Start-Process "com.epicgames.launcher://apps/$($manifest.AppName)?action=launch&silent=true"
}
