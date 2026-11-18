# ADEXO

ADEXO is a Windows administration companion for Active Directory Users and Computers. An administrator selects a user, group, contact, computer, or organizational unit and manages the on-premises object and the matching Microsoft 365 or Exchange Online object from one window.

The working name is Hybrid User Manager for ADUC. v1 does not load code inside `mmc.exe`.

```text
ADUC
  |
  | Administer Microsoft 365
  v
Static adminContextMenu
  |
  | ADsPath + object class
  v
ADEXO.exe
  |
  +-- Active Directory provider
  +-- Microsoft Graph provider
  +-- Exchange Online provider
  +-- Entra Connect provider
```

Installing the program and publishing the ADUC menu are separate steps.

- `ADEXO.msi` copies `C:\Program Files\ADEXO\ADEXO.exe` onto a management workstation. It does not change the forest.
- `Register-ADEXOForest.ps1` appends the menu to the display specifiers, after `Backup-ADEXODisplaySpecifiers.ps1`. `Test-ADEXOForestRegistration.ps1` and `Unregister-ADEXOForest.ps1` check and remove that value.

The lab registration scripts are in `scripts/`. They are not run by the application.

Phase 2 complete - Windows ADUC and Active Directory lab validation passed.

The read-only path validates an ADUC launch, resolves a user, and shows the user only when a second lookup by objectGUID returns the same GUID. It does not write to the directory and it does not call Microsoft 365. The menu registration scripts perform a verified display-specifier change only when an administrator runs them explicitly; they are not run by the application. ADUC behavior was observed in an isolated disposable Windows domain lab, not a production deployment.

See [Phase 2 ADUC validation](docs/testing/phase-2-aduc-validation.md) for the sanitized lab record.

## Architecture

- [ADUC integration](docs/architecture/0001-aduc-integration.md)
- [Runtime and stack](docs/architecture/0002-runtime-and-stack.md)
- [Authentication and permissions](docs/architecture/0003-authentication-and-permissions.md)
- [Change operations](docs/architecture/0004-change-operations.md)
- [Provider capabilities](docs/architecture/0005-provider-capabilities.md)
- [Repository structure](docs/architecture/repository-structure.md)
- [ADUC launch contract](docs/architecture/0007-aduc-launch-contract.md)
- [Phase 2 lab checklist](docs/testing/phase-2-aduc-validation.md)
- [Proposed Phase 3 read-only cloud correlation](docs/architecture/0008-phase-3-readonly-cloud.md)
- [Risk register](docs/architecture/risk-register.md)
- [Threat model](docs/security/threat-model.md)

Portable libraries target `net10.0`. The WPF shell targets `net10.0-windows`. .NET 10 is the active LTS, supported through November 14, 2028. Every write goes through a preview, a capability check, and a re-read. The UI does not decide whether a hybrid field is editable.
