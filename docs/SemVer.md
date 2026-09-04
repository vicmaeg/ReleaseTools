# Semantic Versioning (SemVer)

## Overview

Semantic Versioning uses a three-part version number: `MAJOR.MINOR.PATCH`.

1. **MAJOR** — incompatible API changes
2. **MINOR** — backward-compatible functionality
3. **PATCH** — backward-compatible bug fixes

The `semver` tool implements this with a fixed schema `{MAJOR}.{MINOR}.{PATCH}`, deriving the increment from [Conventional Commits](https://www.conventionalcommits.org/) since the latest stable tag.

## Usage

```bash
dotnet run --file src/semver.cs -- [options]
```

### Options

| Option | Description |
|--------|-------------|
| `--prefix <PREFIX>` | Literal tag prefix for monorepo scenarios (e.g. `api-`) |
| `-f, --folder <PATH>` | Use a tracked repository-relative folder's history |
| `-p, --prerelease <ID>` | Append identifier and matching commit count (e.g. `alpha.3`) |
| `-b, --buildmetadata` | Append short commit SHA as build metadata |
| `-o, --output <text\|json>` | Output format (default: `text`) |

## Commit Types

| Type | Increment |
|------|-----------|
| `feat` | Minor |
| `fix`, `perf`, `revert` | Patch |
| `docs`, `style`, `refactor`, `test`, `chore`, `build`, `ci`, anything else | None |

### Breaking Changes

Append `!` to the type to signal a breaking change (increments MAJOR):

```
feat!: remove deprecated API
fix(api)!: change response format
```

Full commit messages are analyzed. Both `BREAKING CHANGE:` and `BREAKING-CHANGE:` footers increment MAJOR.

## Behavior Details

- **No tags** → initial version `0.1.0`.
- **Tag selection**: only strict SemVer tags with the exact requested prefix are considered. Prerelease and unreachable tags are skipped; build metadata (`+...`) remains stable.
- **Base version**: the highest reachable stable SemVer wins, not the alphabetically first or nearest tag.
- **Increment is applied once**: the highest increment among all commits since the tag wins (e.g. three `feat` commits → one minor bump).
- **Folder filter**: the folder must be a literal tracked path. Its latest commit supplies the effective HEAD date/SHA, and commits outside it are ignored.
- **Prerelease counter**: `-p alpha` produces `alpha.N`, where `N` is the filtered commit count since the stable base (or the full matching history before the first tag). The prerelease is appended whenever requested, even without version-relevant commits (e.g. docs-only changes after `1.0.0` → `1.0.0-alpha.1`).

## Examples

```bash
# Tag 1.0.0, new commits: feat: add user authentication
dotnet run --file src/semver.cs
# 1.1.0

# Tag 1.0.0, new commits: fix: resolve login issue
dotnet run --file src/semver.cs
# 1.0.1

# Tag 1.0.0, new commits: feat!: redesign API
dotnet run --file src/semver.cs
# 2.0.0

# Prerelease and build metadata
dotnet run --file src/semver.cs -- -p beta -b
# 1.1.0-beta.1+a1b2c3d

# Monorepo: tags like api-1.0.0, web-2.3.0
dotnet run --file src/semver.cs -- --prefix api-
# 1.1.0 (based on api-1.0.0, ignoring web-* tags)

# JSON output for pipelines
dotnet run --file src/semver.cs -- -o json
# { "version": "1.1.0", "fullVersion": "1.1.0", "baseTag": "1.0.0", ... }
```
