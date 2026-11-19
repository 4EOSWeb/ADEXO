# Threat model

Status: accepted for ADEXO v1. This is a design summary. It is not a penetration test.

ADEXO runs as the administrator who started it, on a management workstation, and talks to Active Directory, Microsoft Graph, Exchange Online, and optionally an Entra Connect server. The trust boundary is the companion process. ADUC is outside that boundary.

## Assets

- The administrator's refresh token and access token.
- The app-only certificate private key, when that mode is configured.
- Directory and mailbox data shown for the selected object.
- The audit log.
- The forest display-specifier values that publish the menu.

## Actors

- The signed-in administrator, who is allowed to do what AD, Entra, and Exchange already allow.
- Another local user on the same workstation.
- A forest administrator who can write display specifiers.
- A tenant attacker who phishes the administrator or steals the app-only certificate.

## Boundaries

```text
mmc.exe  --ShellExecute-->  ADEXO.exe  --delegated token-->  Graph and Exchange
                                |
                                +-- LDAP as the Windows user
                                +-- optional restricted remoting to Entra Connect
                                +-- named pipe, current user only
```

No SYSTEM service sits on that path. Interactive failure does not switch to the app-only certificate.

## Abuse cases

| Case | Mitigation |
| --- | --- |
| A crash or hang in ADEXO takes down ADUC | v1 loads no ADEXO code in `mmc.exe`. See [0001-aduc-integration.md](../architecture/0001-aduc-integration.md). |
| Another local user connects to the pipe and requests a cloud write | The pipe ACL and a peer-token check allow only the user who owns the companion. Requests without a valid nonce are dropped. |
| The pipe message is used to inject a PowerShell string | The message contains an ADsPath, a class, and a nonce. Cmdlet parameters are typed. Administrator text is not concatenated into a command. |
| A forged ADsPath selects an object outside the configured forest | The companion resolves the GUID and rejects paths that are not LDAP paths in a configured naming context. |
| Tokens are copied from logs or a support bundle | Tokens, secrets, cookies, and passwords are excluded from logs, the audit trail, and the bundle. The support bundle is sanitized before export. |
| Interactive sign-in fails and the app-only certificate is used instead | The modes are separate. There is no fallback. |
| A workstation MSI changes the forest | The MSI does not write the configuration partition. Forest scripts are separate and start with a backup. |
| An administrator is shown a write for a value that Entra Connect will overwrite | `SourceOfAuthority` makes the control read-only and names the on-premises attribute. |
| External forwarding is enabled without a record | The change operation requires confirmation, shows the destination, and audits the administrator, the destination, and the result. ADEXO does not weaken the tenant's outbound policy. |
| Two administrators write the same attribute | `GetCurrentState` captures the value used for the preview. `Execute` checks that it is still current. A mismatch stops the write. |
| Rollback restores a `proxyAddresses` set that has since changed | Rollback is refused unless the re-read matches the expected restored state. Otherwise the UI shows the actual state. |
| Display-specifier registration points at a replaced executable path | `Test-ADEXOForestRegistration.ps1` compares the registered command with the installed path. |
| The audit log is edited to hide a change | The log is stored so that casual users cannot rewrite it. Phase 9 decides the file ACL or an append-only store. v1 does not claim tamper-proof storage beyond that review. |

## Phase 2 launch and directory reads

- A malicious ADsPath is parsed as data. It is not passed to a shell. Trailing command text outside the LDAP URL is rejected. Valid punctuation inside a distinguished name is accepted.
- The host in the ADsPath does not receive Windows credentials unless it is the domain controller Windows discovery returned.
- Negotiate is required. Basic, anonymous, and weaker retries are not used. A synchronous LDAP call is bounded by a timeout and is not described as canceled merely because the UI moved on.
- Registration stores one exact menu value. Unregistration removes that value or stops when more than one ADEXO entry exists. Quoting of `C:\Program Files\ADEXO\ADEXO.exe` was confirmed in the isolated Phase 2 lab: the quoted path arrived intact as a single argument through the real static context-menu invocation.
- Default diagnostics omit distinguished names, mail, proxy addresses, and SIDs. A detailed copy is labeled sensitive.
- The registration scripts are not run by the application. A stale backup is not written back over unrelated menu entries.

## Phase 3 cloud reads (design, not implemented)

Phase 3 adds delegated, read-only Microsoft Graph and Exchange Online access. These rules bind the implementation.

- ADEXO is a public client. It holds no client secret, no certificate, and no application-only credential in any phase-3 path.
- Authentication is interactive through MSAL with the Windows broker. A failed interactive sign-in never falls through to a silent, device-code, or application-only path.
- Access tokens and refresh tokens are excluded from logs, the audit trail, diagnostics, the support bundle, and any file ADEXO writes itself. The MSAL token cache is the only token store.
- The tenant lock is enforced twice: the token tenant identifier is compared to the configured approved tenant before any resource request is composed, and the `/organization` identifier is compared before any data is read. A mismatch stops the phase with no cloud data displayed.
- Correlation uses `onPremisesImmutableId`, derived from the verified on-premises `objectGUID` or the consistency GUID. A search by UPN, mail, display name, or `sAMAccountName` is not performed, because a mutable name is not proof of identity.
- The Exchange lookup uses the Entra object identifier, not a UPN, alias, or SMTP address. `Get-EXOMailbox -Identity` is avoided because a null or non-existent value returns every mailbox.
- Ambiguous or conflicting correlation refuses to display rather than choosing a candidate.
- Phase 3 has no cloud write path to disable, because none is built. A missing permission is shown as a missing permission and never triggers a broader scope request.
- Interactive authentication prefers the Windows broker. A bounded system-browser fallback (authorization code with PKCE, MSAL-managed) runs only when the broker produces no usable interactive UI within 20 seconds or fails to activate. The window detects absence of UI and is not an authentication deadline; once broker UI surfaces, an administrator may take as long as needed for MFA or Conditional Access.
- The fallback is an accepted compatibility trade-off. The browser path does not claim the broker's device-bound properties: device-based Conditional Access, Windows Hello through the broker, and token protection are broker-only. The broker is always attempted first in the default mode, `WamOnly` is available where broker-only is required, the two flows never run as simultaneous active sessions, and T1 and T2 are mandatory on both paths.
- An available broker does not guarantee usable interactive broker UI in every host or session. The project's own harness authenticated through the broker on a device without a primary refresh token, so "no PRT means WAM fails" is not the explanation and is not recorded as one. Live validation observed both outcomes on the same host in the same session: a broker attempt that surfaced no UI (fallback ran) and an attempt that surfaced UI after about 15.6 seconds and authenticated through the broker.
- Conditional Access is never bypassed. A device-based control the browser path cannot satisfy fails authentication and displays the real policy error.
- Sign-in can always be abandoned. Each interactive attempt runs under its own cancellation token and a Cancel sign-in command is enabled only while an attempt is in flight, so a browser window the operator closed cannot leave the application with every command disabled.
- The copied diagnostics name the preferred method, the actual method, the fallback reason, the elapsed broker time, the cloud correlation state, and the Exchange connection state, and contain no token, bearer value, refresh token, or secret. A browser-fallback session is therefore never indistinguishable from a brokered one, whose security properties differ.

## Out of scope for the product

ADEXO does not extract passwords, bypass MFA or Conditional Access, read other users' session cookies, or store administrator passwords.
