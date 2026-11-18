# Phase 3: read-only Microsoft 365 correlation

Status: discovery. Not implemented. The design is in [0009-phase-3-cloud-identity-design.md](0009-phase-3-cloud-identity-design.md) and the validation plan is in [Phase 3 Microsoft 365 lab validation](../testing/phase-3-m365-validation.md).

## Intent

Phase 3 is the first Microsoft 365 milestone. It stays read-only. ADEXO may display cloud state for a user whose on-premises `objectGUID` has already been verified. It may not change licenses, aliases, forwarding, delegation, mailbox settings, or directory attributes.

```text
Selected AD user
        |
        v
Verified objectGUID
        |
        v
Determine hybrid or cloud relationship
        |
        +-- Microsoft Entra ID / Graph
        |
        +-- Exchange Online
        |
        v
Correlate identities
        |
        v
Display, without cloud writes:
  sync state
  Entra object ID
  UPN
  licenses
  mailbox existence and type
  primary SMTP
  aliases
  archive status
  forwarding
  mailbox usage
```

## Boundary

The verified `objectGUID` remains the identity key. Cloud correlation must not fall back to an unverified UPN, mail address, or distinguished name. Graph and Exchange calls use the signed-in administrator's delegated access. There is no automatic switch to application-only access. Missing permission hides or disables the related display and says which permission is absent. It does not look like "user not found."

Correlation uses `onPremisesImmutableId`, and the Exchange lookup uses the Entra object identifier. Neither step searches by a mutable name. See ADR 0009 for the candidate derivation, the outcomes, and the refusal rules.

## Tenant lock

ADEXO refuses any authenticated session whose tenant is not the configured approved tenant. The check runs twice: against the token tenant identifier before any resource call, and against the `/organization` identifier before any data read. The approved tenant for lab validation is recorded in the validation document.

## Not in Phase 3

License assignment, alias edits, forwarding changes, delegation changes, mailbox conversion, sign-in changes, and Entra Connect sync writes wait for a later controlled-write phase.

Delegation and calendar-permission display are also deferred. Their cmdlets were not permission-verified during discovery, and a feature copies its least-privilege permission from its own documentation page when it is built.
