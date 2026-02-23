# Calendar Versioning (CalVer)

## Overview

Calendar Versioning is a versioning scheme that uses the release date as the primary version component. It's ideal for projects with time-sensitive releases or large/constantly-changing scope.

## Specification

The version format includes date components and an optional patch number to prevent clashing:

- **YYYY** - Full year (2006, 2016, 2106)
- **YY** - Short year (6, 16, 106)
- **0Y** - Zero-padded year (06, 16, 106)
- **MM** - Short month (1, 2, ..., 12)
- **0M** - Zero-padded month (01, 02, ..., 12)
- **WW** - Short week (1, 2, ..., 52)
- **0W** - Zero-padded week (01, 02, ..., 52)
- **DD** - Short day (1, 2, ..., 31)
- **0D** - Zero-padded day (01, 02, ..., 31)
- **PATCH** - Incremental counter to avoid clashing

## Usage with ReleaseTools

### Basic Usage

```bash
# Calculate next version (year.month.patch)
ver next --schema "{YYYY}.{0M}.{PATCH}"

# Create and push tag
ver tag --schema "{YYYY}.{0M}.{PATCH}" --push
```

### Common Schema Formats

#### Ubuntu-style (YY.0M.MICRO)

```bash
ver next --schema "{YY}.{0M}.{PATCH}"
# Output: 25.02.0 (February 2025)
```

#### Full Year (YYYY.0M.MICRO)

```bash
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0
```

#### Daily (YYYY.0M.0D)

```bash
ver next --schema "{YYYY}.{0M}{0D}.{PATCH}"
# Output: 2025.0223.0 (February 23, 2025)
```

#### Ultra-detailed (YYYY.MM.DD)

```bash
ver next --schema "{YYYY}.{MM}.{DD}"
# Output: 2025.2.23 (no patch, purely date-based)
```

### With Prefix (Monorepo)

```bash
ver next --schema "{YYYY}.{0M}.{PATCH}" --prefix "api-"
# Output: api-2025.02.0
```

## Date Source

ReleaseTools uses the **commit date** (not current date) to determine the version. This provides:
- **Reproducibility**: Same commit always produces the same version
- **Traceability**: Easy to trace version back to specific commit
- **Accuracy**: Reflects when changes were actually made

## Clashing Prevention

When multiple releases occur within the same date window (same day/month/year), the PATCH number is incremented to prevent clashing:

### Example: Same Month

```bash
# First release in February 2025
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0

# Second release in same month
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.1

# Third release in same month
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.2
```

### Example: Same Day

```bash
# Schema: YYYY.0M.0D.PATCH
ver next --schema "{YYYY}.{0M}{0D}.{PATCH}"
# First: 2025.0223.0
# Second: 2025.0223.1
# Third: 2025.0223.2
```

## Date Window Changes

When the date window changes (e.g., new month), the PATCH resets to 0:

```bash
# Current tag: 2025.02.5
# Next commit is in March 2025
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.03.0 (PATCH reset)
```

## Pre-release Versions

```bash
ver next --schema "{YYYY}.{0M}.{PATCH}-alpha.{NUM_COMMITS}"
# Output: 2025.02.0-alpha.5

ver next --schema "{YYYY}.{0M}{0D}.{PATCH}-rc.{NUM_COMMITS}"
# Output: 2025.0223.0-rc.2
```

## Initial Version

When no tags exist, ReleaseTools uses the commit date with PATCH=0:

```bash
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0 (based on commit date)
```

## When to Use CalVer

CalVer is ideal for:

- **Large systems and frameworks** (like Ubuntu, Twisted)
- **Projects with constantly-changing scope**
- **Time-sensitive releases** (security updates, compliance changes)
- **Projects where knowing when something was released matters more than API compatibility**

## Comparison with Other Schemes

| Project | Schema | Example |
|---------|--------|---------|
| Ubuntu | YY.0M.MICRO | 24.04 |
| Twisted | YY.MM.MICRO | 24.01 |
| youtube-dl | YYYY.0M.0D | 2025.02.23 |
| pip | YY.MINOR.MICRO | 25.01 |
| certifi | YYYY.MM.DD | 2025.02.23 |

## Examples

### Example 1: First Release

```bash
# Fresh repository with commits
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0 (based on commit date)
```

### Example 2: Monthly Releases

```bash
# Current tag: 2025.01.3
# Next commit in same month
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.01.4

# Next commit in February
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0
```

### Example 3: Daily Releases

```bash
# Schema with day: YYYY.0M.0D.PATCH
# Multiple releases same day
ver next --schema "{YYYY}.{0M}{0D}.{PATCH}"
# First: 2025.0223.0
# Second: 2025.0223.1
# Third: 2025.0223.2
```
