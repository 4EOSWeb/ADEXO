Set-StrictMode -Version Latest

$Script:AdexoCaption = 'Manage with ADEXO'
$Script:AdexoScriptVersion = '2.1.0'
$Script:AdexoOrderStart = 200
$Script:AdexoOrderEnd = 249
$Script:AdexoValueSeparator = [string][char]31

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

function Get-AdexoMenuOrderFromValue {
    [CmdletBinding()]
    param([string] $Value)

    if ($Value -match '^(\d+),') {
        return [int]$Matches[1]
    }
    return $null
}

function Select-AdexoMenuOrder {
    [CmdletBinding()]
    param(
        [string[]] $ExistingValues = @()
    )

    $used = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($value in $ExistingValues) {
        $order = Get-AdexoMenuOrderFromValue -Value $value
        if ($null -ne $order) {
            [void]$used.Add($order)
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

function ConvertFrom-AdexoAdminContextMenuValue {
    [CmdletBinding()]
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }
    if ($Value -match '^(?<order>\d+),(?<caption>[^,]+),(?<command>.+)$') {
        return [pscustomobject]@{
            Order = [int]$Matches['order']
            Caption = $Matches['caption']
            Command = $Matches['command']
        }
    }
    return $null
}

function Get-AdexoComparableCommand {
    [CmdletBinding()]
    param([string] $Command)

    if ([string]::IsNullOrWhiteSpace($Command)) {
        return $null
    }
    try {
        return Get-AdexoNormalizedPath -Command $Command
    }
    catch {
        return $Command.Trim().Trim('"')
    }
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

function Assert-AdexoDirectoryTools {
    [CmdletBinding()]
    param()

    if ($null -ne (Get-Command -Name 'Get-ADRootDSE' -ErrorAction SilentlyContinue)) {
        return
    }
    Import-Module -Name ActiveDirectory -ErrorAction Stop
}

function Resolve-AdexoConfigurationNamingContext {
    [CmdletBinding()]
    param([string] $ConfigurationNamingContext)

    if (-not [string]::IsNullOrWhiteSpace($ConfigurationNamingContext)) {
        return $ConfigurationNamingContext
    }
    Assert-AdexoDirectoryTools
    $root = Get-ADRootDSE -ErrorAction Stop
    $discovered = [string]$root.configurationNamingContext
    if ([string]::IsNullOrWhiteSpace($discovered)) {
        throw 'The RootDSE did not report a configurationNamingContext.'
    }
    return $discovered
}

function Get-AdexoAdminContextMenu {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $DisplaySpecifierDn
    )

    Assert-AdexoDirectoryTools
    $object = Get-ADObject -Identity $DisplaySpecifierDn -Properties adminContextMenu -ErrorAction Stop
    $property = $object.PSObject.Properties['adminContextMenu']
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }
    return @($property.Value | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
}

function Add-AdexoAdminContextMenuValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $DisplaySpecifierDn,
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    Assert-AdexoDirectoryTools
    Set-ADObject -Identity $DisplaySpecifierDn -Add @{ adminContextMenu = $Value } -ErrorAction Stop
}

function Remove-AdexoAdminContextMenuValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $DisplaySpecifierDn,
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    Assert-AdexoDirectoryTools
    Set-ADObject -Identity $DisplaySpecifierDn -Remove @{ adminContextMenu = $Value } -ErrorAction Stop
}

function Get-AdexoAdminContextMenuFingerprint {
    [CmdletBinding()]
    param([string[]] $Values = @())

    $sorted = @($Values | ForEach-Object { [string]$_ } | Sort-Object -CaseSensitive)
    return ('{0}|{1}' -f $sorted.Count, ($sorted -join $Script:AdexoValueSeparator))
}

function Get-AdexoRegistrationConflict {
    [CmdletBinding()]
    param(
        [string[]] $ExistingValues = @(),
        [Parameter(Mandatory = $true)]
        [string] $ExactValue,
        [Parameter(Mandatory = $true)]
        [int] $Order
    )

    if (@($ExistingValues | Where-Object { $_ -eq $ExactValue }).Count -gt 0) {
        return [pscustomobject]@{ Conflict = $true; Reason = 'duplicate-exact-value' }
    }

    $proposed = ConvertFrom-AdexoAdminContextMenuValue -Value $ExactValue
    $proposedCommand = Get-AdexoComparableCommand -Command $proposed.Command

    foreach ($value in $ExistingValues) {
        $existingOrder = Get-AdexoMenuOrderFromValue -Value $value
        if ($null -ne $existingOrder -and $existingOrder -eq $Order) {
            return [pscustomobject]@{ Conflict = $true; Reason = 'order-collision' }
        }
        if ($null -eq $proposed) { continue }
        $existing = ConvertFrom-AdexoAdminContextMenuValue -Value $value
        if ($null -eq $existing) { continue }
        if ($existing.Caption -ne $proposed.Caption) { continue }
        if ((Get-AdexoComparableCommand -Command $existing.Command) -ne $proposedCommand) { continue }
        return [pscustomobject]@{ Conflict = $true; Reason = 'duplicate-registration' }
    }

    return [pscustomobject]@{ Conflict = $false; Reason = $null }
}

