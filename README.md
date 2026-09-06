# ReleaseTools

ReleaseTools is a set of three small, read-only CLI tools that calculate versions from Git history. The tools do not create tags, change files, or publish packages; a successful calculation writes either one version line or one JSON object to stdout, making the commands easy to use in scripts and CI pipelines.

| Command | Version shape | Version source |
|---------|---------------|----------------|
| `semver` | `MAJOR.MINOR.PATCH` | Conventional Commits after the highest reachable stable tag |
| `calver` | Configurable date tokens; default `YYYY.MM.PATCH` | Effective HEAD date and the commit count in its date window |
| `scalver` | `MAJOR.DATE.PATCH` | Manual major, effective HEAD date, and the commit count in its date window |

## Requirements

- Git available on `PATH`.
- A Git repository with at least one commit.
- .NET SDK 10.0.300 or a compatible later SDK. The repository pins `10.0.300` with `rollForward: latestMinor` in [`global.json`](global.json); the tools use .NET 10 file-based apps and `#:include` directives.
- Complete history for accurate results. SemVer also needs all tags. In GitHub Actions, use `actions/checkout` with `fetch-depth: 0` rather than the shallow default.

## Installation

Install any or all of the tools from NuGet:

```bash
dotnet tool install --global ReleaseTools.SemVer
dotnet tool install --global ReleaseTools.CalVer
dotnet tool install --global ReleaseTools.ScalVer
```

The installed commands are `semver`, `calver`, and `scalver`. Replace `install` with `update` to update an existing global installation.

## Quick start

Run the installed commands from anywhere inside the Git repository:

```bash
semver
calver
scalver --major 1
```

To run the source directly from the root of this repository, put tool arguments after `--`:

```bash
dotnet run --file src/semver.cs -- --prerelease alpha
dotnet run --file src/calver.cs -- --format YYYY.0M.0D.PATCH
dotnet run --file src/scalver.cs -- --major 1 --date-format YYYYMMDD
```

Every command supports `-h`/`--help`.

## Common options and behavior

| Option | Behavior |
|--------|----------|
| `-f, --folder <PATH>` | Restrict history to a tracked, repository-relative folder |
| `-p, --prerelease <ID>` | Add a prerelease label made of ASCII letters, digits, and hyphens |
| `-b, --buildmetadata` | Add the effective HEAD short commit SHA as build metadata |
| `-o, --output <text\|json>` | Select output format; default is `text` |

`--folder` is useful for independently versioned components in a monorepo. The path is always relative to the repository root. When it is present:

- only commits touching that folder are analyzed or counted;
- the latest commit touching the folder supplies the date and build SHA;
- the path is treated literally, so pathspec characters such as `[` are not expanded;
- missing, untracked, absolute, escaping, and unnormalized paths fail with a nonzero exit code.

Date-based calculations use the effective HEAD commit's **committer timestamp converted to UTC**, not the wall clock. This makes a calculation reproducible at a given commit. The current commit is included in its date-window count, so the first commit in a window has patch number `1` when `PATCH` is present.

Prerelease and build metadata can be combined in standard order:

```text
1.2.0-alpha.3+a1b2c3d
```

## Semantic Versioning (`semver`)

`semver` uses the fixed schema `MAJOR.MINOR.PATCH`.

```bash
semver [options]
```

In addition to the common options, SemVer accepts:

| Option | Behavior |
|--------|----------|
| `--prefix <PREFIX>` | Consider only tags beginning with this exact literal prefix |

The prefix selects tags but is not included in the emitted version. For example, `semver --prefix api-` uses `api-1.2.3` as a base and still outputs a value such as `1.3.0`.

### Calculation rules

1. Find all tags reachable from `HEAD` whose text after the requested prefix is a complete, strict SemVer.
2. Ignore malformed, unrelated, unreachable, and prerelease tags. Stable versions containing build metadata are valid bases.
3. Select the numerically highest stable core version, even if a lower version tag is closer to `HEAD`.
4. Analyze commit messages in `base-tag..HEAD`, optionally restricted by `--folder`, and apply the highest detected increment once.

