# Phase 3 lab limitations

Status: recorded. This note exists so the Phase 3 work is not over-claimed.

## What was proven end to end in the live lab

These were observed against the approved test tenant, not simulated:

- The minimal WPF harness authenticated interactively through WAM, showed the account picker, and
  returned a token for tenant `c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af`.
- Production ADEXO authenticated through its cached MSAL state.
- T1 passed: the token tenant identifier equalled the approved tenant.
- T2 passed: `GET /organization` returned the approved tenant identifier.
- Sign-out cleared the session and left Active Directory loaded with the same `objectGUID`.
- Token redaction held: copied diagnostics contained no token material.
- Active Directory remained loaded through every cloud failure, including a 40-second hang.
- No Graph write and no Exchange operation occurred at any point.

## What remains unproven

**Fresh interactive WAM authentication inside the full ADEXO executable.** The production run that
succeeded used the silent path because a cached account existed. The interactive path was proven in the
isolated harness, not in the product.

Why it was not closed out:

The only Windows control available in this lab is the built-in `TEST\Administrator` account, SID ending
`-500`, on the lab's only domain controller. Two properties of that host prevent a normal-user control:

- `FilterAdministratorToken` is unset, so the built-in Administrator runs with a full, unfiltered token
  and is always at High integrity. No medium token exists for that account.
- The domain controller's effective policy does not grant interactive or remote-interactive logon to
  `Domain Users` or `Users`, so an ordinary domain user cannot sign in here at all.

Both were verified, not assumed: a probe launched through `explorer.exe` still reported
`Mandatory Label\High Mandatory Level`, and `secedit /export` showed the logon-right assignments.

Closing the gap would have required editing Default Domain Controllers Policy or changing UAC policy
purely to satisfy a test. Neither was judged justified for a lab observation, so the work stopped.

Related observations, recorded for honesty:

- `dsregcmd` reports `WamDefaultSet: ERROR (0xd0020017)` on this host. Microsoft documents that this
  field can display an error when `dsregcmd` runs from an elevated prompt, and every reading taken here
  came from a High-integrity session. It is therefore **not** treated as evidence of a WAM fault.
- A workplace registration exists for `c18ee4be-...` in the current user's context, with a valid
  TPM-protected `MS-Organization-Access` certificate. It is internally consistent and was treated as
  legitimate. It is not described as a defect.
- `Automatic-Device-Join` attempts hybrid join on every logon and fails with `0x801C001D` because this
  forest has no Device Registration service connection point. That is expected for a lab with no
  Entra Connect, and the task was not modified.

## What this means for deployment

Interactive WAM sign-in from a normal, non-elevated domain user session on an ordinary workstation
remains a **final deployment-validation item**. It is not a known product defect, and it is not proven
broken either.

ADEXO should be documented as running non-elevated. The Phase 3 code does not require elevation, and
the harness succeeded in a normal interactive session when one was available.

## What this limitation does not affect

Nothing in the correlation, licence, mailbox, delegation, or UI work depends on it. Those are covered by
deterministic tests with no live tenant, and the identity architecture they rely on is proven by the T1
and T2 results above.
