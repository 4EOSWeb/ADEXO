# Phase 2 ADUC validation

Status: complete. Every item below was observed on an isolated, disposable Windows domain lab. This record is not a production deployment validation and must not be read as one.

WINDOWS DOMAIN LAB VALIDATION PASSED

## Lab platform

- Microsoft Windows Server 2025 Standard Evaluation, build 26100, acting as its own domain controller.
- A single-domain, single-forest lab created for this validation. Disposable by design.
- ADUC and the Active Directory PowerShell module were present on the same host as the built executable.
- The application was published to `C:\Program Files\ADEXO\ADEXO.exe` so the real product command path, including its space, was exercised.

Forest, domain, controller, and administrator names are deliberately omitted. They identify a live directory and add nothing to the engineering record.

## Observed results

- Static `adminContextMenu` registration on the locale-specific `user-Display` display specifier. Locale was chosen explicitly and was corroborated from the host locale settings rather than assumed.
- The menu appeared in real ADUC for the synthetic user, between the standard `Move...` and `Open Home Page` entries.
- The entry was invoked through the real static context menu. The application was never started with hand-built arguments.
- The command was `"C:\Program Files\ADEXO\ADEXO.exe"`, quoted because the path contains a space.
- ADUC supplied exactly two application arguments: a controller-qualified `LDAP://` ADsPath for the selected object, and the object class `user`.
- The space-bearing Program Files path survived `ShellExecute` intact and reached the application as a single first argument.
- Initial resolution of the selected object succeeded and produced `objectGUID` A.
- The binary `objectGUID` rebind succeeded and produced `objectGUID` B, equal to A.
- The interface remained read-only: no editable controls, no save operation, no password, enable/disable, group, or proxy-address action. No cloud provider was invoked.
- The synthetic user was moved to a different organizational unit. `objectGUID` was unchanged by the move and the distinguished name changed.
- `Refresh` was available after the verified load, executed through the real WPF command, and resolved the object by canonical `objectGUID`. It displayed the user's **new** distinguished name after the move. No mutable-name fallback was used or needed.
- `Unregister-ADEXOForest.ps1 -WhatIf` identified the exact recorded value, targeted only it, and caused no directory mutation.
- Exact-value unregistration removed only the ADEXO value. After a full ADUC restart the menu was gone.
- The display specifier returned to its original state, one unrelated value, and that value was never modified at any point.
- No schema, ACL, group policy, DNS, or unrelated directory change occurred.
- The synthetic user differed from its captured baseline only by the authorized move.

## Sanitized argument shape

The captured first argument is written here with the identifiers removed:

```text
argv[0]  "C:\Program Files\ADEXO\ADEXO.exe"
argv[1]  LDAP://<controller>/CN=<user>,OU=<organizational unit>,DC=<domain>,DC=<tld>
argv[2]  user
```

Two application arguments. The first is a controller-qualified LDAP URL with no trailing attributes, scope, or filter. The second is the object class.

## Defects found by this validation

The lab found two real implementation defects that the automated suites had not caught. Both were fixed and the fix was proven on a second live run. This history is kept deliberately.

### Defect 1: registration tooling did not write to the directory

`Register-ADEXOForest.ps1` and `Unregister-ADEXOForest.ps1` built and printed a plan, but their `ShouldProcess` blocks only wrote a verbose line. No directory mutation existed, so the menu could never actually be registered, verified, or removed.

Why automation missed it: the Pester suite exercised the pure planning helpers only. Nothing asserted that a mutation boundary was reached, so a script that never mutated anything passed every test.

Correction: a live read of the attribute, a targeted multivalue add and remove, a pre-mutation re-read that aborts on a concurrent administrator change, and a post-write re-read that verifies the exact entry plus every pre-existing value.

Regression coverage: `scripts/tests/Registration.Tests.ps1` drives the mutation boundary with mocks and asserts the append, the exact-value removal, duplicate and collision refusal, concurrent-change abort, verification failure, authorization failure, and that `-WhatIf` performs no write.

A second, related defect surfaced during the fix: a script-level `-WhatIf` did not reach the module orchestrator because preference variables are not shared across that boundary. The scripts now forward `WhatIf` and `Confirm` explicitly.

### Defect 2: the Refresh command never invalidated

After a verified load the `Refresh` button stayed permanently disabled, so a moved object could never be re-read and new-DN recognition was unreachable.

Why automation missed it: the existing test called `RefreshAsync()` directly. That bypasses `CanExecute` entirely, so no test ever asked whether the command was enabled. `RelayCommand` raises `CanExecuteChanged` only from `RaiseCanExecuteChanged()`, which nothing called, and it does not hook `CommandManager.RequerySuggested`, so WPF evaluated the predicate once at bind time and never again.

Correction: state changes that a command predicate depends on now invalidate that command. The predicates themselves were not weakened. `CommandManager.RequerySuggested` was deliberately not used, because it would make the enabled state depend on input timing and would not be deterministic under test.

Regression coverage: `tests/ADEXO.Desktop.Tests/RefreshCommandTests.cs` drives the real `ICommand` surface — `CanExecute`, `CanExecuteChanged`, and `Execute` — rather than the method behind it. It covers the disabled state before verification, the transition after a verified load, busy-state transitions, cancellation before verification, failed verification, concurrent invocations, exception sanitization, and a moved-object refresh that must keep the GUID and load the new distinguished name. A sibling test covers `CancelCommand` and a moved-object assertion, which had the same latent defect.

## What remains unproven

- Multi-selection. Static administrative menu items do not support it, and Phase 2 does not either.
- Any object class other than `user`.
- Any cloud identity, mailbox, or licensing behavior. Phase 2 makes no such call.
- Production deployment. This was an isolated disposable lab and its objects are disposable.

## Lab objects

Three organizational units and one synthetic user were created for this validation and are intentionally retained for later identity-correlation work. They exist only in the disposable lab and must never be mistaken for production validation objects.
