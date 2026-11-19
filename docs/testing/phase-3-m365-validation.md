# Phase 3 Microsoft 365 lab validation

Status: Phase 3 implemented. App registration in place (broker redirect plus `http://localhost`).
Live validation partially complete: the broker path passed end to end; the system-browser path is
partially validated. See [Phase 3 lab limitations](phase-3-lab-limitations.md) for the precise record.

PHASE 3A APP REGISTRATION GATE

This document describes how Phase 3 is validated against an approved Microsoft 365 lab tenant, what was
observed, and how each observation is judged. Rows are only PASS when the stated evidence was captured.

## Scope split

Phase 3A covers identity, the tenant lock, Graph correlation, and licence display. Phase 3B covers Exchange Online mailbox metadata, which is not designed in detail yet and is not authorized by this gate. Exchange read cmdlets were not permission-verified during discovery, so they stay out of 3A rather than being guessed at.

## Approved lab tenant

| Item | Value |
| --- | --- |
| Tenant ID | `c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af` |
| Initial domain | `testdomain387.onmicrosoft.com` |
| Administrator | one delegated administrator of that tenant, recorded in lab notes only |

The tenant ID is the tenant-lock value ADEXO enforces. It is an identifier and is safe to record. The administrator UPN is an identifier but is deliberately not repeated throughout source, tests, or this document; it belongs in local lab notes.

No password is stored, requested, or transmitted by anything in this repository. Authentication is interactive through MSAL and WAM.

```text
test.domain
   |
   | Active Directory lab, Phase 2 validated
   v
ADEXO  (read-only)
   |
   | Phase 3 correlation, verified objectGUID only
   v
testdomain387.onmicrosoft.com
Tenant c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af
   |
   +-- Entra ID / Microsoft Graph   (delegated read)
   |
   +-- Exchange Online              (delegated read, Exchange RBAC)
```

## What this gate does not authorize

Explicitly still prohibited:

- authenticating to the tenant;
- creating the app registration;
- granting admin consent;
- any Graph query;
- any Exchange Online connection;
- creating cloud users;
- changing licences;
- creating, changing, or deleting mailboxes;
- changing forwarding or delegation;
- any cloud write of any kind.

Discovery produces documents. The next step requires a new authorization.

## Prerequisites that must hold before the first live step

1. Phase 3 ADR 0009 reviewed and accepted.
2. App registration created in the approved tenant, single tenant, public client, with the broker redirect URI and exactly three delegated scopes.
3. Admin consent granted once, by the administrator, outside ADEXO.
4. A cloud test user that correlates to an on-premises lab user. The Phase 2 lab objects were retained for this purpose.
5. The Phase 2 lab objects confirmed still present: three organizational units and the synthetic user, with `objectGUID` unchanged.

## App registration checklist

| Setting | Required value |
| --- | --- |
| Supported account types | Single tenant |
| Platform | Mobile and desktop applications |
| Redirect URI (broker) | `ms-appx-web://Microsoft.AAD.BrokerPlugin/<clientId>` |
| Redirect URI (browser fallback) | `http://localhost` |
| Allow public client flows | Yes |
| Client secret | none |
| Certificates | none |
| Delegated permissions | `User.Read`, `User.Read.All`, `Organization.Read.All` |
| Admin consent | granted |

Two redirect entries are registered and neither is a secret. The broker entry is the WAM redirect Microsoft
documents. The loopback entry lets MSAL complete the authorization-code flow in the system browser, which
is the bounded fallback path described in ADR 0010. Adding the loopback entry grants no additional
permission, and no application permission, secret, or certificate is added with it.

Delegated scope identifiers, verified in the Graph permissions reference on November 18, 2026:

| Scope | Delegated identifier | Admin consent required |
| --- | --- | --- |
| `User.Read` | `e1fe6dd8-ba31-4d61-89e7-88639da4683d` | No |
| `User.Read.All` | `a154be20-db9c-4678-8ab7-66f6cc099a59` | Yes |
| `Organization.Read.All` | `4908d5b9-3fb2-4b1e-9336-1888b7937185` | Yes |

