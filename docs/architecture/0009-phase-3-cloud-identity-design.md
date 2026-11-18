# ADR 0009: Phase 3 read-only cloud identity design

Status: proposed. Discovery only. No cloud call has been made and no app registration exists.

This ADR is the design record for Phase 3. It exists so that implementation starts from verified facts rather than assumptions. Everything below was checked against current Microsoft documentation on November 18, 2026, and against the package feeds on the same date.

## Scope

Phase 3 is read-only. ADEXO may display cloud state for a user whose on-premises `objectGUID` has already been verified by the Phase 2 pipeline. It may not change licenses, aliases, forwarding, delegation, mailbox settings, authentication methods, or directory attributes.

Nothing in Phase 3 depends on Phase 2 having registered a menu. The cloud feature reads the user the Phase 2 launch already resolved.

## Configuration identifiers

| Item | Value | Classification |
| --- | --- | --- |
| Approved lab tenant ID | `c18ee4be-07a0-4da1-9ddf-c3ebe9bae0af` | Identifier, not a secret |
| Approved lab tenant initial domain | `testdomain387.onmicrosoft.com` | Identifier, not a secret |
| Lab administrator account | one delegated administrator in that tenant | Identifier; recorded in lab notes only, not spread through source |

The tenant ID is the tenant-lock value. The administrator UPN is configured once, in the local configuration file, and is not duplicated through source, tests, or documentation.

No password, client secret, certificate private key, refresh token, or access token is stored anywhere in this repository. Phase 3 uses interactive delegated authentication only.

## 1. Verified package versions

Checked against the NuGet and PowerShell Gallery feeds on November 18, 2026.

| Package | Latest stable | Target frameworks relevant here | Notes |
| --- | --- | --- | --- |
| `Microsoft.Identity.Client` | 4.92.0 | `net8.0`, `.NETStandard2.0`, .NET Framework 4.6.2 / 4.7.2 | Matches the pin already recorded in [0003](0003-authentication-and-permissions.md). Consumable from `net10.0-windows`. |
| `Microsoft.Identity.Client.Broker` | 4.92.0 | `.NETStandard2.0`, .NET Framework 4.6.2 | Depends on `Microsoft.Identity.Client` 4.92.0 and `Microsoft.Identity.Client.NativeInterop` 0.20.7. Required for WAM. |
| `Microsoft.Graph` | 6.10.0 | `net10.0`, `net8.0`, `.NETStandard2.0`, `.NETStandard2.1` | Ships a `net10.0` asset. This is the first Graph major we can reference without a framework downgrade. |
| `ExchangeOnlineManagement` | 3.11.1 | PowerShell module | Matches the pin in 0003. Verified present in the gallery. Note the gallery's `FindPackagesById` result set is paginated and truncated; the full version list is needed to see 3.10.x and 3.11.x. |

A single-file publish of the application must set `IncludeNativeLibrariesForSelfExtract` to `true`, or WAM fails at runtime with `wam_runtime_init_failed` because the native interop binaries are not packaged. This is a documented Microsoft limitation and is recorded here so it is not rediscovered during the lab.

## 2. Authentication design: MSAL with WAM

Source: [Acquire a token to call a web API by using Web Account Manager](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-acquire-token-wam) and [Public and confidential client apps](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications), both read November 18, 2026.

ADEXO is a **public client**. It runs on a management workstation, its compiled bytes are inspectable, and it cannot hold a client secret. Microsoft's guidance is explicit that a desktop app is a public client and that public clients cannot have client secrets. Phase 3 therefore uses no secret of any kind.

WAM is an authentication broker, available on Windows 10 and later and on Windows Server 2019 and later. The lab host is Windows Server 2025, so WAM is available. Microsoft lists the benefit as integration with accounts already known to Windows, plus Windows Hello, Conditional Access, and FIDO support.

Design:

```text
PublicClientApplicationBuilder
  .Create(<clientId>)
  .WithBroker(BrokerOptions.OperatingSystems.Windows)
  .WithAuthority("https://login.microsoftonline.com/<approved-tenant-id>")
  .WithRedirectUri("ms-appx-web://Microsoft.AAD.BrokerPlugin/<clientId>")
```

