# ADR 0004: Change operations

Status: accepted for ADEXO v1

Every write uses one operation type. The WPF layer does not call Active Directory, Microsoft Graph, or Exchange Online itself. This is required from the first editable control, not added after several one-off buttons exist.

## Contract

```csharp
interface IChangeOperation
{
    Task<CurrentState> GetCurrentStateAsync(CancellationToken cancellationToken);
    Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken);
    Task<ChangePreview> PreviewAsync(CancellationToken cancellationToken);
    Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken);
    Task<VerificationResult> VerifyAsync(CancellationToken cancellationToken);
    Task<RollbackResult> RollbackAsync(CancellationToken cancellationToken);
}
```

The types are defined in the contracts project in Phase 1. Names may be split into smaller interfaces if a test shows that a reader and a writer need different lifetimes. The six steps stay.

## Pipeline

```text
Preview
  -> Validate
  -> Authorize
  -> Execute
  -> Re-read
  -> Verify
  -> Audit
```

Apply stays disabled until validation and authorization succeed. Authorization uses the provider capability from [0005-provider-capabilities.md](0005-provider-capabilities.md). A missing permission stops the pipeline before `Execute`.

A preview shows:

- the current value
- the requested value
- the provider
- the attribute or cmdlet
- the action, such as add, remove, or replace
- the impact, including synchronization through Entra Connect when on-premises Active Directory is the source of authority

Example for an alias change:

```text
Current     smtp:lhoyle@old.example
Requested   smtp:lhoyle@new.example
Provider    Active Directory
Attribute   proxyAddresses
Action      Replace
Impact      Will synchronize through Entra Connect
```

The administrator can cancel or apply. Destructive and security-sensitive changes, including external forwarding and session revocation, require an explicit confirmation beyond the preview.

## Safety rules

- `GetCurrentState` reads by `objectGUID`, not by a distinguished name that may have changed.
- `Execute` writes the smallest set of attributes or parameters. PowerShell parameters are typed objects. Command text is not built by concatenating administrator input.
- `Verify` re-reads the object and compares the confirmed state with the requested state.
- The audit record stores the before state, the requested state, the confirmed state, the administrator, the workstation, the tenant, both object identifiers, the correlation ID, and the result. It does not store passwords, tokens, or message bodies.
- `Rollback` runs only when the restored state can be re-read and confirmed. If another administrator may have changed the object, ADEXO does not write back an entire saved `proxyAddresses` collection. It reports the actual state and a corrective action.
- A sequence that touches more than one provider is not labeled a transaction. Each completed step is recorded. A later failure stops the sequence and marks it partial.
- Non-idempotent writes are not retried automatically. Graph reads honor `Retry-After` and use bounded backoff with jitter.
- The UI thread does not wait on LDAP, Graph, Exchange, or PowerShell. Every step takes a `CancellationToken`.

## Phase 1 amendment

The pipeline above still stands. Two consequences changed when the contracts were implemented:

- A succeeded operation stays `Succeeded`. `HasCompensatingOperation` records that an administrator can request a reversal. That reversal is a new operation with `ParentOperationId`, its own preview, authorization, concurrency check, execution, verification, and audit record. It reads the current state before it changes anything. `RollingBack` remains only for compensation of an operation that stopped before success.
- `SecretText` is the way a secret enters logs, audit records, and diagnostics. `ToString` and JSON render `[REDACTED]`. Pattern matching may hide a recognizable bearer token in a diagnostic string. It is not the control that finds passwords or client secrets in arbitrary text.

Phase 1 does not perform a directory or cloud write. There is no fake provider in the production projects.
