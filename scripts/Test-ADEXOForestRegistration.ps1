#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExactValue,
    [string[]] $ExistingValues = @(),
    [string] $Locale
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force
$decision = Resolve-AdexoUnregistration -ExistingValues $ExistingValues -ExactValue $ExactValue
[pscustomobject]@{
    Locale = $Locale
    Present = $decision.Action -eq 'Remove'
    Action = $decision.Action
    Note = 'Registration is locale-specific. This script does not scan every locale.'
} | ConvertTo-Json
if ($decision.Action -eq 'Ambiguous') { exit 2 }
if ($decision.Action -eq 'Missing') { exit 3 }
