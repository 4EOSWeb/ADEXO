BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..' 'ADEXO.Registration.psm1') -Force
    $script:ExistingFile = Join-Path ([System.IO.Path]::GetTempPath()) 'adexo-registration-test-target.exe'
    if (-not (Test-Path -LiteralPath $script:ExistingFile)) {
        Set-Content -LiteralPath $script:ExistingFile -Value 'test' -Encoding utf8
    }
}

AfterAll {
    if (Test-Path -LiteralPath $script:ExistingFile -PathType Leaf) {
        Remove-Item -LiteralPath $script:ExistingFile -Force
    }
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

    It 'fingerprints values independently of their order' {
        $left = Get-AdexoAdminContextMenuFingerprint -Values @('1,a', '14,b')
        $right = Get-AdexoAdminContextMenuFingerprint -Values @('14,b', '1,a')
        $left | Should -Be $right
        $different = Get-AdexoAdminContextMenuFingerprint -Values @('1,a', '14,c')
        $different | Should -Not -Be $left
    }
}

Describe 'ADEXO registration mutation' {
    BeforeEach {
        $script:Dn = 'CN=user-Display,CN=409,CN=DisplaySpecifiers,CN=Configuration,DC=example,DC=com'
        $script:Configured = @()
        $script:ReadCount = 0
        $script:NextValues = $null
        $script:Added = New-Object 'System.Collections.Generic.List[string]'
        $script:Removed = New-Object 'System.Collections.Generic.List[string]'
        $script:Exact = "200,Manage with ADEXO,$($script:ExistingFile)"

        Mock Resolve-AdexoConfigurationNamingContext { 'CN=Configuration,DC=example,DC=com' } -ModuleName ADEXO.Registration
        Mock Get-AdexoAdminContextMenu -ModuleName ADEXO.Registration {
            $script:ReadCount++
            if ($null -ne $script:NextValues -and $script:ReadCount -ge 2) {
                return @($script:NextValues)
            }
            return @($script:Configured)
        }
        Mock Add-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration {
            param($DisplaySpecifierDn, $Value)
            $script:Added.Add($Value)
            $script:Configured = @($script:Configured + $Value)
        }
        Mock Remove-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration {
            param($DisplaySpecifierDn, $Value)
            $script:Removed.Add($Value)
            $script:Configured = @($script:Configured | Where-Object { $_ -ne $Value })
        }
    }

    Context 'register' {
        It 'appends one ADEXO value when one unrelated value exists' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}')
            $record = Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false
            $record.verificationResult | Should -Be 'verified'
            $record.order | Should -Be 200
            $record.caption | Should -Be 'Manage with ADEXO'
            $record.exactValue | Should -Be $script:Exact
            $record.displaySpecifierDn | Should -Be $script:Dn
            $record.locale | Should -Be '409'
            $script:Added.Count | Should -Be 1
            $script:Added[0] | Should -Be $script:Exact
            $script:Configured.Count | Should -Be 2
        }

        It 'preserves multiple unrelated values' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', '14,Legacy,C:\Tools\Legacy.exe')
            $null = Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false
            $script:Configured | Should -Contain '1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}'
            $script:Configured | Should -Contain '14,Legacy,C:\Tools\Legacy.exe'
            $script:Configured.Count | Should -Be 3
        }

        It 'refuses a duplicate exact registration' {
            $script:Configured = @($script:Exact)
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -Order 200 -AllowLabPath -Confirm:$false } |
                Should -Throw '*duplicate-exact-value*'
            $script:Added.Count | Should -Be 0
        }

        It 'refuses a duplicate registration that moved to another order' {
            $script:Configured = @("201,Manage with ADEXO,$($script:ExistingFile)")
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false } |
                Should -Throw '*duplicate-registration*'
            $script:Added.Count | Should -Be 0
        }

        It 'refuses an occupied order' {
            $script:Configured = @("200,Other tool,$($script:ExistingFile)")
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -Order 200 -AllowLabPath -Confirm:$false } |
                Should -Throw '*order-collision*'
            $script:Added.Count | Should -Be 0
        }

        It 'refuses an order outside the reserved range' {
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -Order 14 -AllowLabPath -Confirm:$false } |
                Should -Throw '*order-out-of-range*'
            $script:Added.Count | Should -Be 0
        }

        It 'selects the next available order' {
            $script:Configured = @("200,Other,$($script:ExistingFile)", "201,Another,$($script:ExistingFile)")
            $record = Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false
            $record.order | Should -Be 202
        }

        It 'performs no write under WhatIf' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}')
            $record = Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -WhatIf
            $record.verificationResult | Should -Be 'planned'
            $script:Added.Count | Should -Be 0
            $script:Configured.Count | Should -Be 1
            Should -Invoke Add-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration -Times 0 -Exactly
        }

        It 'fails loudly when post-write verification does not see the new value' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}')
            Mock Add-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration { param($DisplaySpecifierDn, $Value) }
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false } |
                Should -Throw '*verification failed*'
        }

        It 'fails when the directory denies the write' {
            Mock Add-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration { throw 'Access is denied.' }
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false } |
                Should -Throw '*Access is denied*'
            $script:Configured.Count | Should -Be 0
            Should -Invoke Add-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration -Times 1 -Exactly
        }

        It 'aborts when the value changes between the initial read and the mutation' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}')
            $script:NextValues = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', '77,Concurrent,C:\Tools\New.exe')
            { Invoke-AdexoRegistration -ExecutablePath $script:ExistingFile -Locale '409' -AllowLabPath -Confirm:$false } |
                Should -Throw '*concurrent-change*'
            $script:Added.Count | Should -Be 0
        }

        It 'refuses a missing executable' {
            { Invoke-AdexoRegistration -ExecutablePath 'C:\src\ADEXO-lab\absent.exe' -Locale '409' -AllowLabPath -Confirm:$false } |
                Should -Throw '*missing-executable*'
        }
    }

    Context 'unregister' {
        It 'removes exactly the recorded value' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', $script:Exact)
            $result = Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false
            $result.verificationResult | Should -Be 'verified'
            $script:Removed.Count | Should -Be 1
            $script:Removed[0] | Should -Be $script:Exact
            $script:Configured.Count | Should -Be 1
        }

        It 'preserves unrelated values' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', '14,Legacy,C:\Tools\Legacy.exe', $script:Exact)
            $null = Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false
            $script:Configured | Should -Contain '1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}'
            $script:Configured | Should -Contain '14,Legacy,C:\Tools\Legacy.exe'
        }

        It 'preserves a same-executable value that has a different caption' {
            $other = "200,Other caption,$($script:ExistingFile)"
            $script:Configured = @($other)
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*ambiguous-adexo-value*'
            $script:Removed.Count | Should -Be 0
            $script:Configured | Should -Contain $other
        }

        It 'preserves a same-executable value that has a different order' {
            $other = "201,Manage with ADEXO,$($script:ExistingFile)"
            $script:Configured = @($other)
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*ambiguous-adexo-value*'
            $script:Removed.Count | Should -Be 0
        }

        It 'preserves a same-caption value that has a different executable' {
            $other = '200,Manage with ADEXO,C:\Program Files\ADEXO\ADEXO.exe'
            $script:Configured = @($other)
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*ambiguous-adexo-value*'
            $script:Removed.Count | Should -Be 0
        }

        It 'refuses an ambiguous duplicate registration' {
            $script:Configured = @($script:Exact, $script:Exact)
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*ambiguous-adexo-value*'
            $script:Removed.Count | Should -Be 0
        }

        It 'handles a missing registration without mutating' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}')
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*missing-adexo-value*'
            $script:Removed.Count | Should -Be 0
            $script:Configured.Count | Should -Be 1
        }

        It 'performs no write under WhatIf' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', $script:Exact)
            $result = Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -WhatIf
            $result.verificationResult | Should -Be 'planned'
            $script:Removed.Count | Should -Be 0
            $script:Configured.Count | Should -Be 2
            Should -Invoke Remove-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration -Times 0 -Exactly
        }

        It 'fails loudly when post-removal verification still sees the value' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', $script:Exact)
            Mock Remove-AdexoAdminContextMenuValue -ModuleName ADEXO.Registration { param($DisplaySpecifierDn, $Value) }
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*verification failed*'
        }

        It 'preserves a concurrent change by aborting' {
            $script:Configured = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', $script:Exact)
            $script:NextValues = @('1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}', $script:Exact, '77,Concurrent,C:\Tools\New.exe')
            { Invoke-AdexoUnregistration -ExactValue $script:Exact -Locale '409' -Confirm:$false } |
                Should -Throw '*concurrent-change*'
            $script:Removed.Count | Should -Be 0
        }
    }
}

