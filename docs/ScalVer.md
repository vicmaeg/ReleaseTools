# Scalable Calendar Versioning (ScalVer)

## Overview

ScalVer combines the best of Semantic Versioning (SemVer) and Calendar Versioning (CalVer). It uses:
- **MAJOR** - For breaking changes (like SemVer)
- **DATE** - Calendar-based component (like CalVer)
- **PATCH** - To avoid clashing within the same date window

## Specification

Format: `<MAJOR>.<DATE>.<PATCH>`

The DATE component may lengthen over time within a MAJOR line:
- `YYYY` → `YYYYMM` → `YYYYMMDD`

### Key Rules

1. **MAJOR** increments for:
   - Breaking changes
   - When the DATE segment would shrink

2. **DATE** may:
   - Stay the same width
   - Expand (YYYY → YYYMM → YYYYMMDD)
   - Never shrink within the same MAJOR

3. **PATCH** increments:
   - For backward-compatible releases within the same DATE window

## Usage with ReleaseTools

### Basic Usage

```bash
# Calculate next version
ver next --schema "{MAJOR}.{YYYY}.{PATCH}"

# Create and push tag
ver tag --schema "{MAJOR}.{YYYY}.{PATCH}" --push
```

### Common Schema Formats

#### Yearly

```bash
ver next --schema "{MAJOR}.{YYYY}.{PATCH}"
# Output: 1.2025.0
```

#### Monthly

```bash
ver next --schema "{MAJOR}.{YYYY}{0M}.{PATCH}"
# Output: 1.202502.0 (February 2025)
```

#### Daily

```bash
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 1.20250223.0 (February 23, 2025)
```

#### Short Year

```bash
ver next --schema "{MAJOR}.{0Y}{0M}.{PATCH}"
# Output: 1.2502.0
```

### With Prefix (Monorepo)

```bash
ver next --schema "{MAJOR}.{YYYY}.{PATCH}" --prefix "api-"
# Output: api-1.2025.0
```

## Date Source

Like CalVer, ReleaseTools uses the **commit date** (not current date) for reproducibility.

## Breaking Changes

Breaking changes in ScalVer work like SemVer - they increment the MAJOR version:

```bash
# Repository has tag 1.2025.0
# New commits include breaking change:
#   - feat!: remove deprecated API

ver next --schema "{MAJOR}.{YYYY}.{PATCH}"
# Output: 2.2025.0
```

## Date-Only-Grows (DOG) Rule

Within any single MAJOR line, DATE can expand but never shrink:

### Allowed Transitions

| From | To | Valid? |
|------|-----|--------|
| 1.2025.0 | 1.202502.0 | ✓ (YYYY → YYYMM) |
| 1.202502.0 | 1.20250223.0 | ✓ (YYYYMM → YYYYMMDD) |
| 1.20250223.0 | 1.20250223.1 | ✓ (same date, patch++) |
| 1.202502.0 | 1.202502.1 | ✓ (same date, patch++) |

### Required MAJOR Bump

| From | To | Correct |
|------|-----|---------|
| 1.20250223.0 | 1.2026.0 | ✗ - Need: 2.2026.0 |
| 1.2025.3 | 1.202502.0 | ✗ - Need: 2.202502.0 |

## PATCH Reset

PATCH resets when:
- DATE changes (new month/day)
- MAJOR increments

```bash
# Current: 1.202502.3
# Next commit in March 2025
ver next --schema "{MAJOR}.{YYYY}{0M}.{PATCH}"
# Output: 1.202503.0 (PATCH reset)
```

## Clashing Prevention

When multiple releases occur within the same DATE window:

```bash
# Current: 1.20250223.0
# Another release same day
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 1.20250223.1
```

## Initial Version

When no tags exist:

```bash
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 0.20250223.0 (starts at MAJOR=0)
```

## Pre-release Versions

```bash
ver next --schema "{MAJOR}.{YYYY}.{PATCH}-alpha.{NUM_COMMITS}"
# Output: 1.2025.0-alpha.5

ver next --schema "{MAJOR}.{YYYY}{0M}.{PATCH}-beta.{NUM_COMMITS}"
# Output: 1.202502.0-beta.3
```

## SemVer Compatibility

Every ScalVer tag is syntactically valid SemVer, so existing tools work unchanged:

```
1.2025.0 < 1.202502.0 < 1.20250223.0 < 2.2025.0
```

This means:
- Package managers (npm, NuGet, Cargo, etc.) work correctly
- CI/CD tooling works without modification
- Version comparison works as expected

## When to Use ScalVer

ScalVer is ideal for:

- **Projects needing time-based clarity** (when was this released?)
- **Projects needing SemVer compatibility** (does this break my API?)
- **Projects with varying release cadence** (yearly → monthly → daily)
- **Projects with both stable and rapidly-changing components**

## Examples

### Example 1: Initial Release

```bash
# Fresh repository
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 0.20250223.0
```

### Example 2: Breaking Change

```bash
# Current: 1.202502.5
# New commits include breaking change
ver next --schema "{MAJOR}.{YYYY}{0M}.{PATCH}"
# Output: 2.202502.0
```

### Example 3: Expanding Release Cadence

```bash
# Current: 1.2025.0 (yearly)
# Next commit in February
ver next --schema "{MAJOR}.{YYYY}{0M}.{PATCH}"
# Output: 1.202502.0 (expanded)

# Next commit in late February
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 1.20250223.0 (expanded again)
```

### Example 4: Same Date Window

```bash
# Current: 1.20250215.0
# Another release same day
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 1.20250215.1 (patch incremented)
```

### Example 5: Comparing Versions

```bash
# All these compare correctly as SemVer:
1.2025.0 < 1.202502.0 < 1.20250223.0 < 2.2025.0
```
