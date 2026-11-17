#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string] $ExecutablePath,
    [Parameter(Mandatory = $true)][string] $Locale,
    [Parameter(Mandatory = $true)][string] $ConfigurationNamingContext,
    [string[]] $ExistingValues = @(),
    [string] $BackupDirectory,
    [switch] $AllowLabPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) { throw 'ADEXO forest registration runs on Windows only.' }
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force
$plan = New-AdexoRegistrationPlan -ExecutablePath $ExecutablePath -Locale $Locale -ConfigurationNamingContext $ConfigurationNamingContext -ExistingValues $ExistingValues -AllowLabPath:$AllowLabPath
if (-not $plan.Accepted) { throw "Registration refused: $($plan.Reason)" }
if ($PSCmdlet.ShouldProcess($plan.DisplaySpecifierDn, 'Append ADEXO adminContextMenu value')) {
    Write-Verbose "Append-only value $($plan.ExactValue)"
}
$plan | ConvertTo-Json -Depth 5
