# Repository structure

Status: Phase 1 foundation is in the tree.

The product name is ADEXO. The working name from the brief is Hybrid User Manager for ADUC. Phase 0 kept a `HybridUserManager` prefix so the solution would match the brief. Phase 1 uses the `ADEXO` prefix instead, because the executable and the repository are ADEXO. The executable name remains `ADEXO.exe`.

## Source

```text
src/
  ADEXO.Contracts/         Identities, capabilities, operations, secrets, configuration.
  ADEXO.Core/              State machine, cancellation, execution guard, compensation.
  ADEXO.ActiveDirectory/   Provider project. No directory calls in Phase 1.
  ADEXO.Graph/             Provider project. No Graph calls in Phase 1.
  ADEXO.ExchangeOnline/    Provider project. No Exchange calls in Phase 1.
  ADEXO.EntraConnect/      Provider project. No sync calls in Phase 1.
  ADEXO.Security/          Boundary for later token protection. No MSAL in Phase 1.
  ADEXO.Diagnostics/       Boundary for a later support bundle.
  ADEXO.Infrastructure/    System clock and composition references.
  ADEXO.Desktop/           WPF shell, net10.0-windows. No provider calls.
```

`ADEXO.AducExtension`, `ADEXO.PowerShell`, and `ADEXO.Installer` are not in this phase.

## Tests

```text
tests/
  ADEXO.Contracts.Tests/
  ADEXO.Core.Tests/
```

Provider, integration, and UI tests wait until a Windows lab and a non-production tenant exist. Tests do not call a production tenant or a production domain.

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
