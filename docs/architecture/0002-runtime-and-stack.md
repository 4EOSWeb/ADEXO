# ADR 0002: Runtime and stack

Status: accepted. Phase 1 verified the target framework on November 17, 2026.

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

## Phase 1 verification

Source: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), last updated November 10, 2026.

- Selected LTS: .NET 10. Support is active. End of support is November 14, 2028.
- .NET 8 and .NET 9 are out of support. .NET 11 is STS and is not the LTS choice.
- Portable libraries and tests target `net10.0`.
- `ADEXO.Desktop` targets `net10.0-windows` because it is the WPF shell. Other projects do not use the `-windows` suffix.
- `global.json` asks for SDK `10.0.100` and rolls forward inside the 10.0 feature band.

Packages added in Phase 1, from nuget.org:

| Package | Version | License | Reason |
| --- | --- | --- | --- |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.15 | MIT | Structured logging. The 10.0 line matches this LTS. |
| `xunit` | 2.9.3 | Apache-2.0 | Unit tests. |
| `xunit.runner.visualstudio` | 4.0.1 | Apache-2.0 | Test discovery. |
| `Microsoft.NET.Test.Sdk` | 18.12.0 | MIT | Test host. |

`Microsoft.Graph`, `Microsoft.Identity.Client`, `ExchangeOnlineManagement`, and WiX are still not referenced.

## Host limit

The Linux host can build and test the portable projects. It does not prove that the WPF shell or ADUC integration runs on Windows.