| Conventional Commit | Increment |
|---------------------|-----------|
| `type!:` or `type(scope)!:` | Major |
| A valid Conventional Commit with a `BREAKING CHANGE:` or `BREAKING-CHANGE:` footer | Major |
| `feat:` | Minor |
| `fix:`, `perf:`, `revert:` | Patch |
| Any other type or a non-Conventional subject | None |

Scopes may contain values such as `core-api`, `web/client`, or `data.access`. Types are matched case-insensitively. Multiple relevant commits do not repeatedly increment the version: any breaking change wins over features, and any feature wins over patches.

If no matching stable tag exists, the core version is always `0.1.0`; commit messages do not alter that initial core version. Prefixes are explicit: `v1.2.3` requires `--prefix v`, and `api-1.2.3` requires `--prefix api-`.

SemVer prereleases include the number of selected commits since the base, because the core version may remain unchanged:

```bash
semver --prerelease alpha --buildmetadata
# 1.2.0-alpha.3+a1b2c3d
```

The prerelease counter is still added for changes that do not affect the core version. With no matching stable tag, it counts the selected history through `HEAD`.

## Calendar Versioning (`calver`)

`calver` builds a version from a case-sensitive token format. It does not inspect commit messages or use tags.

```bash
calver [--format <FORMAT>] [options]
```

The default format is `YYYY.MM.PATCH`, which emits an unpadded month such as `2026.9.4`. This keeps the CLI value, Git tag version, and NuGet package version identical.

| Token | Meaning | Example for January 5, 2005 |
|-------|---------|-----------------------------|
| `YYYY` | Four-digit year | `2005` |
| `YY` | Unpadded two-digit year | `5` |
| `0Y` | Zero-padded two-digit year | `05` |
| `MM` | Unpadded month | `1` |
| `0M` | Zero-padded month | `01` |
| `WW` | Unpadded ISO week | `1` |
| `0W` | Zero-padded ISO week | `01` |
| `DD` | Unpadded day | `5` |
| `0D` | Zero-padded day | `05` |
| `PATCH` | Commit count in the selected date window | `3` |

Tokens can be joined directly or separated with dots. For example, `YY.0M0D.PATCH` produces `25.0223.1` for February 23, 2025. Other separators and arbitrary text are not supported.

A valid format:

- contains exactly one year token;
- contains at most one token from each date category and at most one `PATCH`;
- does not combine month and week;
- uses a month when it uses a day;
- orders tokens as Year → Month/Week → Day → PATCH;
- has no leading, trailing, or repeated dots.

`PATCH` counts commits in the window selected by the finest date token:

| Format | PATCH window | Example |
|--------|--------------|---------|
| `YYYY.PATCH` | Year | `2025.12` |
| `YYYY.0M.PATCH` | Month | `2025.02.3` |
| `YYYY.0W.PATCH` | ISO week, Monday through Sunday | `2025.08.2` |
| `YYYY.0M.0D.PATCH` | Day | `2025.02.23.1` |

When the format omits `PATCH`, no counter is appended:

```bash
calver --format YYYY.0M
# 2025.02

calver --format YYYY.0M.0D.PATCH --prerelease rc --buildmetadata
# 2025.02.23.1-rc+a1b2c3d
```

## Scalable Calendar Versioning (`scalver`)

ScalVer combines a manually managed major version with a fixed-width date segment and a date-window commit count:

```text
MAJOR.DATE.PATCH
```

```bash
scalver --major <NUMBER> [options]
```

In addition to the common options, ScalVer accepts:

| Option | Behavior |
|--------|----------|
| `-m, --major <NUMBER>` | Required non-negative major version; bump it manually for breaking changes |
| `-d, --date-format <FORMAT>` | `YYYY`, `YYYYMM`, or `YYYYMMDD`; default is `YYYYMM` |

Date-format values are accepted case-insensitively and normalized internally.

