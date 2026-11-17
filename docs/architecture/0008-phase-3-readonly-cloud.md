# Phase 3: read-only Microsoft 365 correlation

Status: proposed. Not started. Do not implement this phase until the Phase 2 lab checklist is complete.

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
  delegation
  mailbox usage
```

## Boundary

The verified `objectGUID` remains the identity key. Cloud correlation must not fall back to an unverified UPN, mail address, or distinguished name. Graph and Exchange calls use the signed-in administrator's delegated access. There is no automatic switch to application-only access. Missing permission hides or disables the related display and says which permission is absent. It does not look like "user not found."

## Not in Phase 3

License assignment, alias edits, forwarding changes, delegation changes, mailbox conversion, sign-in changes, and Entra Connect sync writes wait for a later controlled-write phase.
