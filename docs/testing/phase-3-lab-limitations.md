# Phase 3 lab limitations

Status: recorded. This note exists so the Phase 3 work is not over-claimed.

## What was proven end to end in the live lab

These were observed against the approved test tenant, not simulated:

- The minimal WPF harness authenticated interactively through WAM, showed the account picker, and
  returned a token for tenant `c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af`.
- **Fresh interactive WAM authentication completed inside the full product executable.** On one attempt
  the broker surfaced its interactive UI after roughly 15.6 seconds and ADEXO authenticated through it
  from a cold start, with no cached account satisfying the silent path.
- Production ADEXO also authenticated through its cached MSAL state on earlier runs.
- WAM T1 passed: the token tenant identifier equalled the approved tenant.
- WAM T2 passed: `GET /organization` returned the approved tenant identifier.
- WAM immutable correlation executed and returned `Unmatched` for the lab object, which is the correct
  outcome: `adexo-phase2-test` has no `mS-DS-ConsistencyGuid` and no synchronized cloud counterpart.
- The bounded fallback behaved correctly in both directions. Four consecutive `Auto` runs surfaced no
  broker UI and fell back to the browser at 20.4–20.6 seconds, displaying the approved message and
  recording `Actual = System browser` with the fallback reason. One run surfaced broker UI at about
  15.6 seconds, and the fallback was correctly suppressed.
- Sign-out after the brokered session cleared the session and left Active Directory loaded with the same
  `objectGUID`.
- Token redaction held: copied diagnostics contained no token material.
- Active Directory remained loaded through every cloud failure, including a 40-second hang.
- No Graph write and no Exchange operation occurred at any point.

## What remains unproven

**A complete live system-browser authentication session requiring operator credential interaction.**

The browser path was validated as far as it can be without a credential entry. The following were
observed live for `SystemBrowserOnly`:

- the system browser launched;
- MSAL created the loopback listener, and the authorize request carried `code_challenge` with `S256`;
- the redirect reached an ADEXO "Authentication complete" loopback page, which evidences the redirect
  plumbing only. It is not evidence that ADEXO received a usable token, and it was not accompanied by
  T1, T2, or any correlation in the product;
- diagnostics recorded `Preferred = System browser only`, and before any completion `Actual = None yet`.

What was **not** observed: the operator did not complete the credential or passkey prompt during the
browser test, so no browser token was issued to the product. The Microsoft 365 section remained
`Disconnected` on every browser-path run, which is consistent with that.

| Item | Status |
| --- | --- |
| Browser launch and fallback mechanics | PASS (live) |
| Loopback redirect plumbing | PASS (live, where observed) |
| Fallback selection at ~20 seconds | PASS (live) |
| Approved UI wording displayed | PASS (live) |
| Diagnostics record method and fallback reason | PASS (live) |
| Interactive credential completion | NOT COMPLETED BY OPERATOR |
| Browser token acquisition | NOT PROVEN LIVE |
| Browser T1 | Automated test coverage only |
| Browser T2 | Automated test coverage only |
| Browser Graph business reads (`subscribedSkus`, correlation) | NOT PROVEN LIVE |

The browser path's T1 and T2 are the same code as the WAM path's, which did pass live, and they are
covered by deterministic tests that drive the real authentication service and the real session
pipeline. That is strong evidence, but it is **not** the same as a live observation, and this note does
not present it as one.

## Status

**System-browser authentication: PARTIALLY VALIDATED.**

> System-browser fallback launch, loopback, fallback selection, and automated T1/T2 pipeline are
> validated. A complete live browser-authentication session requiring operator credential interaction
> remains a final pre-release validation item.

This is a known validation gap, not a blocker to continuing product development. Phase 4 may be
designed. Before shipping a production v1, one human-completed system-browser sign-in must prove, in
order:

1. interactive authentication completes;
2. a token is returned;
3. T1 passes;
4. T2 passes;
5. immutable correlation executes;
6. `subscribedSkus` executes;
7. sign-out succeeds;
8. no token leakage;
9. no cloud mutation occurred.

## Why the credential prompt is the operator's to complete

ADEXO holds no password and never asks for one. The browser prompt is Microsoft's, and the value never
passes through this application. That is deliberate, and it is why the gap has to be closed by a person
at the prompt rather than by any change here. No production code was changed because the prompt was not
finished, and the incomplete interaction is not treated as a defect.

## Earlier host limitations, retained for context

The first attempts to obtain fresh interactive WAM inside the product did not succeed, and the
investigation that followed is recorded here rather than discarded, because it explains the host's
behaviour:

The only Windows control available in this lab is the built-in `TEST\Administrator` account, SID ending
`-500`, on the lab's only domain controller. Two properties of that host affect what a normal-user
control looks like:

- `FilterAdministratorToken` is unset, so the built-in Administrator runs with a full, unfiltered token
  and is always at High integrity. No medium token exists for that account.
- The domain controller's effective policy does not grant interactive or remote-interactive logon to
  `Domain Users` or `Users`, so an ordinary domain user cannot sign in here at all.

Both were verified, not assumed: a probe launched through `explorer.exe` still reported
`Mandatory Label\High Mandatory Level`, and `secedit /export` showed the logon-right assignments.

Closing that limitation would have required editing Default Domain Controllers Policy or changing UAC
policy purely to satisfy a test, which was not judged justified. The broker later surfaced UI on its own
without any such change, which is consistent with the recorded finding that the behaviour is
intermittent rather than policy-driven.

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

ADEXO should be documented as running non-elevated. The Phase 3 code does not require elevation, and
the broker succeeded in a normal interactive session when it surfaced.

The two remaining deployment-validation items are the partial browser result above and a fresh
interactive sign-in from an ordinary, non-elevated domain user session on a standard workstation. Neither
is a known product defect, and neither is proven broken.

## What this limitation does not affect

Nothing in the correlation, licence, mailbox, delegation, or UI work depends on it. Those are covered by
deterministic tests with no live tenant, and the identity architecture they rely on is proven by the WAM
T1 and T2 results above.
