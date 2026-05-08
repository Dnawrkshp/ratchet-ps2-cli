# Command Reference

This document describes the commands exposed by the `ratchet-ps2` CLI.

For the most current command-line help, run:

```bash
ratchet-ps2 --help
ratchet-ps2 <command> --help
ratchet-ps2 <command> <subcommand> --help
```

## Global Usage

```bash
ratchet-ps2 [command] [options]
```

Global options:

- `-?`, `-h`, `--help`: Show help and usage information.
- `--version`: Show version information.

Top-level commands:

- `hello`: Print a hello-world style greeting for a selected game.
- `hw3d`: Inspect experimental HUD widget 3D files.
- `pif`: Work with PIF texture files.
- `wad`: Work with WAD-compressed files.

## Game IDs

Commands that accept `--game` currently support:

- `1` or `RC1`: Ratchet & Clank
- `2` or `GC`: Going Commando
- `3` or `UYA`: Up Your Arsenal
- `4` or `DL`: Deadlocked

## `hello`

Smoke-test command for game selection and command wiring.

```bash
ratchet-ps2 hello [<target>...] --game <game>
```

Arguments:

- `<target>`: Optional words to include in the hello target. Defaults to `world`.

Options:

- `--game <game>`: Required game ID. Accepts `1`, `2`, `3`, `4`, `RC1`, `GC`, `UYA`, or `DL`.

Examples:

```bash
ratchet-ps2 hello --game RC1
ratchet-ps2 hello minimap tools --game UYA
```

## `pif`

Commands for PIF texture files.

```bash
ratchet-ps2 pif [command] [options]
```

Subcommands:

- `to-png`: Convert a PIF texture file to a PNG image.

### `pif to-png`

Convert a PIF texture file to a PNG image.

```bash
ratchet-ps2 pif to-png --input <input> --output <output> [--png-format <format>] [--double-alpha]
```

Options:

- `--input <input>`: Required path to the input PIF texture file.
- `--output <output>`: Required path to write the output PNG file.
- `--png-format <format>`: PNG output format. Accepts `rgba32`, `indexed8`, or `indexed4`. Defaults to `rgba32`.
- `--double-alpha`: Double alpha values while converting, useful for some UI/minimap textures.

Examples:

```bash
ratchet-ps2 pif to-png --input texture.pif --output texture.png
ratchet-ps2 pif to-png --input minimap.pif --output minimap.png --double-alpha
ratchet-ps2 pif to-png --input icon.pif --output icon.png --png-format indexed8
```

## `wad`

Commands for WAD-compressed files and TOC-backed data blocks.

```bash
ratchet-ps2 wad [command] [options]
```

Subcommands:

- `compress`: Compress a file using the game's WAD compression.
- `decompress`: Decompress a WAD-compressed file to a single output file.
- `unpack-toc`: Extract entries from a TOC-backed data block.

### `wad compress`

Compress a file using the game's WAD compression.

```bash
ratchet-ps2 wad compress --input <input> --output <output>
```

Options:

- `--input <input>`: Required path to the decompressed input file.
- `--output <output>`: Required path to write the compressed WAD file.

Example:

```bash
ratchet-ps2 wad compress --input data.bin --output data.wad
```

### `wad decompress`

Decompress a WAD-compressed file to a single output file.

```bash
ratchet-ps2 wad decompress --input <input> --output <output>
```

Options:

- `--input <input>`: Required path to the compressed WAD file.
- `--output <output>`: Required path to write the decompressed output file.

Example:

```bash
ratchet-ps2 wad decompress --input data.wad --output data.bin
```

### `wad unpack-toc`

Extract entries from a TOC-backed data block.

```bash
ratchet-ps2 wad unpack-toc --input <input> --output <output-directory> [--offset <offset>]
```

Options:

- `--input <input>`: Required path to the TOC-backed data file.
- `--output <output-directory>`: Required path to the output directory for extracted entries.
- `--offset <offset>`: Optional TOC start offset in decimal or hex. Defaults to `0`.

Output behavior:

- PIF entries are written as `.pif` and converted `.png` files.
- WAD entries are decompressed when possible, then processed again.
- WAD entries that cannot be decompressed are written as `.wad`.
- Other entries are written as `.bin` after trimming trailing zero bytes.

Examples:

```bash
ratchet-ps2 wad unpack-toc --input hud.dat --output extracted
ratchet-ps2 wad unpack-toc --input hud.dat --output extracted --offset 0x1000
```

## `hw3d`

Commands for experimental HUD widget 3D inspection.

```bash
ratchet-ps2 hw3d [command] [options]
```

Subcommands:

- `inspect`: Inspect an HW3D binary and dump the currently understood outer structure.

### `hw3d inspect`

Inspect an HW3D/HBN binary and dump the currently understood structure.

```bash
ratchet-ps2 hw3d inspect --input <input> [--output <output>] [--svg <svg>]
```

Options:

- `--input <input>`: Required path to the hudw3d / HW3D binary file.
- `--output <output>`: Optional path to write the structural report. The report is always printed to stdout.
- `--svg <svg>`: Optional path to write a preliminary SVG visualization for supported HBN files.

Examples:

```bash
ratchet-ps2 hw3d inspect --input hudw3d.bin
ratchet-ps2 hw3d inspect --input menu.hbn --output report.txt --svg menu.svg
```
