# Repository structure

Status: layout for later phases. Phase 0 creates the `docs/` tree only. It does not add projects, packages, or scripts.

The product name is ADEXO. The working name from the brief is Hybrid User Manager for ADUC. Project names keep the `HybridUserManager` prefix so the solution matches the brief. The executable name is `ADEXO.exe`.

## Source

```text
src/
  HybridUserManager.AducExtension/     Static-launch notes and, later, any native stub. v1 has no in-proc DLL.
  HybridUserManager.Desktop/           WPF shell. ADEXO.exe.
  HybridUserManager.Core/              Change operations, capabilities, matching.
  HybridUserManager.ActiveDirectory/   Active Directory provider.
  HybridUserManager.Graph/             Microsoft Graph provider.
  HybridUserManager.ExchangeOnline/    Exchange Online provider.
  HybridUserManager.PowerShell/        Typed PowerShell session used by Exchange and Entra Connect.
  HybridUserManager.Security/          MSAL, token cache, pipe ACL.
  HybridUserManager.Infrastructure/    Logging, configuration, audit store.
  HybridUserManager.Contracts/         IChangeOperation, capability types, IPC messages.
  HybridUserManager.Installer/         WiX v4. Workstation files only.
  HybridUserManager.Diagnostics/       Sanitized support bundle.
```

## Tests

```text
tests/
  HybridUserManager.Core.Tests/
  HybridUserManager.ActiveDirectory.Tests/
  HybridUserManager.Graph.Tests/
  HybridUserManager.ExchangeOnline.Tests/
  HybridUserManager.IntegrationTests/
  HybridUserManager.UITests/
```

Phase 1 adds the contracts and core test projects. Integration and UI tests wait until a Windows lab exists. Tests do not call a production tenant or a production domain.

## Docs

```text
docs/
  architecture/     ADRs 0001 through 0005 and this layout.
  installation/     Filled when the MSI and forest scripts exist.
  permissions/      Points at architecture/0003 until a longer operator matrix is needed.
  troubleshooting/  Filled with lab results.
  security/         threat-model.md
  operator-guide/   Filled with the UI.
```

## Scripts

These scripts are specified now and added in the phase that can run them on Windows. Phase 0 does not add the files.

```text
scripts/
  Install-DevelopmentBuild.ps1
  Uninstall-DevelopmentBuild.ps1
  Register-AducExtension.ps1          Local development helper. Does not write the forest.
  Unregister-AducExtension.ps1
  Register-ADEXOForest.ps1            Explicit forest registration.
  Unregister-ADEXOForest.ps1
  Test-ADEXOForestRegistration.ps1
  Backup-ADEXODisplaySpecifiers.ps1
  Test-Prerequisites.ps1
  Test-TenantConnectivity.ps1
  Test-RequiredPermissions.ps1
  Build-Installer.ps1
```

`ADEXO.msi` and the four forest scripts are the deployment pair. The MSI does not call the forest scripts.

## Phase order

Phase 1 is the first code phase. It adds the solution, nullable reference types, analyzers, the contracts for change operations and capabilities, logging, configuration, and unit tests. It rechecks the supported .NET LTS before setting `TargetFramework`. It does not register ADUC and does not call Graph or Exchange.