| Date format | Example for February 23, 2025 | PATCH window |
|-------------|-------------------------------|--------------|
| `YYYY` | `1.2025.3` | Year |
| `YYYYMM` | `1.202502.3` | Month |
| `YYYYMMDD` | `1.20250223.3` | Day |

ScalVer does not inspect commit messages or use tags. You choose when to increment the major. Within a major line, the convention is that the date segment may become more precise (`YYYY` → `YYYYMM` → `YYYYMMDD`) but should not become less precise; the tool does not enforce that convention.

```bash
scalver --major 2 --date-format YYYYMMDD --prerelease rc --buildmetadata
# 2.20250223.1-rc+a1b2c3d
```

## Output formats

Text output is the default and contains only `fullVersion` followed by a newline. Diagnostics are written to stderr, and invalid arguments, invalid repository state, or Git failures return a nonzero exit code.

JSON output is pretty-printed and uses camel-case property names:

```bash
semver --output json
```

```json
{
  "version": "1.2.0",
  "fullVersion": "1.2.0-alpha.3+a1b2c3d",
  "baseTag": "1.1.0",
  "commitCount": 3,
  "incrementReason": "feat commits detected",
  "schema": "{MAJOR}.{MINOR}.{PATCH}",
  "prerelease": "alpha.3",
  "buildMetadata": "a1b2c3d"
}
```

All results contain `version`, `fullVersion`, `commitCount`, `incrementReason`, and `schema`. Nullable properties are omitted instead of being written as `null`. Scheme-specific fields are:

| Field | Present for |
|-------|-------------|
| `baseTag` | SemVer when a matching base tag exists |
| `prerelease`, `buildMetadata` | Any scheme when requested |
| `format` | CalVer |
| `major`, `dateFormat` | ScalVer |

For CalVer without a `PATCH` token, `commitCount` is `0` and `incrementReason` is `no patch segment`.

## CI and package releases

The repository publishes three independent .NET tool packages. Their version streams and trigger tags are isolated:

| Package | Release calculation | Tag pattern |
|---------|---------------------|-------------|
| `ReleaseTools.SemVer` | `semver --prefix semver-v` | `semver-v<VERSION>` |
| `ReleaseTools.CalVer` | `calver` using `YYYY.MM.PATCH` | `calver-v<VERSION>` |
| `ReleaseTools.ScalVer` | `scalver --major <release.json value> --date-format YYYYMMDD` | `scalver-v<VERSION>` |

CI builds all three file-based apps, runs the black-box CLI tests, packs probe packages, verifies package metadata and embedded per-tool README files, installs the packages locally, and smoke-tests their commands.

To release, run **Actions → Create release tag → Run workflow** and select one tool. The workflow checks out the latest `main`, calculates that tool's version, and creates an annotated package-specific tag. That tag starts the publishing workflow, which recalculates and verifies the version, packs only the selected tool, authenticates to NuGet through GitHub OIDC Trusted Publishing, publishes with duplicate protection, and uploads the `.nupkg` as a workflow artifact.

The release workflows require:

- `RELEASE_TOKEN`: a fine-grained personal access token with read/write repository contents access, used to push a tag that can trigger the publishing workflow.
- `NUGET_USER`: the NuGet.org profile name authorized by a Trusted Publishing policy for owner `vicmaeg`, repository `ReleaseTools`, and workflow `release.yml`, with no GitHub environment configured.

[`release.json`](release.json) is the checked-in source of the manually controlled ScalVer major.

## Development

Build each tool from the repository root:

```bash
dotnet build src/semver.cs --configuration Release
dotnet build src/calver.cs --configuration Release
dotnet build src/scalver.cs --configuration Release
```

Run the black-box test suite:

```bash
dotnet test test/ReleaseTools.Tests.csproj --configuration Release
```

The suite builds each real CLI once, then runs it against temporary Git repositories.

## Detailed documentation

- [Semantic Versioning (SemVer)](docs/SemVer.md)
- [Calendar Versioning (CalVer)](docs/CalVer.md)
- [Scalable Calendar Versioning (ScalVer)](docs/ScalVer.md)

## License

[MIT](LICENSE)