Authorization is the OAuth 2.0 authorization code flow with PKCE, executed by WAM. There is no device code flow, no ROPC, no client secret, and no application-only fallback. A failed interactive sign-in does not fall through to any other mode; this preserves the rule already stated in ADR 0003.

Interactive acquisition must be parented to the application window through `WithParentActivityOrWindow(hwnd)`. Without it the broker dialog can appear detached from ADEXO.

Silent acquisition is attempted first from the cache, falling back to `PublicClientApplication.OperatingSystemAccount`, then to interactive. `MsalUiRequiredException` is the only signal that triggers interactive sign-in; any other failure is surfaced as an error and does not prompt again.

### Token cache

Tokens live only in the MSAL user token cache, protected at rest by the platform. Phase 3 does not write tokens to a file of its own, to logs, to the diagnostics bundle, or to the audit trail. `SecretText` redaction from Phase 1 continues to apply to anything that touches diagnostics.

### Exchange Online

`Connect-ExchangeOnline` uses modern authentication and cannot use Basic authentication. For a delegated administrator the supported pattern is an interactive prompt with `-UserPrincipalName`. The module accepts `-DisableWAM`, which we will not use: WAM is the intended broker.

Connecting a separate PowerShell runspace is deliberately deferred. Phase 3 discovery records the intent and the constraint; the implementation decision about whether to host Exchange cmdlets in-process or in a child runspace belongs to the build, and either way the connection is delegated and interactive.

## 3. Tenant lock

ADEXO must reject any authenticated session whose tenant is not the configured approved tenant. This is a hard gate, not a warning.

Two independent checks, both required:

1. **Token claim, before any resource call.** After authentication, compare `AuthenticationResult.TenantId` to the configured approved tenant ID. A mismatch aborts before the first Graph request is composed, so a wrong tenant costs no directory read.
2. **Directory identity, before any data read.** Call `GET /organization` and compare the returned `id` to the configured approved tenant ID. A mismatch aborts and reports a tenant-lock failure.

Both are needed because the first is cheap and catches the common mistake of signing into the wrong account, while the second proves the token is actually being honoured by the directory we expect.

Least privilege makes check 2 almost free. Microsoft documents that `User.Read` alone returns `id`, `displayName`, and `verifiedDomains` from `/organization`; every other property comes back null. Tenant verification needs only `id`, so **no additional scope is required for the tenant lock**. `Organization.Read.All` is added later, and only for licence names.

Failure behavior: the cloud surface shows a tenant-lock failure naming the configured tenant, and no cloud data is displayed. It never renders as "user not found".

## 4. Graph delegated permission matrix

Delegated only. Phase 3 requests no application permission of any kind, because there is no unattended job in this phase.

| Datum displayed | Endpoint | Delegated scope | Admin consent | Notes |
| --- | --- | --- | --- | --- |
| Signed-in administrator identity | `GET /me` | `User.Read` | No | Shown before any cloud data, per ADR 0003. |
| Tenant identity for the tenant lock | `GET /organization` | `User.Read` | No | Returns `id`, `displayName`, `verifiedDomains` only. Sufficient for the lock. |
| Entra user matched to the AD object | `GET /users?$filter=onPremisesImmutableId eq '{value}'` | `User.Read.All` | Yes | `User.Read` cannot read other users. `User.ReadBasic.All` is not used because it excludes the on-premises correlation properties. |
| Entra object ID, UPN, display name, account state | same, `$select` narrowed | `User.Read.All` | Yes | `id` is an opaque identifier and is never parsed. |
| Assigned licences on the user | `GET /users/{id}?$select=assignedLicenses,licenseAssignmentStates` | `User.Read.All` | Yes | `assignedLicenses` is part of the user resource. |
| Subscribed SKUs and friendly names | `GET /subscribedSkus` | `Organization.Read.All` | Yes | Needed to translate a SKU GUID into a product name. |
| On-premises sync state and source attributes | `GET /users/{id}?$select=onPremisesSyncEnabled,onPremisesLastSyncDateTime,onPremisesImmutableId,onPremisesSamAccountName,onPremisesSecurityIdentifier,onPremisesDistinguishedName,onPremisesDomainName,onPremisesUserPrincipalName` | `User.Read.All` | Yes | These properties require `$select` to be returned. |

