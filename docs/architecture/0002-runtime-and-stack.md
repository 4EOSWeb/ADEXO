# ADR 0002: Runtime and stack

Status: accepted as a rule. The target framework is not committed until Phase 1.

## Runtime rule

The companion targets the current supported Windows-capable .NET LTS release. Phase 1 verifies that release against the [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and records the chosen `TargetFramework` before any project file is committed.

As of November 17, 2026, that release is expected to be `net10.0-windows`:

- .NET 8 and .NET 9 reached end of support on November 10, 2026.
- .NET 10 is the active LTS release, supported through November 14, 2028. The latest patch listed by Microsoft on that date is 10.0.15.
- .NET 11 is an STS release and is not the LTS choice.

The UI is WPF with MVVM. Business logic stays out of code-behind. WinUI 3 is not the v1 UI. .NET Framework 4.8 is reserved for a future native COM boundary. It is not the cloud UI runtime.

The development host that produced this note is Ubuntu 24.04 with the .NET SDK 10.0.115 and PowerShell 7.7.0. That host can compile portable libraries. It cannot run ADUC, WPF, WiX, or Windows PowerShell 5.1. A Windows 11 or Windows Server management workstation is required before a UI or installer result is treated as demonstrated.

## Processes

| Process | Runtime | Responsibility |
| --- | --- | --- |
| `mmc.exe` / ADUC | Windows | Shows the static menu and starts `ADEXO.exe`. Loads no ADEXO code. |
| `ADEXO.exe` | Windows-capable .NET LTS, WPF | UI, providers, authentication, audit. |
| Future COM stub | Native x64, or .NET Framework 4.8 only at the COM boundary | Not in v1. If added, it only signals `ADEXO.exe`. |

## Packages confirmed, not yet referenced

These versions were read from their official feeds on November 17, 2026. Phase 1 rechecks them before a project file pins them.

| Package or module | Version | Feed | Role |
| --- | --- | --- | --- |
| `Microsoft.Graph` | 6.10.0 | NuGet | Graph SDK. Targets `net8.0` and `netstandard2.0`, so it can be referenced from the current LTS. |
| `Microsoft.Identity.Client` | 4.92.0 | NuGet | MSAL.NET for delegated and certificate authentication. |
| `ExchangeOnlineManagement` | 3.11.1 | PowerShell Gallery | REST-backed Exchange Online cmdlets. From 3.10.0, PowerShell 7.6 is the minimum. Windows PowerShell 5.1 remains supported. |

WiX Toolset v4, xUnit, FluentAssertions, and NSubstitute are chosen only in the phase that adds them, after the version and license are read from the official feed. `Microsoft.Extensions.*` packages are pinned to the same servicing band as the chosen LTS after that check.

No package is added because a blog named it. Each addition records the license, the reason, and the framework it supports.

## Logging and composition

The companion uses dependency injection, `ILogger` structured logging, cancellation tokens, and nullable reference types. Analyzers are enabled in Phase 1. Secrets, tokens, and passwords are not log properties.

## Host limit

Nothing in this document has been built or tested as a product. The SDK on the Linux host shows that a current LTS compiler is available. It does not show that ADEXO runs on Windows.
