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
    try { $candidate = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json } catch { continue }
    if (!$candidate.LaunchExecutable -or !$candidate.InstallLocation -or !$candidate.AppName) { continue }
    $executable = $candidate.LaunchExecutable.Trim()
    if ([IO.Path]::GetFileName($executable) -ne 'KINGDOM HEARTS Melody of Memory.exe') { continue }
    if (![IO.Path]::IsPathRooted($executable)) { $executable = Join-Path $candidate.InstallLocation.Trim() $executable }
    if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { continue }
    $root = Split-Path -Parent (Resolve-Path -LiteralPath $executable).Path
    if ($GameDir -and $root -ne (Resolve-Path -LiteralPath $GameDir).Path) { continue }
    $manifest = $candidate
    break
}
if (!$manifest) { throw 'No matching Epic Melody of Memory installation found.' }
$GameDir = $root
$gameExe = Join-Path $GameDir 'KINGDOM HEARTS Melody of Memory.exe'
if (!(Test-Path -LiteralPath $gameExe) -or
    !(Test-Path -LiteralPath (Join-Path $GameDir 'Mods\MoMController.dll'))) {
    throw 'Install the MoM controller/song-launch mod before launching a song.'
}
$userData = Join-Path $GameDir 'UserData'
$requestPath = Join-Path $userData 'MoMSongLaunch-request.json'
$resultPath = Join-Path $userData 'MoMSongLaunch-result.json'
$stateDir = Join-Path $GameDir '.kh3ap-mom'
function Invoke-WithLaunchLock([scriptblock]$Action) {
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    $stream = [IO.File]::Open((Join-Path $stateDir 'lock'), 'OpenOrCreate', 'ReadWrite', 'ReadWrite')
    try {
        $stream.Lock(0, 1)
        & $Action
    } finally {
        $stream.Dispose()
    }
}
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
function Remove-OwnPendingRequest {
    try {
        $pending = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
        if ($pending.RequestId -eq $requestId) { [IO.File]::Delete($requestPath) }
    } catch {
        Write-Verbose "Could not clear pending request ${requestId}: $_"
    }
}
$dispatchedAt = Invoke-WithLaunchLock {
    $running = @(Get-Process 'KINGDOM HEARTS Melody of Memory' -ErrorAction SilentlyContinue)
    foreach ($gameProcess in $running) {
        if ($gameProcess.Path -ne $gameExe) { throw 'Cannot verify the running MoM installation. Close MoM first.' }
        if (!$RestartGame) {
            throw 'MoM is already running. Close it first, or use -RestartGame to discard the current session.'
        }
        Stop-Process -Id $gameProcess.Id
        if (!$gameProcess.WaitForExit(10000)) { throw 'MoM did not exit.' }
    }
    New-Item -ItemType Directory -Path $userData -Force | Out-Null
    $temporary = "$requestPath.$requestId.tmp"
    try {
        [IO.File]::WriteAllText($temporary, ($request | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        if ([IO.File]::Exists($requestPath)) {
            Write-Host "Replacing the pending MoM song request at $requestPath."
            try { [IO.File]::Replace($temporary, $requestPath, [NullString]::Value) }
            catch [IO.FileNotFoundException] { [IO.File]::Move($temporary, $requestPath) }
        } else {
            [IO.File]::Move($temporary, $requestPath)
        }
        $appName = [Uri]::EscapeDataString($manifest.AppName.Trim())
        $started = [DateTime]::UtcNow
        try {
            Start-Process "com.epicgames.launcher://apps/${appName}?action=launch&silent=true"
        } catch {
            Remove-OwnPendingRequest
            throw
        }
        $started
    } finally {
        [IO.File]::Delete($temporary)
    }
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
try { Invoke-WithLaunchLock { Remove-OwnPendingRequest } } catch { Write-Verbose "Could not check pending request: $_" }
throw "Timed out waiting for MoM request $requestId. Inspect $resultPath and MelonLoader\Latest.log."
