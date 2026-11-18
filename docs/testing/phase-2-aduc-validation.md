# Phase 2 ADUC validation

Status: not started. Phase 2 is done only when every item below has been observed on an approved disposable domain. Do not run this against a client forest.

No argument array, forest name, domain controller, registration result, or screenshot from a live directory is recorded in this repository.

WINDOWS DOMAIN LAB VALIDATION PENDING

## Proof required

- [ ] Register `Manage with ADEXO` on the test `user-Display` display specifier.
- [ ] Confirm the existing `adminContextMenu` values remain untouched.
- [ ] Fully close and reopen `mmc.exe` and Active Directory Users and Computers.
- [ ] Right-click a synthetic test user.
- [ ] Confirm `Manage with ADEXO` appears.
- [ ] Launch it from an actual path such as `C:\Program Files\ADEXO\ADEXO.exe`.
- [ ] Capture the actual sanitized argv shape and confirm the quoting assumption.
- [ ] Confirm ADEXO receives the expected ADsPath and `user` class.
- [ ] Resolve `objectGUID` A.
- [ ] Re-query by binary `objectGUID`.
- [ ] Verify the returned `objectGUID` B and require A equals B.
- [ ] Move or rename the test user and verify a GUID-based refresh still resolves it.
- [ ] Test the safe no-argument launch state.
- [ ] Run unregister.
- [ ] Restart ADUC and confirm the menu disappears.
- [ ] Verify every unrelated `adminContextMenu` entry remains unchanged.
- [ ] Verify the test user itself was never modified.

## Rules for the record

Use a synthetic user in a disposable or explicitly approved lab forest. Sanitized notes may record the argv shape, the locale, and whether A equaled B. They must not include a client forest name, a real username, a SID, or a token.

Quoting, menu appearance, display-specifier caching, and live GUID rebind stay unproven until the boxes above are checked.
