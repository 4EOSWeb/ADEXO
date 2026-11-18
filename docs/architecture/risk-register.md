# Risk register

Status: open as of Phase 0. Likelihood and impact are design judgments. They are not measurements from a lab.

| ID | Risk | Likelihood | Impact | Response |
| --- | --- | --- | --- | --- |
| R1 | ADUC behavior is documented from Microsoft Learn and has not been clicked in a domain. The Linux host cannot host ADUC. | ~~High~~ Closed | High | Closed by the Phase 2 Windows domain lab: the menu, the ADsPath argument, the two-argument contract, and unregistration were all observed. See [Phase 2 ADUC validation](../testing/phase-2-aduc-validation.md). |
| R2 | The forest menu appears on workstations that do not have `ADEXO.exe`. | High | Low | Registration is a separate script, states this, and is preceded by a backup. `ShellExecute` failure leaves ADUC up. |
| R3 | The static command passes an ADsPath, which can be stale by the time the companion reads it. | Medium | Medium | The companion resolves `objectGUID` immediately and uses the GUID afterward. |
| R4 | A project file targets a runtime that later falls out of support. | Medium | Medium | Phase 1 rechecks the .NET support policy and records the LTS `TargetFramework`. The expected value is `net10.0-windows`. |
| R5 | Display-specifier edits are cached until ADUC reloads, so a registration looks missing. | High | Low | The operator guide says to close and reopen ADUC. The test script reads the directory, not the cache. Observed in the lab: the menu persisted in an open console until it was restarted. |
| R6 | Static items do not support multi-select in administrative snap-ins. | Certain | Low | v1 is single selection. Multi-select waits for a native stub that only signals `ADEXO.exe`. |
| R7 | Two administrators change one object, or a later cloud step fails after an on-premises write. | Medium | High | `IChangeOperation` previews, re-reads, and refuses a blind restore of `proxyAddresses`. Partial completion is labeled as partial. |
| R8 | Tokens or mailbox content land in a log or support bundle. | Medium | High | Logging rules in the threat model. Phase 9 scans for secrets. |
| R9 | A pipe client running as another user submits a request. | Low | High | Pipe ACL plus peer-token check. No SYSTEM service. |
| R10 | Exchange Online PowerShell 7 support requires 7.6 or later from module 3.10.0. | Low | Medium | 7.7.0 is present on the current host. The Windows lab checks both 5.1 and 7. |
| R11 | `Microsoft.Graph` 6.10.0 targets `net8.0` and `netstandard2.0`. A future major may drop the LTS chosen in Phase 1. | Low | Medium | Pin the verified version. Recheck before the reference is added. |
| R12 | Forest registration is copied between client environments without a backup. | Medium | High | Scripts refuse to append unless a backup file for that forest exists, or the administrator passes an explicit acknowledgment. |
| R13 | App-only mode is used as a convenience for interactive admins. | Medium | High | No automatic fallback. App-only stays a separate approved configuration. |
| R14 | WiX, COM registration, and WPF cannot be proven on this Linux host. | Certain | Medium | Those results wait for the Windows lab. Phase 0 claims no installer or UI success. The WPF shell has since been exercised on Windows; the installer remains unproven. |
| R15 | The executable path quoting has not been captured from ADUC. | ~~High~~ Closed | Medium | Closed by the Phase 2 Windows domain lab: `"C:\Program Files\ADEXO\ADEXO.exe"` arrived as a single first argument through the real static context-menu invocation. See [ADR 0007](0007-aduc-launch-contract.md). |
| R16 | An ADsPath names an untrusted LDAP host. | Medium | High | The connection uses the discovered domain controller, or the named host only when it matches discovery. |
| R17 | Unregistration matches every copy of ADEXO.exe. | Medium | Medium | Removal requires the exact recorded value and stops on ambiguity. |

Accepted for v1: R6. R1 and R15 are closed by the Phase 2 Windows domain lab. The others stay open until the response is implemented and, where the response is a lab check, until that check has a recorded result.
