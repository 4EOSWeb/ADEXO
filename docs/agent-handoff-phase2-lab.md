# ADEXO agent handoff: Phase 2 Windows domain lab

This document is temporary operational handoff material. It is the briefing for a Cursor agent running on the Windows worker `adexo-lab`. The receiving agent has no other conversation context.

Read this document, then read the repository. Where this document and the code disagree, the code at the recorded baseline is the implementation. Do not treat this file as permission to change that implementation.

## 1. What ADEXO is

ADEXO is a Windows administration companion for Active Directory Users and Computers. The working name is Hybrid User Manager for ADUC. The functional inspiration is a product in the same category as Easy365Manager: an administrator selects a directory object in ADUC and manages the related on-premises and cloud settings from one window.

The long-term product direction is:

```text
ADUC
  -> ADEXO
       -> Active Directory
       -> Microsoft Entra ID / Microsoft Graph
       -> Exchange Online
       -> optional Entra Connect
```

The intended product adds user-management views for hybrid administration so an operator does not have to keep switching among ADUC, the Microsoft 365 admin center, the Entra admin center, the Exchange admin center, and PowerShell.

Those cloud capabilities do not exist yet. Phase 2 does not call Microsoft Graph, Exchange Online, or Entra Connect.

## 2. Core architecture

ADEXO v1 does not load inside `mmc.exe`.

ADUC integration is a static `adminContextMenu` value on a locale-specific display specifier. ADUC launches an external executable with `ShellExecute`. Because the command is an application, ADUC appends the selected object's ADsPath and object class. ADEXO treats those two arguments as data.

```text
ADUC / mmc.exe
  -> static adminContextMenu
  -> ADEXO.exe
  -> Active Directory
  -> later Microsoft Graph
  -> later Exchange Online
  -> optional later Entra Connect
```

Cloud libraries, PowerShell, Microsoft Graph, Exchange Online, WPF, and the modern .NET runtime stay outside `mmc.exe`. In-process COM property pages and COM context-menu extensions are deferred.

The command stored in the menu is only the absolute path of the executable. It must not contain extra parameters. When that path contains spaces, the registration formatter wraps the path in quotes. The documented quoting example is `"C:\Program Files\ADEXO\ADEXO.exe"`. Actual argv tokenization from ADUC has not been captured.

## 3. Forest registration model

Installing the program and publishing the ADUC menu are separate operations.

The future machine install path is:

```text
C:\Program Files\ADEXO\ADEXO.exe
```

An MSI, when one exists, copies that file onto a management workstation. The MSI does not change the forest. No MSI has been built or installed in this lab.

Forest registration is performed only by explicit scripts:

- `scripts\Register-ADEXOForest.ps1`
- `scripts\Unregister-ADEXOForest.ps1`
- `scripts\Test-ADEXOForestRegistration.ps1`
- `scripts\Backup-ADEXODisplaySpecifiers.ps1`
- `scripts\ADEXO.Registration.psm1`

The application must never silently modify the Active Directory configuration partition.

The intended registration target is the locale-specific `user-Display` display specifier, attribute `adminContextMenu`. Locale `409` means English. It is an explicit choice, not a silent default.

Intended rules:

- Registration appends exactly one value.
- The value shape is `<order>,<menu text>,<command>`.
- The caption is `Manage with ADEXO`.
- The menu order is the first free integer from 200 through 249.
- Unregistration removes only that exact ADEXO value.
- Unrelated `adminContextMenu` values are never overwritten, replaced, or reordered.
- A stale backup is never restored over newer administrator changes.
- Replacing the whole `adminContextMenu` attribute is forbidden.

### What the scripts actually do at the Phase 2 baseline

At commit `d183b0a`, these scripts plan and print JSON. They do not read or write Active Directory by themselves.

