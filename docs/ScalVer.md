# Scalable Calendar Versioning (ScalVer)

## Overview

ScalVer combines Semantic and Calendar Versioning:

```
{MAJOR}.{DATE}.{PATCH}
```

- **MAJOR** — reserved for breaking changes, bumped **manually** via `-m`
- **DATE** — calendar component from the effective HEAD commit date in UTC (`YYYY`, `YYYYMM` or `YYYYMMDD`)
- **PATCH** — number of commits within the current date window (like CalVer)

Every ScalVer version is syntactically valid SemVer, so package managers and version comparison work unchanged:

```
1.2025.0 < 1.202502.0 < 1.20250223.0 < 2.2025.0
```

## Usage

```bash
dotnet run --file src/scalver.cs -- -m <MAJOR> [options]
```

### Options

| Option | Description |
|--------|-------------|
| `-m, --major <N>` | Major version (**required**); bump it yourself for breaking changes |
| `-d, --date-format <FMT>` | `YYYY`, `YYYYMM` or `YYYYMMDD` (default: `YYYYMM`) |
| `--folder <PATH>` | Use a tracked repository-relative folder's history and effective HEAD |
| `-p, --prerelease <ID>` | Append prerelease identifier (e.g. `alpha`, `rc`) |
| `-b, --buildmetadata` | Append short commit SHA as build metadata |
| `-o, --output <text\|json>` | Output format (default: `text`) |

## Date Formats and PATCH Windows

| Format | Renders (Feb 23, 2025) | PATCH counts commits in |
|--------|------------------------|-------------------------|
| `YYYY` | `1.2025.3` | the year |
| `YYYYMM` | `1.202502.3` | the month |
| `YYYYMMDD` | `1.20250223.3` | the day |

The DATE segment may lengthen over time within a MAJOR line (`YYYY` → `YYYYMM` → `YYYYMMDD`) but should never shrink — shrink requires a MAJOR bump. The tool does not enforce this; it's a convention you follow by choosing `-m` and `-d` deliberately.

## Breaking Changes

There is no commit-message analysis: when you make a breaking change, bump `-m` yourself. The date window's commit count keeps PATCH meaningful without any tags.

## Examples

```bash
# Monthly cadence, 3 commits this month
dotnet run --file src/scalver.cs -- -m 1
# 1.202502.3

# Daily cadence
dotnet run --file src/scalver.cs -- -m 1 -d YYYYMMDD
# 1.20250223.1

# Breaking change: you decide
dotnet run --file src/scalver.cs -- -m 2
# 2.202502.3

# Prerelease + build metadata
dotnet run --file src/scalver.cs -- -m 1 -p rc -b
# 1.202502.3-rc+a1b2c3d
```

## When to Use ScalVer

- Projects needing time-based clarity (when was this released?)
- Projects needing SemVer compatibility (does this break my API?)
- Projects with varying release cadence (yearly → monthly → daily)
- Projects with both stable and rapidly-changing components
