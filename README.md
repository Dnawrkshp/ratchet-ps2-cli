# ratchet-ps2-cli
CLI tool to interact with the Ratchet & Clank series games for PS2.

## Build

CLI command usage is documented in [docs/COMMANDS.md](docs/COMMANDS.md).

Build the CLI:

```bash
dotnet build build.proj -t:Build
```

Publish CLI builds for Linux and Windows:

```bash
dotnet build build.proj -t:PublishCliPlatforms
```

This writes framework-dependent artifacts to:

- `artifacts/publish/ratchet-ps2-linux-x64/`
- `artifacts/publish/ratchet-ps2-win-x64/`

To publish self-contained artifacts, pass:

```bash
dotnet build build.proj -t:PublishCliPlatforms -p:PublishSelfContained=true
```

To clean generated build artifacts:

```bash
dotnet build build.proj -t:CleanArtifacts
```
