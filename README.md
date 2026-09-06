# ReleaseTools

Minimal, opinionated CLI tools for versioning git repositories. Each tool prints the next version to stdout — nothing more. Tagging and releasing is left to your pipeline.

Three tools, one per versioning scheme:

| Tool | Schema | Driven by |
|------|--------|-----------|
| `semver` | `{MAJOR}.{MINOR}.{PATCH}` (fixed) | Conventional Commits since last tag |
| `calver` | Configurable date tokens (default `YYYY.MM.PATCH`) | Effective HEAD commit date + commits in date window |
| `scalver` | `{MAJOR}.{DATE}.{PATCH}` (DATE: `YYYY`, `YYYYMM` or `YYYYMMDD`) | Manual MAJOR + effective HEAD commit date |

## Requirements

- .NET SDK **10.0.300 or later** (the tools are [file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) using `#:include`)
- A Git repository with **full history and tags**. Shallow clones (GitHub Actions default `fetch-depth: 1`) will miss tags and history; use `fetch-depth: 0`.

## Installation

### As .NET tools

```bash
dotnet tool install -g ReleaseTools.SemVer
dotnet tool install -g ReleaseTools.CalVer
dotnet tool install -g ReleaseTools.ScalVer
```

The CLI commands are `semver`, `calver` and `scalver`.

## Usage

Run a tool from inside a git repository; it prints the version followed by a newline:

```bash
dotnet run --file src/semver.cs -- [options]
dotnet run --file src/calver.cs -- [options]
dotnet run --file src/scalver.cs -- [options]
```

### semver

Fixed schema `{MAJOR}.{MINOR}.{PATCH}`. Analyzes [Conventional Commits](https://www.conventionalcommits.org/) since the latest stable tag. No tags → `0.1.0`.

```bash
dotnet run --file src/semver.cs            # 1.2.0
dotnet run --file src/semver.cs -- --prefix api- -f apps/api # isolated monorepo app
```

Options:
- `--prefix <PREFIX>` — literal tag prefix; the remainder must be a complete SemVer (`api-1.0.0` needs `--prefix api-`, not `api`)
- `-f, --folder <PATH>` — use only a tracked repository-relative folder's history
- `-p, --prerelease <ID>` — append the identifier and matching commit count (`1.2.0-alpha.3`)
- `-b, --buildmetadata` — append short commit SHA (`1.2.0+a1b2c3d`)
- `-o, --output <text|json>` — output format (default: text)

### calver

Configurable format built from fixed tokens. The date comes from the effective HEAD commit in UTC; `PATCH` counts commits within that date window (e.g. 3 commits this month → `.3`).

```bash
dotnet run --file src/calver.cs                            # 2025.2.3 (default YYYY.MM.PATCH)
dotnet run --file src/calver.cs -- --format YY.0M0D.PATCH  # 25.0223.1
```

Tokens: `YYYY`, `YY`, `0Y` (year) · `MM`, `0M` (month) · `WW`, `0W` (week) · `DD`, `0D` (day) · `PATCH` (commit count). Rules: a year token is required; month and week are mutually exclusive; day requires month; tokens must be ordered Year → Month/Week → Day → PATCH.

The default `YYYY.MM.PATCH` is unpadded so the CLI version, git tag, and NuGet package version are the same string.

Options:
- `--format <FORMAT>` — token format (default: `YYYY.MM.PATCH`)
- `-f, --folder <PATH>` — use only a tracked repository-relative folder's history
- `-p, --prerelease <ID>` — append prerelease identifier
- `-b, --buildmetadata` — append short commit SHA
- `-o, --output <text|json>` — output format (default: text)

### scalver

Fixed shape `{MAJOR}.{DATE}.{PATCH}` — like CalVer, but MAJOR is reserved for breaking changes and bumped manually via `-m`. `PATCH` counts the commits within the current date window.

```bash
dotnet run --file src/scalver.cs -- -m 1                 # 1.202502.3 (default YYYYMM)
dotnet run --file src/scalver.cs -- -m 2 -d YYYYMMDD     # 2.20250223.1
```

Options:
- `-m, --major <N>` — major version (required; bump it yourself for breaking changes)
- `-d, --date-format <FMT>` — `YYYY`, `YYYYMM` or `YYYYMMDD` (default: `YYYYMM`)
- `-f, --folder <PATH>` — use only a tracked repository-relative folder's history
- `-p, --prerelease <ID>` — append prerelease identifier
- `-b, --buildmetadata` — append short commit SHA
- `-o, --output <text|json>` — output format (default: text)

SemVer `--prerelease` appends `.N` (commit count since the base) because the core version may not change every commit. CalVer and ScalVer append the identifier only; `PATCH` already unique-ifies the version.

## Project layout

```
src/
  semver.cs     # SemVer tool (scheme logic is file-private)
  calver.cs     # CalVer tool
  scalver.cs    # ScalVer tool
  shared/       # code shared by all tools, pulled in via #:include
    GitService.cs
    SchemaParser.cs
    MetadataService.cs
    VersionInfo.cs
    CalculationResult.cs
    DateGranularity.cs
    OutputFormat.cs
    OutputWriter.cs
test/           # black-box CLI tests (run the real tool binaries)
.github/workflows/ # active CI and independent package publishing workflows
docs/           # per-scheme details
```

When `--folder` is supplied, its latest commit supplies the date and build SHA as well as the filtered commit history. Missing, absolute and escaping paths fail instead of silently returning an unchanged version. SemVer tag prefixes are explicit: `v1.2.3` requires `--prefix v`; unrelated, malformed, prerelease and unreachable tags are ignored when selecting the highest stable base.

## Package version streams

The repository publishes each tool with its own versioning scheme and package-specific tag:

| Package | Version | Tag |
|---------|---------|-----|
| `ReleaseTools.SemVer` | `1.2.3` | `semver-v1.2.3` |
| `ReleaseTools.CalVer` | `2026.9.4` (`YYYY.MM.PATCH`) | `calver-v2026.9.4` |
| `ReleaseTools.ScalVer` | `1.20260831.4` | `scalver-v1.20260831.4` |

The checked-in `release.json` holds the manually controlled ScalVer major. On `main`, GitHub Actions publishes each missing package version and then creates its corresponding tag. Existing tag streams are skipped independently, so retries are safe.

## Documentation

- [Semantic Versioning (SemVer)](docs/SemVer.md)
- [Calendar Versioning (CalVer)](docs/CalVer.md)
- [Scalable Calendar Versioning (ScalVer)](docs/ScalVer.md)

## Testing

```bash
cd test
dotnet test
```

The test suite builds the three tools once, then exercises them as real CLI processes against temporary git repositories.

## License

[MIT](LICENSE)
