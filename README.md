# ReleaseTools

A minimal, opinionated CLI tool for versioning git repositories. Supports SemVer, CalVer, and ScalVer.

## Installation

### As .NET Tool

```bash
dotnet tool install -g ReleaseTools.ver
```

### From Source

```bash
dotnet run --file src/ver.cs -- --help
```

## Quick Start

```bash
# Semantic Versioning
ver next --schema "{MAJOR}.{MINOR}.{PATCH}"

# Calendar Versioning
ver next --schema "{YYYY}.{0M}.{PATCH}"

# Scalable Calendar Versioning
ver next --schema "{MAJOR}.{YYYY}.{PATCH}"
```

## Commands

### `ver next`

Calculate the next version without creating a tag.

```bash
ver next [options]

Options:
  -s, --schema <SCHEMA>     Version schema (required)
  -p, --prefix <PREFIX>     Tag prefix for monorepo
  -f, --folder <PATH>       Filter commits to folder
  -o, --output <FORMAT>     Output format: text|json (default: text)
```

### `ver tag`

Create a git tag with the calculated version.

```bash
ver tag [options]

Options:
  -s, --schema <SCHEMA>     Version schema (required)
  -p, --prefix <PREFIX>     Tag prefix for monorepo
  -f, --folder <PATH>       Filter commits to folder
  -m, --message <MSG>       Tag message
  -a, --annotate           Create annotated tag
  --push                   Push tag to origin
  -o, --output <FORMAT>     Output format: text|json
```

## Schema Tokens

### SemVer Tokens
- `{MAJOR}` - Major version
- `{MINOR}` - Minor version
- `{PATCH}` - Patch version
- `{SHORTSHA}` - Short commit hash
- `{SHA}` - Full commit hash
- `{NUM_COMMITS}` - Commits since last tag

### CalVer Tokens
- `{YYYY}` - Full year
- `{YY}` - Short year
- `{0Y}` - Zero-padded year
- `{MM}` - Month (1-12)
- `{0M}` - Zero-padded month
- `{WW}` - Week of year
- `{0W}` - Zero-padded week
- `{DD}` - Day
- `{0D}` - Zero-padded day
- `{PATCH}` - Patch to avoid clashing

### ScalVer Tokens
- `{MAJOR}` - Major version (breaking changes)
- All CalVer tokens for DATE component

### Pre-release Suffixes
- `-alpha.{NUM_COMMITS}`
- `-beta.{NUM_COMMITS}`
- `-rc.{NUM_COMMITS}`

### Build Metadata
- `+{SHA}` or `+{SHORTSHA}` (not Docker-compatible)
- `-{SHORTSHA}` (Docker-compatible)

## Examples

### SemVer

```bash
# Basic
ver next --schema "{MAJOR}.{MINOR}.{PATCH}"
# Output: 1.2.0

# With pre-release
ver next --schema "{MAJOR}.{MINOR}.{PATCH}-alpha.{NUM_COMMITS}"
# Output: 1.2.0-alpha.5

# Monorepo with prefix
ver next --schema "{MAJOR}.{MINOR}.{PATCH}" --prefix "api-"
# Output: api-1.2.0
```

### CalVer

```bash
# Monthly
ver next --schema "{YYYY}.{0M}.{PATCH}"
# Output: 2025.02.0

# Daily
ver next --schema "{YYYY}.{0M}{0D}.{PATCH}"
# Output: 2025.0223.0
```

### ScalVer

```bash
# Yearly
ver next --schema "{MAJOR}.{YYYY}.{PATCH}"
# Output: 1.2025.0

# Daily with breaking changes support
ver next --schema "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}"
# Output: 1.20250223.0
```

## Documentation

- [Semantic Versioning (SemVer)](docs/SemVer.md)
- [Calendar Versioning (CalVer)](docs/CalVer.md)
- [Scalable Calendar Versioning (ScalVer)](docs/ScalVer.md)

## Testing

```bash
cd test
dotnet restore
dotnet test
```

## License

MIT
