# ADR 0007: ADUC launch contract

Status: accepted for Phase 2 implementation. Windows domain-lab observation is pending.

## Context

Phase 0 chose a static `adminContextMenu` command so ADEXO does not load inside `mmc.exe`. Phase 2 has to parse that launch and resolve the selected user by `objectGUID`.

The current Microsoft Learn page [Registering a Static Context Menu Item](https://learn.microsoft.com/en-us/windows/win32/ad/registering-a-static-context-menu-item) was rechecked on November 17, 2026.

## Decision

ADEXO registers one static administrative menu item. The value shape is `<order>,<menu text>,<command>`. The command is only the absolute path of `ADEXO.exe`. ADUC invokes that command with `ShellExecute` and, because the command is an application, appends the selected object's ADsPath and class. ADEXO treats those two arguments as data. It does not pass them to a command interpreter.

The menu caption is `Manage with ADEXO`. Registration is locale-specific. Phase 2 requires the locale to be chosen explicitly. `409` means English and is not a silent default. Administrative snap-ins do not support multiple selection for static items, so Phase 2 does not either. `shellContextMenu` is not used.

Display-specifier data is cached until ADUC or `mmc.exe` is reloaded. That caching behavior is documented by Microsoft and has not been observed in this repository's lab.

### Documented

ADUC appends the ADsPath and class to an application command. The command value cannot contain extra parameters such as `Notepad.exe Myfile.txt`. Paths with spaces are allowed. The arguments are separated by a space.

### Implemented assumption

The registration value stores the absolute executable path. When that path contains a space, the path itself is wrapped in quotes so the command value is still only the path. The quoting strategy is `"C:\Program Files\ADEXO\ADEXO.exe"`.

### Not yet verified

Actual argv tokenization when the executable path contains spaces has not been captured from ADUC. A temporary lab build path is allowed only with the explicit lab override. No observed argument array is recorded here.

## Alternatives Considered

- Reject quotes and shell punctuation in the distinguished name. Rejected. After Windows splits the arguments, those characters are data. Valid names such as `CN=O'Brien\, John` must launch.
- Connect to the host written in the ADsPath. Rejected. That host is untrusted input.
- An in-process COM menu. Rejected for Phase 2. It would load code in `mmc.exe`.

## Consequences

The parser accepts two arguments, an `LDAP://` URL, and the class `user`. It parses the URL into a provider, an optional server, and a distinguished name. The directory connection uses a domain controller discovered by Windows, or the supplied host only when it matches that discovery. The user view is shown only when a second read by binary `objectGUID` returns the same GUID.

## Security Impact

The ADsPath is not executed. Negotiate authentication is not sent to an arbitrary host. Signing and sealing are required where the API supports them. A failure to establish that security is a provider failure, not a retry with Basic or anonymous authentication. Registration changes one locale in an approved lab and is not run by the application.

## Revisit Conditions

Revisit this ADR when a disposable domain lab captures the argument array for `C:\Program Files\ADEXO\ADEXO.exe`, including a path that contains spaces. If that array is not the ADsPath followed by the class, stop and correct the parser before any further launch work.