- `Register-ADEXOForest.ps1` builds a plan with `New-AdexoRegistrationPlan`. Its `ShouldProcess` block only writes a verbose line. It then prints the plan. `VerificationResult` is `planned`. There is no `Set-ADObject` and no LDAP modify.
- `Unregister-ADEXOForest.ps1` decides whether one exact string is present in the caller-supplied list. Its `ShouldProcess` block only writes a verbose line. It does not remove a directory value.
- `Test-ADEXOForestRegistration.ps1` compares caller-supplied strings. It does not query the directory. Exit code 2 means ambiguous. Exit code 3 means missing.
- `Backup-ADEXODisplaySpecifiers.ps1` writes a caller-supplied JSON document to a local folder. It does not read the display specifier.

Running `Register-ADEXOForest.ps1` without `-WhatIf` still does not add the menu. Do not invent an LDAP write, a `Set-ADObject` call, or an `ADS_PROPERTY_APPEND` operation to finish the lab. If a real registration is required and these scripts cannot perform it, stop and report that as a defect. Do not patch it unless the user explicitly reopens Phase 2.

The caller must supply `-ExistingValues` from a read the agent performs. The scripts do not discover the current attribute.

## 4. Current project state

| Phase | State |
| --- | --- |
| Phase 0 | Architecture and documentation. Complete. |
| Phase 1 | Solution foundation. Complete. |
| Phase 2 | ADUC launch and read-only Active Directory identity. Implementation complete. Lab validation pending. |
| Phase 3 | Not started. |

Approved Phase 2 baseline:

```text
d183b0afcdacef4ef0f59e8a37b0a90d291a4862
```

Short commit: `d183b0a`

Subject: `Record the lab proof required to finish Phase 2.`

Branch that contains the implementation:

```text
cursor/adexo-phase-2-aduc-readonly-8d35
```

Parent implementation commit for the Windows cancellation fix, also green in CI: `1def79d` (`State that a stopped load does not cancel the LDAP request.`).

`README.md` currently says:

```text
Phase 2 implementation complete; Windows domain lab validation pending.
```

Leave that sentence unchanged.

Phase 2 is frozen. No additional Phase 2 source changes are allowed unless a real Windows or ADUC test identifies a defect. A defect is reported and stopped on. It is not casually patched.

The remaining gate is:

```text
WINDOWS DOMAIN LAB VALIDATION PENDING
```

Repository layout at the baseline includes `ADEXO.slnx`, `global.json` (SDK `10.0.100`, `rollForward` `latestFeature`), portable projects under `src\`, the WPF shell `src\ADEXO.Desktop`, tests under `tests\`, and registration scripts under `scripts\`.

## 5. Phase 1 foundation

Runtime: .NET 10 LTS, supported through November 14, 2028.

- Portable libraries and tests target `net10.0`.
- `ADEXO.Desktop` and `ADEXO.Desktop.Tests` target `net10.0-windows` with WPF (`UseWPF`).
- `ADEXO.Desktop` has no `AssemblyName`, so `dotnet publish` produces `ADEXO.Desktop.exe`, not `ADEXO.exe`.

The foundation includes:

- `AdObjectIdentity`. Equality is `objectGUID` only.
- `CloudObjectIdentity`
- `CloudRelationship`
- `SourceOfAuthority`
- `AuthoritativeValue<T>`
- `KnownFeatureId`
- `ExternalFeatureId`
- `ProviderCapability`

Provider contracts:

- `IActiveDirectoryProvider`
- `IGraphProvider`
- `IExchangeOnlineProvider`
- `IEntraConnectProvider`

Graph, Exchange Online, and Entra Connect providers are foundation contracts. Phase 2 does not implement live cloud calls.

Operation architecture: `IChangeOperation`.

The broader design for a future write is:

```text
read -> validate -> preview -> authorize -> execute -> re-read -> verify -> audit
```

Phase 2 does not execute a directory write through that pipeline.

`SourceOfAuthority` values:

- `ActiveDirectory`
- `MicrosoftEntra`
- `ExchangeOnline`
- `Calculated`
- `Unsupported`
- `Unknown`

`objectGUID` is the canonical Active Directory identity. UPN, SMTP address, `sAMAccountName`, and `distinguishedName` are not permanent identity keys.

## 6. Security model

The repository must not gain any of the following:

- hard-coded passwords
- hard-coded tokens
- hard-coded tenant secrets
- client secrets in source
- plaintext token storage
- tokens in logging
- an execution-policy bypass
- a TLS validation bypass
- a certificate-validation bypass
- `Invoke-Expression`
- an automatic Domain Admin assumption
- an automatic Global Admin assumption
- an automatic ACL weakening
- production fake providers
- cloud writes during Phase 2

`SecretText` renders as `[REDACTED]` from `ToString()` and from JSON serialization. It has no implicit conversion to `string`.

Phase 2 is read-only from the application's perspective. The public Active Directory provider exposes no Add, Modify, Delete, ModifyDN, password operation, or group modification.

LDAP uses `System.DirectoryServices.Protocols` with `AuthType.Negotiate`, signing, and sealing. There is no Basic or anonymous fallback. The desktop constructs the reader with a 15-second timeout. An in-flight synchronous LDAP call is bounded by that timeout. Canceling the view discards a late result. It does not mean the network request was reported as canceled. Do not change that wording to claim the request was aborted.

The server name inside an incoming ADsPath is untrusted. The connection uses a discovered domain controller, or the supplied host only when it matches that discovery. IP literals and arbitrary external hosts are rejected.

## 7. Phase 2 implementation

Phase 2 validates an ADUC launch with two arguments:

- argument 0: ADsPath
- argument 1: object class

Phase 2 supports only the class `user`.

The ADsPath is untrusted data. It must never become command text for `cmd.exe`, `powershell.exe`, `Process.Start` argument concatenation, a shell, or `Invoke-Expression`. Distinguished names are parsed structurally. A legitimate name such as `CN=O'Brien\, John` is data, not a reason to reject the launch.

