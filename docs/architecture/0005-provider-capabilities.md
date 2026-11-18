# ADR 0005: Provider capabilities

Status: accepted for ADEXO v1

The screen does not decide whether a field can be edited. Each provider reports its capability for the selected object and the signed-in administrator. The control renders that report.

## Providers

| Provider | System |
| --- | --- |
| `ActiveDirectoryProvider` | On-premises Active Directory |
| `MicrosoftGraphProvider` | Microsoft Entra ID and Microsoft Graph |
| `ExchangeOnlineProvider` | Exchange Online |
| `EntraConnectProvider` | Optional Entra Connect sync server |

A provider that has not been configured, or whose sign-in has not completed, still returns a capability. `Available` is false and `UnsupportedReason` says why. The rest of the screen can show data from providers that succeeded.

## Capability

```text
CanRead
CanWrite
Authorized
Available
SourceOfAuthority
MissingPermission
UnsupportedReason
```

- `CanRead` and `CanWrite` describe the product's support for the operation, independent of this administrator.
- `Authorized` is false when this administrator lacks the AD right, Graph permission, or Exchange role.
- `Available` is false when the domain controller, Graph, Exchange, or the sync server cannot be reached, or when the feature is disabled by configuration.
- `SourceOfAuthority` is one of: on-premises Active Directory, Microsoft Entra ID, Exchange Online, read-only calculated status, or unsupported in this environment.
- `MissingPermission` names the permission or role when `Authorized` is false. It is empty otherwise.
- `UnsupportedReason` is the sentence the control shows when the field cannot be edited here.

## How a control uses it

The control shows the value, the source, whether it is editable, and where to edit it when another system is authoritative.

```text
Archive mailbox
  Enabled
  Source: Exchange Online
  Permission: Available
  Editable: Yes

Primary SMTP
  liam@contoso.com
  Source: On-premises Active Directory
  Editable in Exchange Online: No
  Edit using: proxyAddresses
```

Rules:

- A cloud field whose authoritative copy is on-premises is read-only. The control names the on-premises attribute.
- A group-inherited license is read-only. The control says the license comes from a group.
- Dynamic group membership is read-only and says the membership is dynamic.
- Entra Connect delta sync reports `Available` false until an authorized server is configured. After that, `CanWrite` covers only `Start-ADSyncSyncCycle -PolicyType Delta`, with the cooldown from [0003-authentication-and-permissions.md](0003-authentication-and-permissions.md).
- An authorization failure is `Authorized` false plus `MissingPermission`. It is not presented as a missing user.
- Partial data stays on screen when one provider is down. Stale cached reads are labeled with their age.

## What Phase 1 implements

Phase 1 defines the capability type and tests that a view model hides the apply action when `CanWrite` or `Authorized` is false. It does not connect to a directory or a tenant.
