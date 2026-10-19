[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageArgs = @((Join-Path $repoRoot 'tools\package_release.py'), 'package', '--game-dir', $GameDir)
if ($OutputRoot) { $packageArgs += @('--output', $OutputRoot) }
& python @packageArgs
if ($LASTEXITCODE -ne 0) { throw 'MoM source/binary packaging failed.' }