The desktop shell is WPF and MVVM. With no arguments, the state is `NoLaunchRequest` and the status text is:

```text
Phase 2 opens a user from Active Directory Users and Computers.
```

Other view states include `Validating`, `Connecting`, `Resolving`, `VerifyingGuid`, `Loading`, `Loaded`, `UnsupportedClass`, `NotFound`, `AccessDenied`, `DomainUnavailable`, `ValidationFailure`, `Canceled`, `IdentityVerificationFailed`, and `UnknownFailure`.

`System.DirectoryServices` is not the LDAP client. `System.DirectoryServices.Protocols` is.

## 8. Identity resolution

This is the Phase 2 invariant.

```text
ADUC ADsPath
  -> validate
  -> initial read
  -> objectGUID A
  -> binary objectGUID LDAP lookup
  -> objectGUID B
  -> require A == B
  -> display the user
```

The binary filter is built from `Guid.ToByteArray()` as RFC 4515 hex escapes, for example `(objectGUID=\XX\XX...)`.

After objectGUID A is known, the original ADsPath is not the identity. ADEXO must not fall back to UPN, `sAMAccountName`, `mail`, `displayName`, or `distinguishedName`. If A does not equal B, identity resolution fails and the user is not shown.

A rename or move changes the distinguished name and leaves `objectGUID` unchanged. The same GUID remains the same object. That behavior is implemented and unit-tested. It has not been observed in this lab.

## 9. Read-only Phase 2 data

`LdapDirectoryReader.UserAttributes` requests:

- Identity: `objectGUID`, `objectSid`, `distinguishedName`, `canonicalName`, `objectClass`, `name`, `displayName`, `givenName`, `sn`, `initials`, `sAMAccountName`, `userPrincipalName`
- Account state: `userAccountControl`, `accountExpires`, `pwdLastSet`, `lockoutTime`, `badPwdCount`, `whenCreated`, `whenChanged`
- Contact: `mail`, `telephoneNumber`, `mobile`, `ipPhone`, `streetAddress`, `l`, `st`, `postalCode`, `co`
- Organization: `title`, `department`, `company`, `physicalDeliveryOfficeName`, `manager`
- Mail and hybrid attributes stored in AD: `mailNickname`, `proxyAddresses`, `targetAddress`, `msExchHideFromAddressLists`, `extensionAttribute1` through `extensionAttribute15`

`lastLogon` and `lastLogonTimestamp` are not requested. Do not present either as an exact current-login time.