No application permission is recorded, because Phase 3 requests none.

## Validation matrix

Each row is a separate, checkable observation. A row is only PASS when the stated evidence was captured. Sanitized evidence only: no tokens, no passwords, no full user identifiers beyond what the tenant ID already discloses.

### A. Authentication and tenant lock

| # | Observation | Expected | Evidence |
| --- | --- | --- | --- |
| A1 | Interactive sign-in through WAM | Broker dialog appears parented to the ADEXO window | Screenshot, no credentials visible |
| A1b | `SystemBrowserOnly` browser launch and redirect plumbing | Default browser opens, a loopback listener is created, the authorize request carries `code_challenge` with `S256`, and the redirect reaches the ADEXO "Authentication complete" loopback page | UI capture of the browser and of the loopback page, no URLs containing tokens |
| A1b-2 | `SystemBrowserOnly` interactive sign-in, T1, T2, correlation | Operator completes the credential prompt; a token is returned; T1 then T2 pass; correlation executes; `subscribedSkus` executes; sign-out succeeds | UI capture of the signed-in state and diagnostics |
| A1c | `Auto` fallback with no broker UI | Progress message states Windows sign-in could not open; the browser path starts; diagnostics record `System browser` and the fallback reason | UI capture, diagnostics showing preferred/actual/fallback reason |
| A1d | `Auto` with broker UI surfaced late | Broker UI that surfaces inside the observation window suppresses the fallback; the brokered sign-in completes | UI capture, diagnostics showing `Actual = Windows broker` and no fallback reason |
| A2 | No client secret used | No secret configured, requested, or stored | Configuration inspection |
| A3 | Signed-in administrator and tenant shown before any cloud data | Both displayed prior to the first read | UI capture |
| A4 | Token tenant ID equals approved tenant | Match asserted before any resource call | Log line with tenant ID only |
| A5 | `/organization` `id` equals approved tenant | Match asserted before any data read | Same |
| A6 | Wrong-tenant session rejected | Sign in to a different tenant, then confirm ADEXO refuses and displays no cloud data | UI capture showing the tenant-lock failure |
| A7 | No automatic fallback | After a refused or failed sign-in in `WamOnly`, no browser, application-only, or silent retry occurs | Observed behavior |
| A8 | Tokens absent from logs and diagnostics | Diagnostics bundle and log contain no token | Grep of both artefacts |

A6 is the most important row in this section. It is the only one that proves the lock actually locks.

**A1b and A1b-2 are deliberately separate rows.** A1b is what a browser launch proves on its own: the process starts, MSAL binds a loopback listener, the authorize request is well formed, and the redirect returns. It does not prove that a token was issued. A1b-2 requires a human to complete the credential prompt and is the only row that would prove the browser path end to end.

Status of the two rows as of the Phase 3 release candidate:

| Row | Status |
| --- | --- |
| A1b | **PASS** — observed live |
| A1b-2 | **PARTIALLY VALIDATED, not PASS.** The operator did not complete the credential prompt during the browser test, so no browser token was issued. Browser T1 and T2 are covered by automated tests only and were not observed live. |

A1b-2 remains a **final pre-release validation item**. It is a known validation gap, not a product defect, and it does not block Phase 4 design.

### B. AD to Entra correlation

| # | Observation | Expected | Evidence |
| --- | --- | --- | --- |
| B1 | Candidate immutable ID computed from `objectGUID` | Candidate present | Sanitized computation record |
| B2 | `mS-DS-ConsistencyGuid` candidate considered | Second candidate evaluated when populated | Same |
| B3 | Query by `onPremisesImmutableId` returns exactly one user | One match | Count only |
| B4 | Corroboration by SID or DN | Agrees | Sanitized comparison result |
| B5 | Unsynchronized AD user | `Unmatched`, explained as absent, not an error | UI capture |
| B6 | No name-based fallback | No request contains UPN, mail, or displayName as a filter input | Request log review |
| B7 | Forced ambiguity handled | With a deliberately duplicated immutable ID, ADEXO refuses and reports ambiguity | UI capture, then cleanup |

