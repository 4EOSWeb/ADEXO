#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string] $ExecutablePath,
    [Parameter(Mandatory = $true)][string] $Locale,
    [string] $ConfigurationNamingContext,
    [int] $Order,
    [string] $TransactionRecordPath,
    [switch] $AllowLabPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) { throw 'ADEXO forest registration runs on Windows only.' }
Import-Module (Join-Path $PSScriptRoot 'ADEXO.Registration.psm1') -Force

$invokeParameters = @{
    ExecutablePath = $ExecutablePath
    Locale = $Locale
    ConfigurationNamingContext = $ConfigurationNamingContext
    AllowLabPath = $AllowLabPath
}
if ($PSBoundParameters.ContainsKey('Order')) { $invokeParameters['Order'] = $Order }

# The preference variables are not shared across the module boundary, so the
# WhatIf and Confirm intent of this script must be forwarded explicitly.
if ($WhatIfPreference) { $invokeParameters['WhatIf'] = $true }
if ($PSBoundParameters.ContainsKey('Confirm')) { $invokeParameters['Confirm'] = $PSBoundParameters['Confirm'] }

$record = Invoke-AdexoRegistration @invokeParameters

if (-not [string]::IsNullOrWhiteSpace($TransactionRecordPath) -and $record.verificationResult -eq 'verified') {
    $directory = Split-Path -Parent $TransactionRecordPath
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    Set-Content -LiteralPath $TransactionRecordPath -Value ($record | ConvertTo-Json -Depth 5) -Encoding utf8
}

$record | ConvertTo-Json -Depth 5
