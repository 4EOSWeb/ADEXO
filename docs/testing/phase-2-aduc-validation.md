# Phase 2 ADUC validation

Status: not started. This checklist is for an approved disposable domain. Do not run it against a client forest.

No argument array, forest name, domain controller, or registration result from a live directory is recorded in this repository.

## Prerequisites

- [ ] The forest is a disposable lab, or an explicitly approved non-production forest.
- [ ] The workstation is x64 Windows 11 or a supported management server.
- [ ] RSAT Active Directory Domain Services tools and Active Directory Users and Computers are installed.
- [ ] A non-production test user exists.
- [ ] The operator can read the configuration partition and can add and remove one display-specifier value.
- [ ] The .NET 10 desktop runtime is installed.
- [ ] A backup directory outside the repository is chosen.

## Registration

- [ ] Run `Backup-ADEXODisplaySpecifiers.ps1` with a reviewed JSON document.
- [ ] Run `Test-ADEXOForestRegistration.ps1`.
- [ ] Run `Register-ADEXOForest.ps1 -WhatIf` with an explicit locale. English labs pass `-Locale 409`.
- [ ] Inspect the planned order, caption `Manage with ADEXO`, and executable path.
- [ ] Run the register script without `-WhatIf`.
- [ ] Close every `mmc.exe` process and open Active Directory Users and Computers again.

## Launch

- [ ] Right-click the approved test user.
- [ ] Confirm `Manage with ADEXO` is present.
- [ ] Launch ADEXO.
- [ ] Confirm the window shows that user and no edit controls.
- [ ] Confirm the objectGUID.
- [ ] Confirm the distinguished name and object class.
- [ ] Confirm account fields.
- [ ] Confirm diagnostics say GUID verification passed.
- [ ] Record the sanitized argument array. The expected shape is the ADsPath and then the class. Quoting for `C:\Program Files\ADEXO\ADEXO.exe` is not confirmed until this step.

## Negative checks

- [ ] Start ADEXO with no arguments and confirm it does not search.
- [ ] Confirm a non-user object is unsupported.
- [ ] Confirm a missing executable is refused by the script.
- [ ] Confirm an unavailable domain controller is reported without a raw LDAP dump.
- [ ] If safe, rename, move, and delete a disposable test user and confirm GUID behavior.

## Unregistration

- [ ] Run `Unregister-ADEXOForest.ps1 -WhatIf` with the exact recorded value.
- [ ] Remove that exact value only.
- [ ] Restart Active Directory Users and Computers.
- [ ] Confirm the ADEXO item is gone and unrelated menu entries remain.
- [ ] Keep the backup for human review. Ordinary unregistration does not restore the whole attribute.

WINDOWS DOMAIN LAB VALIDATION PENDING
