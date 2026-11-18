# ADR 0001: ADUC integration

Status: accepted for ADEXO v1

ADEXO lets an administrator select an object in Active Directory Users and Computers and manage the matching on-premises and Microsoft 365 state. v1 does that by launching `ADEXO.exe` from a static ADUC context menu. No ADEXO code loads inside `mmc.exe`.

## Decision

Use a static `adminContextMenu` command. The command is the full path of the installed executable, expected at `C:\Program Files\ADEXO\ADEXO.exe`. ADUC starts that executable with `ShellExecute` and appends the selected object's ADsPath and class.

```text
ADUC
  |
  | Administer Microsoft 365
  v
Static adminContextMenu
  |
  | ADsPath + object class
  v
ADEXO.exe
  |
  +-- Active Directory provider
  +-- Microsoft Graph provider
  +-- Exchange Online provider
  +-- Entra Connect provider
```

`ADEXO.exe` resolves `objectGUID` from the ADsPath before any later read or write. The distinguished name is display data. It is not the object's identity.

One companion process runs per signed-in Windows user. A later selection is delivered on a named pipe whose ACL allows only that user's SID. The message carries the ADsPath, class, and a nonce. It does not carry tokens. There is no Windows service.

## What Microsoft documents

ADUC is not a managed .NET plugin host. Microsoft documents two extension mechanisms:

- A static command, registered as `<order>,<menu text>,<command>` on `adminContextMenu` or `shellContextMenu`. The command cannot include extra parameters. When the command is an application, the administrative snap-in passes the ADsPath and class. Static items in those snap-ins do not support multiple selection. Source: [Registering a Static Context Menu Item](https://learn.microsoft.com/en-us/windows/win32/ad/registering-a-static-context-menu-item).
- An in-process COM server. A context-menu extension implements `IShellExtInit` and `IContextMenu`, is registered as `<order>,<clsid>`, and reads `CFSTR_DSOBJECTNAMES` and `CFSTR_DS_DISPLAY_SPEC_OPTIONS`. Source: [Implementing the Context Menu COM Object](https://learn.microsoft.com/en-us/windows/win32/ad/implementing-the-context-menu-com-object). A property page implements `IShellExtInit` and `IShellPropSheetExt` and is registered on `adminPropertyPages` as `<order>,<clsid>[,data]`. Source: [Implementing the Property Page COM Object](https://learn.microsoft.com/en-us/windows/win32/ad/implementing-the-property-page-com-object) and [Registering the Property Page COM Object](https://learn.microsoft.com/en-us/windows/win32/ad/registering-the-property-page-com-object-in-a-display-specifier).

Display specifiers are objects in the configuration partition, one per class and locale. The US English user specifier is `CN=user-Display,CN=409,CN=DisplaySpecifiers,<configurationNamingContext>`. Administrative snap-ins read them when the snap-in loads and cache them until `mmc.exe` reloads. Source: [Display Specifiers](https://learn.microsoft.com/en-us/windows/win32/ad/display-specifiers).

`shellContextMenu` and `shellPropertyPages` are not used. Since Windows Server 2003 the Windows shell has not displayed Active Directory objects.

On 64-bit Windows, ADUC runs in 64-bit `mmc.exe`. An in-proc DLL has to be x64 and registered at `HKLM\SOFTWARE\Classes\CLSID\{clsid}\InProcServer32`. A registration under `Wow6432Node` is invisible to that process.

## Why the static command is v1

An Exchange connection failure, a Graph SDK crash, a WPF exception, a PowerShell module fault, or an ADEXO update stays in `ADEXO.exe`. ADUC continues. If the executable is missing, `ShellExecute` fails for that click.

Loading .NET Framework, modern .NET, PowerShell, Exchange Online, or WebView2 inside `mmc.exe` would put those failures in the ADUC process. Loading both the .NET Framework CLR and the modern .NET runtime in one process is not a supportable design.

These alternatives are rejected for v1:

- An in-proc property page or COM context menu.
- A separate MMC snap-in that recreates the ADUC tree.
- A SYSTEM service that performs cloud calls on the administrator's behalf.

COM property pages and a native multi-select menu can be reconsidered later. A later in-proc DLL, if one is added, is native, x64, and only starts or signals `ADEXO.exe`. Tabs inside ADUC are worth that risk only after v1 is in use.

## Workstation install and forest registration

Copying files onto a workstation and publishing the menu are different operations. Display specifiers are forest configuration. The MSI must not write them.

Workstation install:

- `ADEXO.msi` installs `C:\Program Files\ADEXO\ADEXO.exe` and the local runtime prerequisites.
- It checks x64 Windows, the supported runtime, and RSAT ADUC.
- It does not change the schema, display specifiers, or AD ACLs.

Forest registration, run only by an administrator who can write the configuration partition:

- `Backup-ADEXODisplaySpecifiers.ps1` saves the current `adminContextMenu` values.
- `Register-ADEXOForest.ps1` appends one value with `ADS_PROPERTY_APPEND`. It does not replace the attribute. The first locale is `CN=409`. Additional locales are explicit.
- `Test-ADEXOForestRegistration.ps1` reports whether the ADEXO value is present, whether the command path matches the installed executable, and which locales were checked.
- `Unregister-ADEXOForest.ps1` removes only the ADEXO value and can restore the backup.

Classes for v1 are `user`, `group`, `contact`, `computer`, and `organizationalUnit`. Each class has its own `*-Display` object.

The menu is forest-wide. Every ADUC console in the forest shows it, including workstations that do not have `ADEXO.exe`. On those machines the click fails in `ShellExecute` and ADUC stays up. The registration script states that before it writes.

Registration does not alter the schema. Uninstall of the MSI does not unregister the forest. An administrator runs `Unregister-ADEXOForest.ps1` as its own step.

## Crash and failure behavior

| Failure | Where it happens | Effect on ADUC |
| --- | --- | --- |
| Executable missing | `ShellExecute` | Click fails. ADUC stays up. |
| Companion crash, hang, or leak | `ADEXO.exe` | That process ends or is restarted. ADUC stays up. |
| Named pipe unavailable | Companion | The new launch starts a fresh process. ADUC is not waiting on the pipe. |
| Graph, Exchange, or PowerShell fault | Companion | Shown in ADEXO. ADUC is unaffected. |

The static command path is fixed at registration. An upgrade that moves the executable requires a new forest registration. The test script detects a stale path.

## Verification status

This decision is based on the Microsoft Learn articles linked above. It has not been executed against a domain. This repository's current host is Linux and cannot load ADUC. A Windows lab has to confirm the menu text, the ADsPath argument, locale coverage, and uninstall restoration before the integration is called complete.