Describe 'ADEXO registration scripts forward WhatIf and Confirm' {
    BeforeAll {
        $script:ScriptRoot = Join-Path $PSScriptRoot '..'
        $script:RegisterScript = Join-Path $script:ScriptRoot 'Register-ADEXOForest.ps1'
        $script:UnregisterScript = Join-Path $script:ScriptRoot 'Unregister-ADEXOForest.ps1'
    }

    # These assertions read the script text only. Running the scripts from a unit
    # test would import a fresh copy of the module and bypass the mocks, which
    # would send a real read to whatever domain the test machine can reach.

    It 'defines the register script as ShouldProcess with a high impact' {
        $text = Get-Content -LiteralPath $script:RegisterScript -Raw
        $text | Should -Match 'SupportsShouldProcess\s*=\s*\$true'
        $text | Should -Match "ConfirmImpact\s*=\s*'High'"
    }

    It 'defines the unregister script as ShouldProcess with a high impact' {
        $text = Get-Content -LiteralPath $script:UnregisterScript -Raw
        $text | Should -Match 'SupportsShouldProcess\s*=\s*\$true'
        $text | Should -Match "ConfirmImpact\s*=\s*'High'"
    }

    It 'forwards the WhatIf intent from the register script into the module call' {
        $text = Get-Content -LiteralPath $script:RegisterScript -Raw
        $text | Should -Match '\$WhatIfPreference'
        $text | Should -Match "\`$invokeParameters\['WhatIf'\]\s*=\s*\`$true"
        $text | Should -Match 'Invoke-AdexoRegistration\s+@invokeParameters'
    }

    It 'forwards the WhatIf intent from the unregister script into the module call' {
        $text = Get-Content -LiteralPath $script:UnregisterScript -Raw
        $text | Should -Match '\$WhatIfPreference'
        $text | Should -Match "\`$invokeParameters\['WhatIf'\]\s*=\s*\`$true"
        $text | Should -Match 'Invoke-AdexoUnregistration\s+@invokeParameters'
    }

    It 'requires an exact value or a record for unregistration' {
        $text = Get-Content -LiteralPath $script:UnregisterScript -Raw
        $text | Should -Match 'RecordPath'
        $text | Should -Match 'never guesses'
    }
}