Not requested: `Directory.Read.All`, `Directory.ReadWrite.All`, `User.ReadWrite.All`, `LicenseAssignment.ReadWrite.All`, `User.EnableDisableAccount.All`. Phase 3 has no write path, so none of the write scopes is requested.

Verified consent facts from the Graph permissions reference: `User.Read` requires no admin consent; `User.Read.All` and `Organization.Read.All` both require admin consent. Both delegated scope identifiers are recorded in the lab-validation document for the app registration.

## 5. Entra public-client app-registration design

To be created **after** this gate is reviewed. Design only:

| Setting | Value | Reason |
| --- | --- | --- |
| Supported account type | Accounts in this organizational directory only (single tenant) | The application is for one approved tenant. Single tenant removes an entire class of cross-tenant mistakes before ADEXO's own tenant lock even runs. |
| Platform | Mobile and desktop applications | Public client. |
| Redirect URI | `ms-appx-web://Microsoft.AAD.BrokerPlugin/<clientId>` | The broker redirect Microsoft documents for WAM. `http://localhost` is not used. |
| Allow public client flows | Yes | Required for a public client desktop app. |
| Client secret | None | A public client cannot hold one, and none is needed. |
| Certificates | None | No application-only authentication in Phase 3. |
| API permissions | `User.Read`, `User.Read.All`, `Organization.Read.All` (delegated) | Exactly the matrix above. |
| Admin consent | Granted once by the lab administrator, deliberately, outside ADEXO | ADEXO never attempts to grant its own consent. |
| Owner | The lab administrator account | Single owner, lab tenant only. |

The client ID is an identifier and may be committed. The tenant ID is an identifier and may be committed. Neither is a credential.

## 6. AD to Entra correlation

The verified AD `objectGUID` remains the identity key, exactly as in Phase 2. The question is how to translate it into an Entra user without ever trusting a mutable name.

ADEXO must not search Entra by UPN, `mail`, `displayName`, or `sAMAccountName`. Those are mutable and can be reassigned, and a match on any of them is not proof of the same object.

The documented join is `onPremisesImmutableId`. Microsoft describes it as the property that associates an on-premises Active Directory user account with the Microsoft Entra user object. It is populated by directory synchronization and is a required `$select` to retrieve.

Design:

1. Compute candidate immutable IDs locally from attributes the Phase 2 read already has, or that a bounded extra AD read supplies:
   - `Base64(objectGUID)` — the default source used by directory synchronization.
   - `Base64(mS-DS-ConsistencyGuid)` when that attribute is populated — the value used when the deployment is configured to source the immutable ID from it.
2. Query Entra once per candidate with an exact `$filter=onPremisesImmutableId eq '{candidate}'`.
3. Accept a match only when exactly one user is returned.
4. Corroborate the accepted match against `onPremisesSecurityIdentifier` when both sides have a SID, and against `onPremisesDistinguishedName` when present. Corroboration can reject a match. It can never create one.

Outcomes, all explicit and distinguishable:

| Outcome | Meaning | Display |
| --- | --- | --- |
| `Matched` | Exactly one user, at least one corroborating attribute agrees | Cloud data shown |
| `Unmatched` | No user carries any candidate immutable ID | "This AD user is not synchronized to the cloud" — **not** an error, and never a name search |
| `Ambiguous` | More than one user carries a candidate immutable ID | Refuse to display; report a directory-hygiene problem. ADEXO does not pick one. |
| `Conflict` | A match was found but a corroborating attribute disagrees | Refuse to display; report the disagreement |
| `TenantMismatch` | Tenant lock failed | Refuse; no data |

A user who exists only in the cloud, with no on-premises counterpart, cannot be reached from an AD selection at all. That is correct for Phase 3: the entry point is an AD object.

## 7. Entra to Exchange correlation

Once an Entra user is established, the Exchange lookup must use the same non-mutable key.

