# Architecture

## Goals

This repository is being structured so it can serve three roles over time:

1. A cross-platform CLI
2. A reusable SDK/dependency for other .NET projects
3. A codebase that can later be adapted to WASM-friendly hosts

Because of that, the architecture should keep domain logic separate from host-specific behavior.

## Project roles

### `RatchetPs2.Cli`

This project is the command-line frontend.

It is responsible for:

- parsing command arguments
- resolving the selected game
- presenting output to stdout/stderr
- handling CLI-specific UX
- handling CLI-specific export/options concepts

It should **not** become the home of core parsing, game logic, or reusable data models.

The current CLI registers these command groups:

- `wad`, for WAD compression, decompression, archive inspection, and TOC unpacking
- `pif`, for PIF texture conversion to PNG
- `hw3d`, for HW3D/HBN structure inspection and preliminary SVG visualization
- `hello`, currently a small game-selection/module-resolution smoke command

#### Recommended internal CLI organization

Within the CLI project itself:

- keep `Program.cs` thin and focused on composition/root command registration
- keep `Commands/` reserved for actual commands and command groups
- place shared CLI-only helpers such as common options, command builders, and reusable command wiring outside `Commands/` (for example in `Abstractions/`)

If multiple commands share the same option or argument pattern, prefer defining it once in the shared CLI helper area and reusing it from command files.

### `RatchetPs2.Core`

This project is the shared library layer.

It should contain:

- shared models used across multiple games
- shared abstractions/contracts
- reusable logic that is not tied to the CLI
- APIs intended to be safe for SDK-style consumption

Anything placed here should be usable by:

- the CLI
- tests
- another .NET application
- a future WASM host

`RatchetPs2.Core` currently multi-targets `net9.0` and `net9.0-browser`, so browser compatibility is already part of the build contract rather than only a future aspiration.

Current major areas in `Core` are:

- `Games/`: shared game identifiers and the `IGameModule` contract
- `IO/`: byte-order, sector, magic, and stream helpers used by binary parsers
- `Wad/`: WAD compression, decompression, archive readers, TOC readers, and WAD model types
- `Textures/`: texture models, conversion options, PIF readers/exporters, and PNG encoding helpers
- `Hud/Hw3d/`: HW3D/HBN archive parsing, structural reporting, and early visualization helpers
- `Moby/`: stable moby model, binary, VIF, and normal glTF import/export primitives

The texture pipeline intentionally exposes byte/stream-oriented entry points.
This is important because the same PIF conversion path is used by both the CLI
and the browser-facing WASM host.

Moby diagnostics and research workflows should not be added directly to the
stable moby root. If a diagnostic or research tool still needs importer
internals, keep it in `RatchetPs2.Experimental` until the dependency can be
extracted behind a stable model.

### `RatchetPs2.Experimental`

This project is reserved for research and sandbox workflows that are useful for
development and CLI inspection, but are not part of the stable Core domain
model.

Use this project for:

- generated/custom-static moby import experiments
- moby structural analysis reports under `Moby/Diagnostics/`
- skin and vertex-control inspection under `Moby/Diagnostics/`
- diagnostic JSON writers
- debug visualizations
- topology and packet-budget probes
- player-moby research workflows
- one-off or unstable workflows that should not be promoted to Core yet

If an experimental path becomes reliable and generally useful, promote the stable
domain pieces into `RatchetPs2.Core` and leave only exploratory orchestration
here. It may depend on `RatchetPs2.Core`; Core exposes limited internal access to
this assembly where a diagnostic needs implementation details without making
those details public SDK API.

### `RatchetPs2.Games.RC1`, `GC`, `UYA`, `DL`

These projects isolate game-specific behavior.

They should contain:

- game-specific constants
- game-specific models that do not generalize well
- per-game readers/parsers/transformers
- implementations of shared contracts from `RatchetPs2.Core`

If a type or behavior is truly shared, it should be moved to `RatchetPs2.Core`.

At the moment these modules are lightweight. They implement `IGameModule` with a `GameId` and display name, and provide the extension point where future per-game services and quirks should live.

### `RatchetPs2.Wasm`

This project is the browser-facing host for selected `RatchetPs2.Core` capabilities.

It is responsible for:

- exposing JS-invokable entry points through `Exports.cs`
- packaging the Blazor WebAssembly runtime assets
- shipping generated JavaScript and TypeScript wrapper files for consuming web apps
- keeping browser host concerns out of `RatchetPs2.Core`

The current exported surface focuses on PIF-to-PNG conversion:

