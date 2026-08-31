# Calendar Versioning (CalVer)

## Overview

Calendar Versioning uses the release date as the primary version component. It's ideal for projects with time-sensitive releases or constantly-changing scope.

The `calver` tool builds the version from a configurable token format. The date comes from the effective **HEAD commit date in UTC** (not the current date), which makes versions reproducible. With `--folder`, effective HEAD means the latest commit touching that tracked folder.

## Tokens

| Token | Renders | Example (Feb 23, 2025) |
|-------|---------|------------------------|
| `YYYY` | Full year | `2025` |
| `YY` | Short year | `25` |
| `0Y` | Zero-padded short year | `25` |
| `MM` | Month | `2` |
| `0M` | Zero-padded month | `02` |
| `WW` | ISO week | `8` |
| `0W` | Zero-padded ISO week | `08` |
| `DD` | Day | `23` |
| `0D` | Zero-padded day | `23` |
| `PATCH` | Commits in the date window | `3` |

Tokens may be separated by `.` or concatenated (`YY.0M0D.PATCH` → `25.0223.1`).

### Validation Rules

- Exactly one year token is required.
- Month and week tokens are mutually exclusive.
- Day tokens require a month token.
- Tokens must be ordered: Year → Month/Week → Day → PATCH.
- No duplicate token categories (e.g. two year tokens).

## PATCH Semantics

`PATCH` is the **number of commits within the current date window** — the finest date unit in the format:

- `YYYY.0M.PATCH` → commits in the HEAD commit's month
- `YYYY.0M.0D.PATCH` → commits on the HEAD commit's day
- `YYYY.0W.PATCH` → commits in the HEAD commit's ISO week (weeks start Monday)
- `YYYY.PATCH` → commits in the HEAD commit's year

The count resets naturally as the window rolls over, no tags involved. If the format has no `PATCH` token, no count is appended (`YYYY.0M` → `2025.02`).

## Usage

```bash
dotnet run --file src/calver.cs -- [options]
```

### Options

| Option | Description |
|--------|-------------|
| `-f, --format <FORMAT>` | Token format (default: `YYYY.0M.PATCH`) |
| `--folder <PATH>` | Use a tracked repository-relative folder's history and effective HEAD |
| `-p, --prerelease <ID>` | Append prerelease identifier (e.g. `alpha`, `rc`) |
| `-b, --buildmetadata` | Append short commit SHA as build metadata |
| `-o, --output <text\|json>` | Output format (default: `text`) |

## Examples

```bash
# Monthly cadence, 3 commits this month
dotnet run --file src/calver.cs
# 2025.02.3

# Ubuntu-style
dotnet run --file src/calver.cs -- -f YY.0M.PATCH
# 25.02.3

# Daily
dotnet run --file src/calver.cs -- -f YYYY.0M.0D.PATCH
# 2025.02.23.1

# Pure date, no patch
dotnet run --file src/calver.cs -- -f YYYY.0M
# 2025.02

# Prerelease + build metadata
dotnet run --file src/calver.cs -- -p rc -b
# 2025.02.3-rc+a1b2c3d
```

## When to Use CalVer

- Large systems and frameworks (like Ubuntu, Twisted)
- Projects with constantly-changing scope
- Time-sensitive releases (security updates, compliance changes)
- Projects where knowing *when* something was released matters more than API compatibility