`Get-EXOMailbox` accepts `-ExternalDirectoryObjectId <Guid>`, documented as identifying the mailbox "by the ObjectId in Microsoft Entra ID". That is the Entra user `id`, which is opaque and stable across renames.

Design:

1. Take the Entra user `id` from the matched Graph read.
2. `Get-EXOMailbox -ExternalDirectoryObjectId <entraUserId> -Properties ...`
3. Require the returned mailbox to correspond to that same object. A reply whose identity does not match is treated as a failure, not as a mailbox.
4. Never resolve the mailbox by UPN, primary SMTP address, alias, `Name`, or `SamAccountName`, even though the cmdlet would accept them.

`Get-EXOMailbox -Identity` accepts many forms including UPN and legacy DN. Those forms are deliberately avoided. The caution in the cmdlet documentation is also recorded: a `$null` or non-existent `Identity` value returns **all** objects, as if the filter were omitted. Using the GUID parameter avoids that failure mode entirely.

A mailbox may legitimately not exist for a synchronized user. That is `NoMailbox`, displayed as an absence, not as an error.

## 8. Exchange read-command allowlist

Phase 3 permits exactly these reads. Anything not on this list is not called, including any cmdlet that changes state.

| Purpose | Cmdlet | Key parameter | RBAC role required by the administrator |
| --- | --- | --- | --- |
| Mailbox existence and type | `Get-EXOMailbox` | `-ExternalDirectoryObjectId` | View-Only Recipients, or Mail Recipients |
| Primary SMTP and aliases | `Get-EXOMailbox` | `-Properties EmailAddresses, PrimarySmtpAddress` | same |
| Archive state | `Get-EXOMailbox` | `-Archive`, or `-PropertySets Archive` | same |
| Forwarding and delivery | `Get-EXOMailbox` | `-Properties ForwardingSmtpAddress, DeliverToMailboxAndForward, ForwardingAddress` | same |
| Mailbox usage | `Get-EXOMailboxStatistics` | identity by mailbox GUID already obtained | View-Only Recipients |

Deliberately **not** on the list, and to be added only with its own verified permission when a later feature needs it: `Get-MailboxPermission` for delegation, `Get-MailboxFolderPermission` for calendar delegation, `Get-InboxRule`, `Get-MailboxAutoReplyConfiguration`. The delegation display in Phase 3's intent list is deferred until that cmdlet's RBAC requirement is verified against its own documentation page, following the rule in ADR 0003 that a feature copies the permission from its own page when it is built.

The old `Get-Mailbox` cmdlet is not used. The `-EXO` variants use the REST-backed module and avoid the remote PowerShell session model.

`Get-MailboxStatistics` on the `-EXO` route is recorded here as intended but its exact parameter surface for usage data must be confirmed on its own page before implementation; only the existence and general purpose were verified in this discovery pass.

## 9. Degradation

Every cloud datum is independently degradable.

- Tenant lock fails: no cloud data at all.
- Graph permission missing: the affected panel shows `MissingPermission` with the scope name. Other panels still render.
- Exchange role missing: the mailbox panel shows `MissingPermission` with the role name. Graph data still renders.
- No mailbox: `NoMailbox`, an absence.
- Correlation `Unmatched`: an explicit absence with an explanation.
- Correlation `Ambiguous` or `Conflict`: refuse and explain, never guess.

ADEXO does not loop sign-in after an authorization failure, and does not retry with a broader scope.

## Consequences

- Phase 3 adds two provider implementations behind the existing contracts: `IGraphProvider` and `IExchangeOnlineProvider`. Neither exposes a write.
- No new write operation, no new change pipeline use, and no mutation of any cloud or on-premises attribute.
- The read-only guarantee from Phase 2 extends to the cloud: the application has no cloud write path to disable, because none is built.

## Revisit conditions

Revisit this ADR if any of the following is observed in the lab:

- `onPremisesImmutableId` is not populated, or is populated from an attribute other than the two candidates above. The lab must then record the actual source rather than assume.
- The broker redirect URI is rejected by the tenant.
- `User.Read` does not return the organization `id` as documented.
- `Get-EXOMailbox -ExternalDirectoryObjectId` returns an object whose identity does not match.

Each of these would change the design rather than be worked around.