- `getApiVersion`
- single-image PIF conversion
- batch PIF conversion
- packed batch conversion to reduce JS/WASM transfer overhead

`RatchetPs2.Wasm` should continue to depend on `RatchetPs2.Core`, not on the CLI or per-game projects unless a browser use case clearly requires game-specific behavior.

### `RatchetPs2.Wasm.Generator`

This small tool generates the browser wrapper contract for `RatchetPs2.Wasm`.

It reads:

- `src/RatchetPs2.Wasm/wasm-exports.json`
- JavaScript and TypeScript templates under `src/RatchetPs2.Wasm.Generator/Templates/`

It writes:

- `src/RatchetPs2.Wasm/ratchetps2-wasm.js`
- `src/RatchetPs2.Wasm/ratchetps2-wasm.d.ts`

The WASM project runs this generator before build. When changing the JS-facing WASM API, update the manifest and generated wrappers together with the C# export implementation.

## Contract for SDK-friendly code

Reusable code should follow these rules:

### 1. Keep host concerns out of reusable libraries

Library projects should not directly depend on:

- console input/output
- process launching
- shell commands
- OS-specific APIs
- UI frameworks

Those concerns belong in the CLI or another host layer.

### 2. Prefer data/stream-based APIs over file-path-only APIs

When possible, prefer inputs like:

- `Stream`
- `ReadOnlyMemory<byte>`
- `byte[]`
- plain option/model objects

This makes APIs easier to use in:

- CLI tools
- unit tests
- web apps
- WASM environments

### 3. Keep core logic deterministic and side-effect-light

Parsing and transformation logic should be pure or close to pure where practical.

Avoid hidden global state and avoid writing directly to console from reusable code.

### 4. Use public APIs intentionally

Types in `Core` intended for external use should have stable, understandable API shapes.

Prefer:

- explicit model types
- focused service abstractions
- clear exceptions/messages

Avoid exposing CLI-specific types from reusable projects.

### 5. WASM compatibility should remain possible

To preserve future WASM support, avoid baking in assumptions about:

- unrestricted filesystem access
- unrestricted threading/background workers
- native platform interop
- infinite memory for large asset processing

WASM support does not need to be fully implemented now, but new reusable APIs should avoid blocking it unnecessarily.

## Recommended dependency direction

Dependencies should generally point inward like this:

```text
RatchetPs2.Cli -> RatchetPs2.Core
RatchetPs2.Cli -> RatchetPs2.Experimental
RatchetPs2.Cli -> RatchetPs2.Games.*
RatchetPs2.Games.* -> RatchetPs2.Core
RatchetPs2.Experimental -> RatchetPs2.Core
RatchetPs2.Wasm -> RatchetPs2.Core
RatchetPs2.Wasm -> RatchetPs2.Wasm.Generator (build-time only)
```

Avoid reverse dependencies such as:

- `Core -> Cli`
- `Games.* -> Cli`
- one game project depending on another game project unless there is a very strong reason
- `Core -> Wasm`
- `Wasm -> Cli`

The current solution has two host-style projects:

- `RatchetPs2.Cli`, which owns console UX and file-oriented command orchestration
- `RatchetPs2.Wasm`, which owns browser/WASM interop and packaging

Both should call reusable library APIs rather than duplicating parsing or conversion logic.

## Shared vs game-specific placement rule

When deciding where something belongs:

- put it in a game project if it only matches one game or has game-specific quirks
- put it in `Core` if it is meaningfully shared across multiple games
- keep it in `Cli` if it only exists for command-line interaction or presentation

If unsure, prefer starting in the game project and only promoting to `Core` once reuse is proven.

## Practical next-step API direction

As the codebase grows, favor adding reusable services in the library layer that the CLI simply calls.

Example direction:

- readers/parsers that accept streams or bytes
- export services that return reusable models or byte content
- game modules that expose capabilities through shared interfaces

The CLI should stay as thin orchestration over those reusable APIs.

## Current implementation notes

- `System.CommandLine` is a CLI-only dependency.
- PNG encoding is implemented inside `RatchetPs2.Core.Textures` rather than through a host-specific image library.
- Current WAD and PIF APIs generally expose stream or byte-array entry points; preserve that pattern for SDK and WASM reuse.
- Some HW3D/HBN functionality is still exploratory and includes reverse-engineering notes/report generation. Keep this in the reusable layer only while it remains byte-oriented and host-neutral; move presentation-heavy output choices to host projects as they grow.
- The game-module abstraction is intentionally small today. Add capability interfaces in `Core` when multiple hosts or games need the same behavior, then implement them in the appropriate game projects.
