# Semantic Versioning (SemVer)

## Overview

Semantic Versioning is a versioning scheme that uses a three-part version number: `MAJOR.MINOR.PATCH`.

## Specification

Given a version number `MAJOR.MINOR.PATCH`, increment the:

1. **MAJOR** version when you make incompatible API changes
2. **MINOR** version when you add functionality in a backward compatible manner
3. **PATCH** version when you make backward compatible bug fixes

Additional labels for pre-release and build metadata are available as extensions to the `MAJOR.MINOR.PATCH` format.

## Usage with ReleaseTools

### Basic Usage

```bash
# Calculate next version
ver next --schema "{MAJOR}.{MINOR}.{PATCH}"

# Create and push tag
ver tag --schema "{MAJOR}.{MINOR}.{PATCH}" --push
```

### With Prefix (Monorepo)

```bash
ver next --schema "{MAJOR}.{MINOR}.{PATCH}" --prefix "api-"
# Output: api-1.2.0
```

### With Folder Filter (Monorepo)

```bash
ver next --schema "{MAJOR}.{MINOR}.{PATCH}" --folder "./src/MyApp"
```

### Pre-release Versions

```bash
# Alpha
ver next --schema "{MAJOR}.{MINOR}.{PATCH}-alpha.{NUM_COMMITS}"
# Output: 1.2.0-alpha.5

# Beta
ver next --schema "{MAJOR}.{MINOR}.{PATCH}-beta.{NUM_COMMITS}"
# Output: 1.2.0-beta.3

# Release Candidate
ver next --schema "{MAJOR}.{MINOR}.{PATCH}-rc.{NUM_COMMITS}"
# Output: 1.2.0-rc.2
```

### Build Metadata

```bash
# With short SHA
ver next --schema "{MAJOR}.{MINOR}.{PATCH}+{SHORTSHA}"
# Output: 1.2.0+a1b2c3d

# With full SHA
ver next --schema "{MAJOR}.{MINOR}.{PATCH}+{SHA}"
# Output: 1.2.0+a1b2c3d4e5f6g7h8i9j0
```

### Docker-compatible Tags

Docker doesn't support `+` for build metadata. Use `-` instead:

```bash
ver next --schema "{MAJOR}.{MINOR}.{PATCH}-{SHORTSHA}"
# Output: 1.2.0-a1b2c3d
```

## Conventional Commits

ReleaseTools uses [Conventional Commits](https://www.conventionalcommits.org/) to determine version increments.

### Commit Types

| Type | Description | Increment |
|------|-------------|-----------|
| `feat` | New feature | Minor |
| `fix` | Bug fix | Patch |
| `perf` | Performance improvement | Patch |
| `revert` | Revert previous commit | Patch |
| `docs` | Documentation changes | None |
| `style` | Code style changes | None |
| `ref refactoring | None |
| `test` | Test changesactor` | Code | None |
| `chore` | Maintenance tasks | None |
| `build` | Build system changes | None |
| `ci` | CI/CD changes | None |

### Breaking Changes

Breaking changes increment the MAJOR version:

```bash
# Using ! in commit message
feat!: remove deprecated API

# Or using BREAKING CHANGE in body
feat: add new API

BREAKING CHANGE: This removes the old API
```

## Initial Version

When no tags exist in the repository, ReleaseTools starts at `0.1.0`.

## Examples

### Example 1: Adding Features

```bash
# Repository has tag 1.0.0
# New commits:
#   - feat: add user authentication
#   - feat: add user profile

ver next --schema "{MAJOR}.{MINOR}.{PATCH}"
# Output: 1.1.0
```

### Example 2: Bug Fixes

```bash
# Repository has tag 1.0.0
# New commits:
#   - fix: resolve login issue

ver next --schema "{MAJOR}.{MINOR}.{PATCH}"
# Output: 1.0.1
```

### Example 3: Breaking Changes

```bash
# Repository has tag 1.0.0
# New commits:
#   - feat!: redesign API

ver next --schema "{MAJOR}.{MINOR}.{PATCH}"
# Output: 2.0.0
```

### Example 4: Multiple Changes (Only Increments Once)

```bash
# Repository has tag 1.0.0
# New commits:
#   - feat: add feature A
#   - feat: add feature B
#   - fix: fix bug

ver next --schema "{MAJOR}.{MINOR}.{PATCH}"
# Output: 1.1.0 (not 1.2.1 - only one increment)
```
