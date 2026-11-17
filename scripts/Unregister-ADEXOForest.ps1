#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string] $ExactValue,
    [string[]] $ExistingValues = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) { throw 'ADEXO forest unregistration runs on Windows only.' }
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force
$decision = Resolve-AdexoUnregistration -ExistingValues $ExistingValues -ExactValue $ExactValue
if ($decision.Action -eq 'Ambiguous') { throw 'Unregistration is ambiguous. Pass the exact registration value.' }
if ($decision.Action -eq 'Missing') { throw 'The exact ADEXO registration value was not found.' }
if ($PSCmdlet.ShouldProcess($ExactValue, 'Remove exact ADEXO adminContextMenu value')) {
    Write-Verbose 'Removing one exact value.'
}
$decision | ConvertTo-Json -Depth 5
