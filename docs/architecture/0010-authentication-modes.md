# Phase 3 authentication: WAM primary with a bounded browser fallback

Status: implemented. This note records the decision and the trade-off.

## What the lab established

The isolated WPF harness authenticated interactively through the Windows broker once, and the product
authenticated through its cached session with T1 and T2 passing. Later broker attempts on the same host
stopped producing usable interactive UI, and a clean reboot did not restore it.

The precise statement, which matters:

> An available WAM broker does not guarantee usable interactive broker UI in every Windows host or
> session.

It is **not** accurate to say that a missing primary refresh token makes WAM fail. This project's own
harness authenticated through the broker on a device reporting `AzureAdPrt: NO`, which disproves that
explanation. The broker was present and healthy throughout; what failed was the surfacing of its
interactive surface.

## Decision

`AuthenticationMode.Auto` is the default:

1. Attempt silent authentication from a cached account.
2. If interaction is required, start the Windows broker.
3. Observe the broker for **20 seconds**.
4. If the broker's interactive UI does not surface, or the broker fails to activate, retire that attempt
   and continue in the system browser.
5. Apply T1, then T2, exactly as before.

`WamOnly` and `SystemBrowserOnly` are also supported. `WamOnly` never launches a browser and reports its
failure honestly; it is for organizations that require broker-only authentication. `SystemBrowserOnly`
skips the broker entirely and exists so the browser path can be validated in isolation.

## The 20 seconds is not an authentication deadline

It detects the absence of usable broker UI. Once the broker's UI has surfaced, or the broker reports
that interaction is active, the window stops applying. An administrator may take as long as the identity
platform needs for account selection, credentials, MFA, Conditional Access, or a password reset, and no
fallback occurs.

`BrokerSurfacingProbe` enforces this, and a test covers a UI that surfaces after the window would have
expired.

## Both paths share the same identity rules

| | Windows broker | System browser |
| --- | --- | --- |
| Client identifier | same | same |
| Authority | single tenant | single tenant |
| Scopes | `User.Read`, `User.Read.All`, `Organization.Read.All` | same |
| Redirect | broker-managed | `http://localhost` (MSAL loopback) |
| T1 token tenant check | required | required |
| T2 `/organization` check | required | required |
| Secret or certificate | none | none |

The browser path uses MSAL's own authorization code flow with PKCE. No code verifier, challenge, or
token exchange is implemented in this repository, and tests assert that no such code exists.

## Accepted trade-off

The browser path does **not** carry the same device-bound guarantees as the broker.

| Property | Broker | System browser |
| --- | --- | --- |
| User MFA and user-based Conditional Access | Yes | Yes |
| Device-based Conditional Access | Yes | Not claimed |
| Windows Hello / FIDO through the broker | Yes | Not claimed |
| Token protection (device binding) | Yes | Not claimed |

Mitigations:

- The broker remains preferred and is always attempted first in `Auto`.
- `WamOnly` is available where broker-only is a requirement.
- The two flows never run as simultaneous active sessions; the broker attempt is retired before the
  browser starts, and an attempt generation prevents a late broker result from replacing a browser
  result.
- T1 and T2 are mandatory on both paths; there is no exception.
- Conditional Access is never bypassed. If the tenant requires a device-based control the browser path
  cannot satisfy, authentication fails and the real policy error is displayed.

## Deployment note

ADEXO should run non-elevated. The broker path requires a normal interactive session, and the product
does not need elevation for any Phase 3 operation.
