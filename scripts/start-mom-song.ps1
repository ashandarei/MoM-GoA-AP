[CmdletBinding()]
param(
    [uint32]$SongId,
    [ValidateSet('Beginner', 'Standard', 'Proud')]
    [string]$Difficulty = 'Beginner',
    [ValidateSet('Normal', 'OneButton', 'Performer')]
    [string]$PlayStyle = 'Normal',
    [ValidateSet('None', 'Potion', 'MegaPotion', 'SummoningStar')]
    [string]$StartingItem = 'None',
    [ValidateSet('Classic', 'Days', '3D', 'BBS')]
    [string]$Party = 'Classic',
    [string]$GameDir,
    [switch]$UseSaveProfile,
    [switch]$RestartGame,
    [switch]$NoWait,
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
if ($UseSaveProfile -and ($StartingItem -ne 'None' -or $PSBoundParameters.ContainsKey('Party'))) {
    throw 'Starting item and party overrides require the default temporary profile; omit -UseSaveProfile.'
}
$manifestRoot = Join-Path $env:ProgramData 'Epic\EpicGamesLauncher\Data\Manifests'
$manifest = $null
foreach ($item in Get-ChildItem -LiteralPath $manifestRoot -Filter '*.item') {
    $candidate = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json
    if ($candidate.LaunchExecutable -ne 'KINGDOM HEARTS Melody of Memory.exe') { continue }
    if ($GameDir -and [IO.Path]::GetFullPath($candidate.InstallLocation) -ne [IO.Path]::GetFullPath($GameDir)) { continue }
    $manifest = $candidate
    break
}
if (!$manifest) { throw 'No matching Epic Melody of Memory installation found.' }
$GameDir = (Resolve-Path -LiteralPath $manifest.InstallLocation).Path
$gameExe = Join-Path $GameDir 'KINGDOM HEARTS Melody of Memory.exe'
if (!(Test-Path -LiteralPath $gameExe) -or
    !(Test-Path -LiteralPath (Join-Path $GameDir 'Mods\MoMController.dll'))) {
    throw 'Install the MoM controller/song-launch mod before launching a song.'
}
$running = @(Get-Process 'KINGDOM HEARTS Melody of Memory' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $gameExe })
if ($running.Count -gt 0 -and !$RestartGame) {
    throw 'MoM is already running. Close it first, or use -RestartGame to discard the current session.'
}
$userData = Join-Path $GameDir 'UserData'
$requestPath = Join-Path $userData 'MoMSongLaunch-request.json'
$resultPath = Join-Path $userData 'MoMSongLaunch-result.json'
if (Test-Path -LiteralPath $requestPath) {
    throw "An unconsumed launch request already exists: $requestPath"
}
foreach ($gameProcess in $running) {
    Stop-Process -Id $gameProcess.Id
    if (!$gameProcess.WaitForExit(10000)) { throw 'MoM did not exit.' }
}
New-Item -ItemType Directory -Path $userData -Force | Out-Null
$requestId = [guid]::NewGuid().ToString()
$request = [ordered]@{
    RequestId = $requestId
    Difficulty = @{ Beginner = 0; Standard = 1; Proud = 2 }[$Difficulty]
    FastBoot = !$UseSaveProfile.IsPresent
    PlayStyle = $PlayStyle
    StartingItem = $StartingItem
    Party = $Party
}
if ($PSBoundParameters.ContainsKey('SongId')) { $request.SongId = $SongId }
[IO.File]::WriteAllText($requestPath + '.tmp', ($request | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[IO.File]::Move($requestPath + '.tmp', $requestPath)
$dispatchedAt = [DateTime]::UtcNow
try {
    Start-Process "com.epicgames.launcher://apps/$($manifest.AppName)?action=launch&silent=true"
} catch {
    # Remove only our own unconsumed request if dispatch failed.
    if (Test-Path -LiteralPath $requestPath) {
        $pending = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
        if ($pending.RequestId -eq $requestId) { Remove-Item -LiteralPath $requestPath }
    }
    throw
}
Write-Host "Requested MoM song launch $requestId ($Difficulty)."
if ($NoWait) {
    [pscustomobject]@{ RequestId = $requestId; status = 'dispatched'; resultPath = $resultPath }
    return
}
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$previousStep = ''
while ([DateTime]::UtcNow -lt $deadline) {
    $result = $null
    if (Test-Path -LiteralPath $resultPath) {
        try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
    }
    if ($result -and $result.RequestId -eq $requestId) {
        if ($result.step -ne $previousStep) {
            Write-Host "$($result.step): $($result.message)"
            $previousStep = $result.step
        }
        if ($result.status -eq 'ready') {
            $result | Add-Member -NotePropertyName launchElapsedSeconds -NotePropertyValue ([DateTime]::UtcNow - $dispatchedAt).TotalSeconds
            $result
            return
        }
        if ($result.status -eq 'failed') { throw $result.message }
    }
    Start-Sleep -Milliseconds 500
}
if (Test-Path -LiteralPath $requestPath) {
    $pending = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
    if ($pending.RequestId -eq $requestId) { Remove-Item -LiteralPath $requestPath }
}
throw "Timed out waiting for MoM request $requestId. Inspect $resultPath and MelonLoader\Latest.log."