Manager resolution is bounded and optional. A missing or unreadable manager is not a failed user identity.

## 10. Current CI state

CI on the Phase 2 branch was green at `1def79d` and again at `d183b0a`.

Workflow: `.github/workflows/ci.yml`

- `portable-domain-tests` on Linux: portable projects. Pass.
- `windows-build` on `windows-latest`: full solution, including the WPF shell, desktop view-model tests, and Pester. Pass.

Windows CI installs Pester at the exact version `6.2.0` and runs `scripts/tests`. Those tests are in-memory. They do not connect to a domain.

Normal CI does not register a display specifier and does not modify Active Directory.

## 11. Windows lab execution already checked

The worker `adexo-lab` is a personal My Machines worker for this repository. A command session on that worker already reported:

| Check | Result |
| --- | --- |
| hostname | `WIN-K0LHGHP41QM` |
| COMPUTERNAME | `WIN-K0LHGHP41QM` |
| whoami | `test\administrator` |
| repository | `C:\src\ADEXO` |
| branch | `cursor/adexo-phase-2-aduc-readonly-8d35` |
| HEAD | `d183b0afcdacef4ef0f59e8a37b0a90d291a4862` |
| git status | clean, up to date with origin |

That session was executing on the Windows worker, not on the Linux Cursor cloud VM. Reconfirm hostname, user, path, and HEAD before trusting the placement. If any of those differ, stop and report the difference.

Additional observations from that same lab session, all read-only:

- The Active Directory PowerShell module was present at version `1.0.1.0`.
- `dsa.msc` was present at `C:\WINDOWS\System32\dsa.msc` and `C:\WINDOWS\SysWOW64\dsa.msc`.
- `dotnet` was initially absent from `PATH`. The .NET 10 SDK was then installed with `winget install --id Microsoft.DotNet.SDK.10`. The operator reported that .NET worked and `dotnet test` succeeded. The exact `dotnet --version` string was not captured. Collect it again.
- `dotnet publish src\ADEXO.Desktop\ADEXO.Desktop.csproj -c Release -o C:\src\ADEXO-lab` wrote `C:\src\ADEXO-lab\ADEXO.Desktop.exe`.
- `C:\src\ADEXO-lab\ADEXO.exe` does not exist. A registration plan pointed at that missing path was refused with `missing-executable`.
- `C:\Program Files\ADEXO\ADEXO.exe` was not installed.
- A read of `CN=user-Display,CN=409,CN=DisplaySpecifiers,CN=Configuration,DC=test,DC=domain` returned one value: `1,{08eb4fa6-6ffd-11d1-b0e0-00c04fd8dca6}`.
- A `-WhatIf` plan at `2026-11-17T21:45:37.7619557Z` (4:45 PM America/Indianapolis) was accepted with order `200`, caption `Manage with ADEXO`, and exact value `200,Manage with ADEXO,C:\src\ADEXO-lab\ADEXO.Desktop.exe`.
- After that `-WhatIf`, `adminContextMenu` was still only the original value.
- No synthetic test user has been chosen.
- The no-argument GUI launch was not completed.
- The forest has not been classified. Seeing `DC=test,DC=domain` does not by itself prove the forest is disposable.

Do not repeat the publish unless the exe is missing. Do not add the menu while completing the preflight below.

## 12. Current task

The only current task is Phase 2 Windows domain lab validation.

Do not begin Phase 3. Do not implement cloud support. Do not modify Phase 2 source unless validation identifies a real defect, and even then stop for explicit user approval before patching.

## 13. Safety gate

Perform read-only discovery first. Classify the connected forest as one of:

- A. Isolated or disposable ADEXO lab, explicitly approved for one display-specifier append.
- B. Production or otherwise important domain.
- C. Unknown.

The name `test.domain` and the account `test\administrator` are observations. They are not the classification. If the operator has not explicitly approved this forest as disposable, the classification is C.

If the classification is B or C:

- Do not modify forest configuration.
- Read-only checks and `-WhatIf` only.
- Report `PRODUCTION OR UNVERIFIED FOREST - REGISTRATION NOT PERFORMED`.

