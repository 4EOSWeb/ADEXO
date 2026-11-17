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

Those scripts are specified and are not in the tree yet.

Phase 1 foundation implemented. Portable contracts, the operation state machine, and unit tests are in the solution. The desktop project is a WPF shell and does not call a directory or a tenant. Windows CI is pending until the windows-build job is green.

## Architecture

- [ADUC integration](docs/architecture/0001-aduc-integration.md)
- [Runtime and stack](docs/architecture/0002-runtime-and-stack.md)
- [Authentication and permissions](docs/architecture/0003-authentication-and-permissions.md)
- [Change operations](docs/architecture/0004-change-operations.md)
- [Provider capabilities](docs/architecture/0005-provider-capabilities.md)
- [Repository structure](docs/architecture/repository-structure.md)
- [Risk register](docs/architecture/risk-register.md)
- [Threat model](docs/security/threat-model.md)

Portable libraries target `net10.0`. The WPF shell targets `net10.0-windows`. .NET 10 is the active LTS, supported through November 14, 2028. Every write goes through a preview, a capability check, and a re-read. The UI does not decide whether a hybrid field is editable.
