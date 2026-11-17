BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'ADEXO.Registration.psm1') -Force
}

Describe 'ADEXO registration identity' {
    It 'quotes an executable path that contains spaces' {
        $command = Format-AdexoCommandValue -NormalizedPath 'C:\Program Files\ADEXO\ADEXO.exe'
        $command | Should -Be '"C:\Program Files\ADEXO\ADEXO.exe"'
    }

    It 'builds one exact menu value and selects a free order' {
        $order = Select-AdexoMenuOrder -ExistingValues @('200,Other,C:\Tools\Other.exe')
        $order | Should -Be 201
        $value = New-AdexoAdminContextMenuValue -Order $order -Caption 'Manage with ADEXO' -Command '"C:\Program Files\ADEXO\ADEXO.exe"'
        $value | Should -Be '201,Manage with ADEXO,"C:\Program Files\ADEXO\ADEXO.exe"'
    }

    It 'removes only the exact value' {
        $exact = '201,Manage with ADEXO,"C:\Program Files\ADEXO\ADEXO.exe"'
        $other = '14,Existing,C:\Windows\System32\other.exe'
        $decision = Resolve-AdexoUnregistration -ExistingValues @($other, $exact) -ExactValue $exact
        $decision.Action | Should -Be 'Remove'
        $decision.Value | Should -Be $exact
    }

    It 'stops when the same executable has a different order or caption' {
        $recorded = '201,Manage with ADEXO,"C:\Program Files\ADEXO\ADEXO.exe"'
        $otherOrder = '202,Manage with ADEXO,"C:\Program Files\ADEXO\ADEXO.exe"'
        $decision = Resolve-AdexoUnregistration -ExistingValues @($otherOrder) -ExactValue $recorded
        $decision.Action | Should -Be 'Ambiguous'
    }

    It 'preserves an unrelated ADEXO-like entry only when it is not the recorded value and reports ambiguity' {
        $decision = Resolve-AdexoUnregistration -ExistingValues @('14,Other ADEXO tool,C:\Tools\Other.exe') -ExactValue '201,Manage with ADEXO,C:\Program Files\ADEXO\ADEXO.exe'
        $decision.Action | Should -Be 'Ambiguous'
        $decision.Value | Should -Be $null
    }

    It 'requires an explicit locale' {
        { Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext 'CN=Configuration,DC=example,DC=com' -Locale '' } | Should -Throw
        $dn = Get-AdexoDisplaySpecifierDn -ConfigurationNamingContext 'CN=Configuration,DC=example,DC=com' -Locale '409'
        $dn | Should -Be 'CN=user-Display,CN=409,CN=DisplaySpecifiers,CN=Configuration,DC=example,DC=com'
    }

    It 'rejects a relative path and a UNC path' {
        (Test-AdexoExecutablePath -Path 'ADEXO.exe').Reason | Should -Be 'relative-path'
        (Test-AdexoExecutablePath -Path '\\server\share\ADEXO.exe').Reason | Should -Be 'unc-path'
    }

    It 'rejects a malformed backup' {
        (Test-AdexoBackup -Json '{').Accepted | Should -BeFalse
        (Test-AdexoBackup -Json '{"schemaVersion":1}').Reason | Should -Be 'malformed'
    }
}