Only classification A permits a later real display-specifier registration, and only after the mutation gate in this document has been reviewed by a person.

## 14. Required lab preflight

Collect:

- Windows version
- computer name and domain membership
- RSAT status
- ADUC availability (`dsa.msc`)
- Active Directory PowerShell module availability and version
- `dotnet --version`
- `Get-ADDomain`
- `Get-ADForest`
- `Get-ADDomainController -Discover`
- RootDSE: `defaultNamingContext`, `configurationNamingContext`, `rootDomainNamingContext`

Locate an explicit synthetic ADEXO test user. Do not select an employee because the account looks unused, old, or administrative. If no synthetic user exists, stop. Do not create one unless the user specifically authorizes creation.

Capture a before snapshot of that user before any later rename or move. Include identity attributes, `userAccountControl`, mail attributes, `proxyAddresses`, `targetAddress`, extension attributes, group membership, and `accountExpires`.

## 15. Display-specifier preflight

Before any write:

1. Determine the ADUC locale. Do not assume `409` if the server is using another locale. `409` was the locale read in the earlier check.
2. Resolve the `user-Display` distinguished name.
3. Read the existing `adminContextMenu` values.
4. Record that before state.
5. Build a backup JSON document from the live values and run `Backup-ADEXODisplaySpecifiers.ps1`. The script stores the JSON it is given. It does not query AD.
6. Run `Test-ADEXOForestRegistration.ps1` against those live values and the proposed exact value.
7. Run `Register-ADEXOForest.ps1 -WhatIf -Verbose` with the live `-ExistingValues`, the real executable path, the explicit locale, and the live configuration naming context.

Then stop.

The first report back to the operator is titled `PHASE 2 MUTATION GATE` and includes every field below. Use `NOT RUN` or `UNKNOWN` rather than guessing.

```text
PHASE 2 MUTATION GATE

WINDOWS:
DOMAIN:
FOREST:
FOREST TYPE: A, B, or C
DOMAIN CONTROLLER:
RSAT:
ADUC:
ACTIVE DIRECTORY MODULE:
.NET:
TEST USER:
OBJECTGUID:
TEST USER DN:
TEST USER SNAPSHOT CAPTURED:
CONFIGURATION NAMING CONTEXT:
DISPLAY SPECIFIER DN:
LOCALE:
CURRENT adminContextMenu VALUES:
BACKUP:
BACKUP PATH:
REGISTRATION TEST:
REGISTRATION WHATIF:
PROPOSED ADEXO ENTRY:
SELECTED ORDER:
EXISTING ENTRIES PRESERVED:
PROPOSED DIRECTORY CHANGES:
SCHEMA CHANGES:
ACL CHANGES:
USER OBJECT CHANGES:
WHETHER THE SCRIPT CAN ACTUALLY WRITE:
SAFETY CONCERNS:
SAFE TO PROCEED WITH REAL REGISTRATION:
```

Do not perform real registration until a person reviews this gate. At the current baseline, `WHETHER THE SCRIPT CAN ACTUALLY WRITE` is no.

## 16. Eventual lab acceptance path

This path starts only after a person approves a real registration and the write mechanism is an approved one. Do not start it during the first preflight.

1. Register `Manage with ADEXO` on the test `user-Display` display specifier by appending one value.
2. Confirm every pre-existing `adminContextMenu` value is unchanged.
3. Fully close and reopen `mmc.exe` and ADUC. Display-specifier data is cached until then.
4. Right-click only the synthetic test user.
5. Confirm `Manage with ADEXO` appears.
6. Launch it from the real ADUC menu. The documented product path is `C:\Program Files\ADEXO\ADEXO.exe`. The file currently built in this lab is `C:\src\ADEXO-lab\ADEXO.Desktop.exe`. Do not pretend a menu pointed at one path tested the other path.
7. Do not construct the process arguments by hand for the acceptance launch. Capture the actual sanitized argv that ADUC produced.
8. The expected shape, still unverified, is argument 0 = selected LDAP ADsPath and argument 1 = `user`. If the captured array differs, stop. Do not weaken the parser to fit the capture.
9. Record objectGUID A from the first read and objectGUID B from the binary-GUID re-query. Require A equals B. Do not record a client username, SID, or token in the repository.
10. Only in a forest classified A, rename or move the synthetic user. Refresh ADEXO. Require the GUID to be unchanged, the new DN to be recognized, and the canonical object to be the same.
11. Run unregister as `-WhatIf`, then unregister only the exact ADEXO value.
12. Restart ADUC and confirm the ADEXO menu is gone.
13. Confirm every unrelated `adminContextMenu` entry is unchanged.
14. Confirm the synthetic user differs from the before snapshot only by the deliberate rename or move.