function Get-AdexoOperatorName {
    [CmdletBinding()]
    param()

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        if ($null -ne $identity -and -not [string]::IsNullOrWhiteSpace($identity.Name)) {
            return $identity.Name
        }
    }
    catch {
        return [string]$env:USERNAME
    }
    return [string]$env:USERNAME
}

function New-AdexoRegistrationRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $ConfigurationNamingContext,
        [Parameter(Mandatory = $true)][string] $DisplaySpecifierDn,
        [Parameter(Mandatory = $true)][string] $Locale,
        [Parameter(Mandatory = $true)][int] $Order,
        [Parameter(Mandatory = $true)][string] $Caption,
        [Parameter(Mandatory = $true)][string] $ExactValue,
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $VerificationResult
    )

    return [pscustomobject]@{
        schemaVersion = 1
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        configurationNamingContext = $ConfigurationNamingContext
        displaySpecifierDn = $DisplaySpecifierDn
        locale = $Locale
        order = $Order
        caption = $Caption
        exactValue = $ExactValue
        executablePath = $ExecutablePath
        operator = Get-AdexoOperatorName
        machine = [Environment]::MachineName
        scriptVersion = $Script:AdexoScriptVersion
        verificationResult = $VerificationResult
    }
}

function New-AdexoRegistrationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $Locale,
        [Parameter(Mandatory = $true)][string] $ConfigurationNamingContext,
        [string[]] $ExistingValues = @(),
        [int] $Order,
        [switch] $AllowLabPath
    )

    $checked = Test-AdexoExecutablePath -Path $ExecutablePath -AllowLabPath:$AllowLabPath
    if (-not $checked.Accepted) {
        return [pscustomobject]@{ Accepted = $false; Reason = $checked.Reason }
    }
    if ($PSBoundParameters.ContainsKey('Order') -and $Order -gt 0) {
        if ($Order -lt $Script:AdexoOrderStart -or $Order -gt $Script:AdexoOrderEnd) {
            return [pscustomobject]@{ Accepted = $false; Reason = 'order-out-of-range' }
        }
        $selected = $Order
    }
    else {
        $selected = Select-AdexoMenuOrder -ExistingValues $ExistingValues
    }
    $command = Format-AdexoCommandValue -NormalizedPath $checked.NormalizedPath
    $value = New-AdexoAdminContextMenuValue -Order $selected -Caption $Script:AdexoCaption -Command $command
    $dn = Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext $ConfigurationNamingContext -Locale $Locale
    return [pscustomobject]@{
        Accepted = $true
        SchemaVersion = 1
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Locale = $Locale
        DisplaySpecifierDn = $dn
        Order = $selected
        Caption = $Script:AdexoCaption
        ExactValue = $value
        ExecutablePath = $checked.NormalizedPath
        ScriptVersion = $Script:AdexoScriptVersion
        OriginalValues = @($ExistingValues)
        VerificationResult = 'planned'
    }
}

