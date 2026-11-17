Set-StrictMode -Version Latest

$Script:AdexoCaption = 'Manage with ADEXO'
$Script:AdexoScriptVersion = '2.0.0'
$Script:AdexoOrderStart = 200
$Script:AdexoOrderEnd = 249

function Test-AdexoExecutablePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [switch] $AllowLabPath
    )

    if ($Path.StartsWith('\\') -or $Path.StartsWith('//')) {
        return [pscustomobject]@{ Accepted = $false; Reason = 'unc-path' }
    }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [System.IO.Path]::IsPathRooted($Path)) {
        return [pscustomobject]@{ Accepted = $false; Reason = 'relative-path' }
    }
    $full = [System.IO.Path]::GetFullPath($Path)
    $temp = [System.IO.Path]::GetTempPath()
    if (-not $AllowLabPath -and $full.StartsWith($temp, [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Accepted = $false; Reason = 'temporary-path' }
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        return [pscustomobject]@{ Accepted = $false; Reason = 'missing-executable' }
    }
    return [pscustomobject]@{ Accepted = $true; Reason = 'accepted'; NormalizedPath = $full }
}

function Format-AdexoCommandValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $NormalizedPath
    )

    if ($NormalizedPath.Contains(' ')) {
        return '"' + $NormalizedPath + '"'
    }
    return $NormalizedPath
}

function New-AdexoAdminContextMenuValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [int] $Order,
        [Parameter(Mandatory = $true)]
        [string] $Caption,
        [Parameter(Mandatory = $true)]
        [string] $Command
    )

    return ('{0},{1},{2}' -f $Order, $Caption, $Command)
}

function Get-AdexoDisplaySpecifierDn {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConfigurationNamingContext,
        [Parameter(Mandatory = $true)]
        [string] $Locale,
        [string] $ObjectClass = 'user'
    )

    if ($Locale -notmatch '^[0-9A-Fa-f]{3,4}$') {
        throw 'A locale identifier such as 409 is required. ADEXO does not guess the ADUC locale.'
    }
    return ('CN={0}-Display,CN={1},CN=DisplaySpecifiers,{2}' -f $ObjectClass, $Locale, $ConfigurationNamingContext)
}

function Select-AdexoMenuOrder {
    [CmdletBinding()]
    param(
        [string[]] $ExistingValues = @()
    )

    $used = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($value in $ExistingValues) {
        if ($value -match '^(\d+),') {
            [void]$used.Add([int]$Matches[1])
        }
    }
    for ($order = $Script:AdexoOrderStart; $order -le $Script:AdexoOrderEnd; $order++) {
        if (-not $used.Contains($order)) {
            return $order
        }
    }
    throw 'No free menu order remains in the reserved range 200-249.'
}

function Get-AdexoNormalizedPath {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Command)
    $text = $Command.Trim().Trim('"')
    return [System.IO.Path]::GetFullPath($text)
}

function Resolve-AdexoUnregistration {
    [CmdletBinding()]
    param(
        [string[]] $ExistingValues = @(),
        [Parameter(Mandatory = $true)]
        [string] $ExactValue
    )

    $matches = @($ExistingValues | Where-Object { $_ -eq $ExactValue })
    if ($matches.Count -eq 1) {
        return [pscustomobject]@{ Action = 'Remove'; Value = $ExactValue; Candidates = @($ExactValue) }
    }
    if ($matches.Count -gt 1) {
        return [pscustomobject]@{ Action = 'Ambiguous'; Value = $null; Candidates = $matches }
    }

    $similar = @($ExistingValues | Where-Object { $_ -like '*ADEXO*' })
    if ($similar.Count -gt 0) {
        return [pscustomobject]@{ Action = 'Ambiguous'; Value = $null; Candidates = $similar }
    }
    return [pscustomobject]@{ Action = 'Missing'; Value = $null; Candidates = @() }
}

function New-AdexoRegistrationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $Locale,
        [Parameter(Mandatory = $true)][string] $ConfigurationNamingContext,
        [string[]] $ExistingValues = @(),
        [switch] $AllowLabPath
    )

    $checked = Test-AdexoExecutablePath -Path $ExecutablePath -AllowLabPath:$AllowLabPath
    if (-not $checked.Accepted) {
        return [pscustomobject]@{ Accepted = $false; Reason = $checked.Reason }
    }
    $order = Select-AdexoMenuOrder -ExistingValues $ExistingValues
    $command = Format-AdexoCommandValue -NormalizedPath $checked.NormalizedPath
    $value = New-AdexoAdminContextMenuValue -Order $order -Caption $Script:AdexoCaption -Command $command
    $dn = Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext $ConfigurationNamingContext -Locale $Locale
    return [pscustomobject]@{
        Accepted = $true
        SchemaVersion = 1
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Locale = $Locale
        DisplaySpecifierDn = $dn
        Order = $order
        Caption = $Script:AdexoCaption
        ExactValue = $value
        ExecutablePath = $checked.NormalizedPath
        ScriptVersion = $Script:AdexoScriptVersion
        OriginalValues = @($ExistingValues)
        VerificationResult = 'planned'
    }
}

function Test-AdexoBackup {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Json)
    try {
        $document = $Json | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{ Accepted = $false; Reason = 'malformed' }
    }
    foreach ($name in @('schemaVersion', 'timestampUtc', 'locale', 'displaySpecifierDn', 'exactValue', 'executablePath', 'scriptVersion')) {
        if (-not ($document.PSObject.Properties.Name -contains $name)) {
            return [pscustomobject]@{ Accepted = $false; Reason = 'malformed' }
        }
    }
    return [pscustomobject]@{ Accepted = $true; Reason = 'accepted'; Document = $document }
}

Export-ModuleMember -Function *-Adexo*