Quoting for a path that contains spaces is still unverified. A lab path with no spaces, such as `C:\src\ADEXO-lab\ADEXO.Desktop.exe`, does not prove the `"C:\Program Files\ADEXO\ADEXO.exe"` quoting assumption.

## 17. Forest mutation safety

The only planned forest mutation is appending one static ADEXO `adminContextMenu` value to the approved `user-Display` object.

Forbidden in this lab:

- schema modification
- ACL modification
- GPO modification
- DNS modification
- domain-controller configuration changes
- Exchange modification
- Entra modification
- any Microsoft Graph call
- any tenant operation
- restoring a whole attribute from backup

Stop if the registration code proposes replacing the entire `adminContextMenu` attribute.

## 18. Test-user safety

Capture the before snapshot first.

ADEXO and the lab procedure must not change the synthetic user's enabled state, password state, UPN, `sAMAccountName`, `mail`, `proxyAddresses`, `targetAddress`, extension attributes, group membership, account expiration, or Exchange attributes.

The only deliberately permitted user change, and only in a forest classified A during the final acceptance path, is a rename or a move used to prove GUID identity.

## 19. Phase 3 boundary

Phase 3 has not started. After Phase 2 lab validation passes, Phase 3 is expected to be read-only and to cover:

- Microsoft Graph / Entra authentication
- tenant verification
- AD-to-cloud identity correlation
- license display
- mailbox discovery
- mailbox type
- primary SMTP
- aliases
- archive state
- forwarding display
- delegation display
- mailbox usage
- capability detection

The initial Phase 3 design has no cloud writes. Do not implement any of it during this lab task. The design note is `docs/architecture/0008-phase-3-readonly-cloud.md`. Reading it is optional context. Implementing it is out of scope.

## 20. Documents and code to read

Read these before the lab work:

- `README.md`
- `docs/architecture/0001-aduc-integration.md`
- `docs/architecture/0002-runtime-and-stack.md`
- `docs/architecture/0003-authentication-and-permissions.md`
- `docs/architecture/0004-change-operations.md`
- `docs/architecture/0005-provider-capabilities.md`
- `docs/architecture/0007-aduc-launch-contract.md`
- `docs/architecture/risk-register.md`
- `docs/security/threat-model.md`
- `docs/testing/phase-2-aduc-validation.md`
- `scripts/ADEXO.Registration.psm1`
- `scripts/Register-ADEXOForest.ps1`
- `scripts/Unregister-ADEXOForest.ps1`
- `scripts/Test-ADEXOForestRegistration.ps1`
- `scripts/Backup-ADEXODisplaySpecifiers.ps1`

This handoff does not supersede the repository when the code or those documents are more precise.

## 21. Operating principle

Do not rush to get a pass.

The lab exists to find out whether the Phase 2 assumptions hold on real Windows, real ADUC, and real Active Directory. A failure is a useful result.

Do not work around a failure by weakening input validation, LDAP protection, identity verification, registration safety, authorization, source-of-authority boundaries, redaction, or read-only enforcement.

If the lab reveals a defect, stop, document the exact observed behavior, and wait. Do not patch Phase 2 unless the user explicitly reopens it.

## 22. First action

After reading this handoff and inspecting the repository:

- Do not start coding.
- Continue directly with the Phase 2 read-only preflight and the mutation gate.
- Stop immediately before the first real Active Directory configuration write.
- Return the `PHASE 2 MUTATION GATE` report for review.

Do not begin Phase 3.