function Invoke-AdexoRegistration {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $Locale,
        [string] $ConfigurationNamingContext,
        [int] $Order,
        [switch] $AllowLabPath
    )

    $namingContext = Resolve-AdexoConfigurationNamingContext -ConfigurationNamingContext $ConfigurationNamingContext
    $before = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn (Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext $namingContext -Locale $Locale))

    $planParameters = @{
        ExecutablePath = $ExecutablePath
        Locale = $Locale
        ConfigurationNamingContext = $namingContext
        ExistingValues = $before
        AllowLabPath = $AllowLabPath
    }
    if ($PSBoundParameters.ContainsKey('Order')) { $planParameters['Order'] = $Order }
    $plan = New-AdexoRegistrationPlan @planParameters
    if (-not $plan.Accepted) {
        throw "Registration refused: $($plan.Reason)"
    }

    $conflict = Get-AdexoRegistrationConflict -ExistingValues $before -ExactValue $plan.ExactValue -Order $plan.Order
    if ($conflict.Conflict) {
        throw "Registration refused: $($conflict.Reason)"
    }

    $record = @{
        ConfigurationNamingContext = $namingContext
        DisplaySpecifierDn = $plan.DisplaySpecifierDn
        Locale = $Locale
        Order = $plan.Order
        Caption = $plan.Caption
        ExactValue = $plan.ExactValue
        ExecutablePath = $plan.ExecutablePath
    }

    if (-not $PSCmdlet.ShouldProcess($plan.DisplaySpecifierDn, 'Append ADEXO adminContextMenu value')) {
        return New-AdexoRegistrationRecord @record -VerificationResult 'planned'
    }

    $live = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn $plan.DisplaySpecifierDn)
    if ((Get-AdexoAdminContextMenuFingerprint -Values $live) -ne (Get-AdexoAdminContextMenuFingerprint -Values $before)) {
        throw 'Registration aborted: concurrent-change. adminContextMenu changed after the preflight read.'
    }
    $recheck = Get-AdexoRegistrationConflict -ExistingValues $live -ExactValue $plan.ExactValue -Order $plan.Order
    if ($recheck.Conflict) {
        throw "Registration aborted: $($recheck.Reason)"
    }

    Add-AdexoAdminContextMenuValue -DisplaySpecifierDn $plan.DisplaySpecifierDn -Value $plan.ExactValue

    $after = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn $plan.DisplaySpecifierDn)
    if (@($after | Where-Object { $_ -eq $plan.ExactValue }).Count -ne 1) {
        throw "Registration verification failed: the exact ADEXO value is not present exactly once. Observed: $($after -join ' | ')"
    }
    foreach ($value in $before) {
        if (@($after | Where-Object { $_ -eq $value }).Count -lt 1) {
            throw "Registration verification failed: the pre-existing value '$value' is missing. Observed: $($after -join ' | ')"
        }
    }

    return New-AdexoRegistrationRecord @record -VerificationResult 'verified'
}

function Invoke-AdexoUnregistration {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)][string] $ExactValue,
        [Parameter(Mandatory = $true)][string] $Locale,
        [string] $ConfigurationNamingContext
    )

    $namingContext = Resolve-AdexoConfigurationNamingContext -ConfigurationNamingContext $ConfigurationNamingContext
    $dn = Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext $namingContext -Locale $Locale
    $before = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn $dn)

    $decision = Resolve-AdexoUnregistration -ExistingValues $before -ExactValue $ExactValue
    if ($decision.Action -eq 'Ambiguous') {
        throw 'Unregistration refused: ambiguous-adexo-value. Pass the exact recorded registration value.'
    }
    if ($decision.Action -eq 'Missing') {
        throw 'Unregistration refused: missing-adexo-value. The exact recorded value is not present.'
    }

    if (-not $PSCmdlet.ShouldProcess($dn, 'Remove exact ADEXO adminContextMenu value')) {
        return [pscustomobject]@{
            schemaVersion = 1
            timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
            configurationNamingContext = $namingContext
            displaySpecifierDn = $dn
            locale = $Locale
            exactValue = $ExactValue
            operator = Get-AdexoOperatorName
            machine = [Environment]::MachineName
            scriptVersion = $Script:AdexoScriptVersion
            verificationResult = 'planned'
        }
    }

    $live = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn $dn)
    if ((Get-AdexoAdminContextMenuFingerprint -Values $live) -ne (Get-AdexoAdminContextMenuFingerprint -Values $before)) {
        throw 'Unregistration aborted: concurrent-change. adminContextMenu changed after the preflight read.'
    }
    $recheck = Resolve-AdexoUnregistration -ExistingValues $live -ExactValue $ExactValue
    if ($recheck.Action -ne 'Remove') {
        throw "Unregistration aborted: $($recheck.Action)"
    }

    Remove-AdexoAdminContextMenuValue -DisplaySpecifierDn $dn -Value $ExactValue

    $after = @(Get-AdexoAdminContextMenu -DisplaySpecifierDn $dn)
    if (@($after | Where-Object { $_ -eq $ExactValue }).Count -ne 0) {
        throw "Unregistration verification failed: the exact ADEXO value is still present. Observed: $($after -join ' | ')"
    }
    foreach ($value in $before) {
        if ($value -eq $ExactValue) { continue }
        if (@($after | Where-Object { $_ -eq $value }).Count -lt 1) {
            throw "Unregistration verification failed: the unrelated value '$value' is missing. Observed: $($after -join ' | ')"
        }
    }

    return [pscustomobject]@{
        schemaVersion = 1
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        configurationNamingContext = $namingContext
        displaySpecifierDn = $dn
        locale = $Locale
        exactValue = $ExactValue
        operator = Get-AdexoOperatorName
        machine = [Environment]::MachineName
        scriptVersion = $Script:AdexoScriptVersion
        verificationResult = 'verified'
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
