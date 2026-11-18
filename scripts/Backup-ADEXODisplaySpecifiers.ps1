#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $BackupDirectory,
    [Parameter(Mandatory = $true)][string] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force
$checked = Test-AdexoBackup -Json $Json
if (-not $checked.Accepted) { throw 'The backup document is malformed.' }
if (-not (Test-Path -LiteralPath $BackupDirectory)) {
    New-Item -ItemType Directory -Path $BackupDirectory | Out-Null
}
$path = Join-Path $BackupDirectory ("adexo-display-specifier-{0}.json" -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
Set-Content -LiteralPath $path -Value $Json -Encoding utf8
Write-Output $path