B7 requires temporarily creating a second cloud object carrying the same immutable ID, then removing it. That is a cloud write and is therefore **not authorized by this gate**; it is listed so the authorization request can include it explicitly if wanted.

### C. Entra to Exchange correlation

| # | Observation | Expected | Evidence |
| --- | --- | --- | --- |
| C1 | Mailbox lookup by `-ExternalDirectoryObjectId` | Exactly the matching mailbox | Sanitized result |
| C2 | Identity correspondence confirmed | Returned mailbox belongs to the matched Entra user | Same |
| C3 | No UPN or SMTP lookup used | Request record shows GUID parameter only | Request review |
| C4 | Synchronized user without a mailbox | `NoMailbox` displayed as an absence | UI capture |

### D. Read-only data shown

| # | Observation | Expected |
| --- | --- | --- |
| D1 | Sync state and last sync time | Displayed when populated |
| D2 | Entra object ID, UPN, display name, account state | Displayed |
| D3 | Licence names, resolved from subscribed SKUs | Displayed as product names, not GUIDs |
| D4 | Mailbox type, primary SMTP, aliases | Displayed |
| D5 | Archive state | Displayed |
| D6 | Mailbox usage | Displayed, or explicitly unavailable |
| D7 | Forwarding settings | Displayed |

Delegation and calendar permission display are **not** in this matrix. Their cmdlets were not permission-verified in discovery, and ADR 0003 requires a feature to copy the least-privilege permission from its own page when it is built.

### E. Read-only guarantee

| # | Observation | Expected |
| --- | --- | --- |
| E1 | No write cmdlet is reachable | Code inspection: allowlist contains only reads |
| E2 | Cloud object unchanged after a full session | Before and after comparison of a cloud test user |
| E3 | No write attempted on a permission failure | Failure path performs no write |
| E4 | Exchange session performs no state change | Cmdlet list exercised is reads only |

E1 is a code check, not a lab check, and must hold before the lab begins.

### F. Degradation

| # | Observation | Expected |
| --- | --- | --- |
| F1 | Missing Graph scope | `MissingPermission` naming the scope; other panels render |
| F2 | Missing Exchange role | `MissingPermission` naming the role; Graph data still renders |
| F3 | No sign-in loop after authorization failure | Single failure, no repeat prompt |
| F4 | No scope escalation attempt | No request for a broader scope on failure |

F1 and F2 require a second app registration or a role change. Both are cloud changes and are out of scope for this gate.

## Evidence rules

Record:

- the tenant ID, because it is the lock value and is already approved for recording;
- PASS or FAIL per row;
- sanitized request shapes, with identifiers replaced;
- the exact package and module versions actually used.

Do not record:

- passwords;
- access tokens or refresh tokens;
- client secrets, if any were ever created (none should be);
- the administrator password;
- full screenshots containing tokens or credentials;
- cloud user SIDs beyond what a sanitized comparison requires.

Evidence belongs in local files outside the repository, as with Phase 2. Only sanitized conclusions are committed.

## Exit criteria

Phase 3 validation passes only when:

- every row in A, B, C, D, E, and F is PASS or explicitly recorded as deferred with a reason;
- the tenant lock has been demonstrated to reject a wrong tenant (A6);
- no cloud write occurred at any point;
- the read-only guarantee (E1) held in code before the lab and in observation during it.

Current outstanding rows, both from the browser path:

- **A1b-2** — `SystemBrowserOnly` interactive sign-in, T1, T2, correlation. Partially validated: the
  operator did not complete the credential prompt, so no browser token was issued. Browser T1 and T2 are
  automated test coverage only.
- The browser-side equivalents of B, C, D, and the `subscribedSkus` read, which require a completed
  browser session.

Both are pre-release validation items. They do not block Phase 4 design, and neither is a known product
defect.
