# ADR-0008 — Build on .NET 9 now, upgrade to .NET 10 LTS in a dedicated change

Status: Accepted (risk acknowledged) · 2026-10-29

## Context

The plan targeted .NET 10 LTS. Verification of the machine showed only SDK `9.0.200` installed
(`C:\Program Files\dotnet\sdk`), with ASP.NET Core runtimes 9.0.2 and 8.0.13. The .NET 10 SDK
installation was not present at the start of Phase 1.

.NET 9 is a Standard Term Support release whose support window has ended. Continuing on it
indefinitely is a security-maintenance liability.

## Decision

Proceed with `net9.0` for Phases 1–7, with:
- `global.json` pinning the SDK major/minor with `rollForward: latestFeature`.
- A single `Directory.Build.props` holding the target framework and package versions, so the
  upgrade is a contained diff.
- An upgrade to `net10.0` scheduled before Phase 8 (deployment), when the SDK is installed and
  verified with `dotnet --list-sdks`.

## Alternatives

- **Block Phase 1 until .NET 10 is installed** — safest, but delays work for a contained change.
- **Target `net8.0` LTS** — supported longer than 9, but its LTS window also ends in 2026 and it
  would mean downgrading packages we are starting fresh with.

## Consequences

- Phase 1–7 run on an unsupported runtime. This must not reach production.
- The upgrade touches the TFM, package versions, and possibly minimal-API/EF API changes. Budget
  half a day in Phase 7, with the full test suite as the gate.
- If the SDK is installed earlier, the upgrade moves earlier.
