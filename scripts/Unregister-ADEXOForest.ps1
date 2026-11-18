#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $ExactValue,
    [string] $RecordPath,
    [Parameter(Mandatory = $true)][string] $Locale,
    [string] $ConfigurationNamingContext
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) { throw 'ADEXO forest unregistration runs on Windows only.' }
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force

if ([string]::IsNullOrWhiteSpace($ExactValue) -and [string]::IsNullOrWhiteSpace($RecordPath)) {
    throw 'Unregistration needs -ExactValue or -RecordPath. It never guesses from a filename, path, caption, or order.'
}
if ([string]::IsNullOrWhiteSpace($ExactValue)) {
    if (-not (Test-Path -LiteralPath $RecordPath -PathType Leaf)) {
        throw "The registration record was not found: $RecordPath"
    }
    $document = Get-Content -LiteralPath $RecordPath -Raw | ConvertFrom-Json
    $ExactValue = [string]$document.exactValue
    if ([string]::IsNullOrWhiteSpace($ExactValue)) {
        throw 'The registration record does not contain an exactValue.'
    }
}

$result = Invoke-AdexoUnregistration `
    -ExactValue $ExactValue `
    -Locale $Locale `
    -ConfigurationNamingContext $ConfigurationNamingContext

$result | ConvertTo-Json -Depth 5
