# ADR 0003: Authentication and permissions

Status: accepted for ADEXO v1

ADEXO has two authentication modes. A failed interactive sign-in never falls through to application access.

## Mode 1: interactive delegated access

Used for the administrator who launched `ADEXO.exe`.

- MSAL.NET (`Microsoft.Identity.Client` 4.92.0, rechecked before the project references it).
- OAuth 2.0 authorization code flow.
- Web Account Manager when the Windows broker package is verified for that MSAL version.
- The signed-in account and tenant are shown before cloud calls.
- The tenant is checked against the configured directory before an operation runs.
- The token cache is protected with Windows DPAPI through the MSAL cache extension that Phase 4 verifies. Tokens are not written to logs, the audit trail, or the diagnostics bundle.
- Sign-out removes the cache.

Conditional Access and MFA are handled by the identity platform. ADEXO does not bypass them.

## Mode 2: application access

Used only for an unattended job that an administrator has separately approved.

- Certificate-based authentication. No client secret in source, configuration files, scripts, registry values, or MSI properties.
- The configuration stores the tenant ID, the application ID, and the certificate thumbprint or a reference to a certificate in the current user's store.
- Exchange Online app-only sign-in uses `Connect-ExchangeOnline` with the certificate, application ID, and organization, as documented for [app-only authentication](https://learn.microsoft.com/en-us/powershell/exchange/app-only-auth-powershell-v2).
- Audit records mark these operations as app-only.
- The app registration's permissions are narrower than the interactive administrator's, and they are not a fallback path.

## Who the call runs as

Interactive Graph and Exchange calls use the signed-in administrator's delegated token and Exchange RBAC roles. Active Directory writes use that administrator's existing AD permissions. ADEXO does not change AD ACLs, and it does not require Domain Admin or Global Admin for routine work.

Entra Connect is optional. Delta sync stays disabled until an authorized server is configured. The allowed remote command is `Start-ADSyncSyncCycle -PolicyType Delta`. Unrestricted delegation is not enabled. A full sync is a later advanced setting with a separate warning.

## Permission matrix

Least-privilege permissions below come from Microsoft Graph v1.0 documents read on November 17, 2026. A feature still copies the permission from its own Graph or Exchange page when it is built. The provider reports a missing permission through `MissingPermission` (see [0005-provider-capabilities.md](0005-provider-capabilities.md)). `Directory.ReadWrite.All` is not requested.

| Feature | Graph delegated | Graph application | Exchange / AD | Admin consent | Notes |
| --- | --- | --- | --- | --- | --- |
| Read the signed-in administrator | `User.Read` | Not used | None | No | Profile of the current admin only. |
| Read another user's directory profile | `User.Read.All` | `User.Read.All`, only if an approved app-only job reads users | AD read of the selected object | Yes for `User.Read.All` | `User.Read` does not read other users. |
| List subscribed SKUs and friendly names | `Organization.Read.All` | Same, only for an approved app-only job | None | Yes | Used to name licenses. Not a write. |
| Assign or remove a direct license | `LicenseAssignment.ReadWrite.All` | Same, only for an approved app-only job | Entra role with `microsoft.directory/users/assignLicense`, such as License Administrator | Yes | Least privilege for `POST /users/{id}/assignLicense`. `User.ReadWrite.All` and `Directory.ReadWrite.All` are higher and are not requested. Source: [user: assignLicense](https://learn.microsoft.com/en-us/graph/api/user-assignlicense). |
| Block or unblock Entra sign-in | `User.EnableDisableAccount.All` plus `User.Read.All` | Same pair, only for an approved app-only job | None for the Graph write | Yes | Least privilege for `accountEnabled`. Source: [Get user](https://learn.microsoft.com/en-us/graph/api/user-get), permissions for specific scenarios. |
| Mailbox, aliases that Exchange owns, delegation, calendar permissions, forwarding, automatic replies | None unless a later Graph page documents a narrower mailbox API | None by default | The administrator's Exchange Online RBAC role that contains the cmdlet | Exchange role assignment | Graph is not treated as a full mailbox API. `ExchangeOnlineManagement` 3.11.1 runs the cmdlet. |
| On-premises attributes, including `proxyAddresses` when AD is authoritative | None | None | The administrator's existing write permission on that attribute | No Graph consent | The product does not grant itself AD rights. |
| Group-inherited license | Read with the license permissions above | None | None | Already covered | The UI does not remove a license inherited from a group. |
| Entra Connect delta sync | None | None | Remote PowerShell to the configured sync server, restricted to `Start-ADSyncSyncCycle -PolicyType Delta` | Sync-server admin, configured explicitly | Disabled until that server is set. Five-minute cooldown. |

Revoke-session and authentication-method reads are not pinned here. The phase that adds them copies the least-privilege permission from the current Graph page for that API. Authorization failure is shown as missing permission, not as "user not found."

## Degradation

When a permission or role is missing, the control stays visible, shows `MissingPermission` or `UnsupportedReason`, and does not call the write. Read-only data from providers that succeeded is still shown. ADEXO does not loop sign-in after a single authorization failure.
