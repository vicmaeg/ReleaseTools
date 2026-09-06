# Calendar Versioning (CalVer)

## Overview

`ReleaseTools.CalVer` is a .NET tool that calculates a calendar version from Git commit history. It prints the version to stdout, making it suitable for local scripts and release pipelines.

Calendar Versioning uses a date as the primary version component. It's ideal for projects with time-sensitive releases or constantly-changing scope.

The `calver` tool builds the version from a configurable token format. The date comes from the effective **HEAD commit date in UTC** (not the current date), which makes versions reproducible. With `--folder`, effective HEAD means the latest commit touching that tracked folder.

## Installation

```bash
dotnet tool install --global ReleaseTools.CalVer
```

The installed command is `calver`. To upgrade an existing installation:

```bash
dotnet tool update --global ReleaseTools.CalVer
```

## Requirements

- The .NET 10 SDK
- Git available on `PATH`
- A Git repository with at least one commit
- Full Git history; shallow clones can undercount commits in the selected date window

In GitHub Actions, configure `actions/checkout` with `fetch-depth: 0` so the full commit history is available.

## Tokens

| Token | Renders | Example (Jan 5, 2005) |
|-------|---------|------------------------|
| `YYYY` | Full year | `2005` |
| `YY` | Unpadded short year | `5` |
| `0Y` | Zero-padded short year | `05` |
| `MM` | Unpadded month | `1` |
| `0M` | Zero-padded month | `01` |
| `WW` | Unpadded ISO week | `1` |
| `0W` | Zero-padded ISO week | `01` |
| `DD` | Unpadded day | `5` |
| `0D` | Zero-padded day | `05` |
| `PATCH` | Commits in the date window | `3` |

Tokens may be separated by `.` or concatenated (`YY.0M0D.PATCH` → `25.0223.1` for Feb 23, 2025). Formats are case-sensitive.

### Validation Rules

- Exactly one year token is required.
- Month and week tokens are mutually exclusive.
- Day tokens require a month token.
- Tokens must be ordered: Year → Month/Week → Day → PATCH.
- No duplicate token categories (e.g. two year tokens).

## PATCH Semantics

`PATCH` is the **number of commits within the current date window** — the finest date unit in the format:

- `YYYY.MM.PATCH` (default) → commits in the HEAD commit's month
- `YYYY.0M.PATCH` → commits in the HEAD commit's month (zero-padded)
- `YYYY.0M.0D.PATCH` → commits on the HEAD commit's day
- `YYYY.0W.PATCH` → commits in the HEAD commit's ISO week (weeks start Monday)
- `YYYY.PATCH` → commits in the HEAD commit's year

The count resets naturally as the window rolls over, no tags involved. If the format has no `PATCH` token, no count is appended (`YYYY.0M` → `2025.02`).

## Usage

```bash
calver [options]
```

### Options

| Option | Description |
|--------|-------------|
| `--format <FORMAT>` | Token format (default: `YYYY.MM.PATCH`) |
| `-f, --folder <PATH>` | Use a tracked repository-relative folder's history and effective HEAD |
| `-p, --prerelease <ID>` | Append prerelease identifier (e.g. `alpha`, `rc`) |
| `-b, --buildmetadata` | Append short commit SHA as build metadata |
| `-o, --output <text\|json>` | Output format (default: `text`) |

## Examples

```bash
# Monthly cadence, 3 commits this month
calver
# 2025.2.3

# Ubuntu-style
calver --format YY.0M.PATCH
# 25.02.3

# Daily
calver --format YYYY.0M.0D.PATCH
# 2025.02.23.1

# Pure date, no patch
calver --format YYYY.0M
# 2025.02

# Prerelease + build metadata
calver -p rc -b
# 2025.2.3-rc+a1b2c3d
```

## When to Use CalVer

- Large systems and frameworks (like Ubuntu, Twisted)
- Projects with constantly-changing scope
- Time-sensitive releases (security updates, compliance changes)
- Projects where knowing *when* something was released matters more than API compatibility
