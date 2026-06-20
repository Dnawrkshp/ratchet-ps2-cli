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
- `shrub`: Work with shrub static foliage geometry files.
- `skybox`: Work with skybox geometry files.
- `tfrag`: Work with tfrag terrain geometry files.
- `tie`: Work with tie static world geometry files.
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

## `skybox`

Commands for skybox geometry files.

```bash
ratchet-ps2 skybox [command] [options]
```

Subcommands:

- `export-gltf`: Export skybox geometry to a glTF model.
- `export-gltf-batch`: Export a directory of skybox binaries and write a viewer manifest.

Skybox support currently accepts `UYA` and `DL`.

### `skybox export-gltf`

Export skybox geometry to a glTF model.

```bash
ratchet-ps2 skybox export-gltf --game <UYA|DL> --input <input> --output <output>
```

Options:

- `--game <game>`: Required game ID. Currently `UYA` and `DL` are supported.
- `--input <input>`: Required path to the input `sky.bin` binary.
- `--output <output>`: Required path to write the output `.gltf` file.

Output behavior:

- A sibling `.buffer.bin` is written for binary glTF buffers.
- A sibling `.diagnostics.json` is written with shell, cluster, triangle, and texture counts.
- Embedded skybox textures are converted to PNGs in a sibling `textures/` folder and referenced by glTF materials.
- UYA and DL texture payloads are decoded with the palette-index remap used by the PIF texture utilities; DL also uses RAC4 pixel unswizzling.
- UYA and DL skybox texture alpha and RGB are preserved from the decoded source texture without alpha expansion or transparent RGB dilation.
- Skybox glTF textures use clamp-to-edge samplers so ST values on `0`/`4096` shell edges do not wrap-filter against the opposite texture edge.
- UYA/DL shell flag `0x2` exports a separate bloom material variant with emissive texture metadata and `KHR_materials_emissive_strength`.
- Untextured gouraud sky shells export the packed RGBA values stored in the cluster `st_ofs` table as `COLOR_0`; RGB source bytes are converted from sRGB-style values to glTF linear color.
- Triangle records are grouped by texture id; texture id `255` is exported as an untextured material.

Example:

```bash
ratchet-ps2 skybox export-gltf --game DL --input sky.bin --output sky.gltf
```

### `skybox export-gltf-batch`

Export a directory of skybox binaries to glTF and write a manifest for
`tools/skybox-viewer`.

```bash
ratchet-ps2 skybox export-gltf-batch --game <UYA|DL> --input-root <input-root> --output-root <output-root> [--sky-file-name <name>] [--manifest-name <name>] [--limit <count>]
```

Options:

- `--game <game>`: Required game ID. Currently `UYA` and `DL` are supported.
- `--input-root <input-root>`: Required directory to scan recursively.
- `--output-root <output-root>`: Required directory for exported models and the manifest.
- `--sky-file-name <name>`: Skybox binary file name to scan for. Defaults to `sky.bin`.
- `--manifest-name <name>`: Viewer manifest file name. Defaults to `manifest.json`.
- `--limit <count>`: Optional maximum number of skyboxes to export.

Output behavior:

- Each source skybox gets an output subdirectory containing `sky.gltf`,
  `sky.buffer.bin`, `sky.diagnostics.json`, and converted PNG textures.
- The manifest records per-skybox shell, cluster, triangle, texture, alpha, and
  conversion timing metadata.
- `tools/skybox-viewer/index.html` loads
  `/test-assets/skyboxes/_viewer/DL/manifest.json` by default and can switch to
  `/test-assets/skyboxes/_viewer/UYA/manifest.json` from the game dropdown.

Examples:

```bash
ratchet-ps2 skybox export-gltf-batch --game DL --input-root test-assets/skyboxes/DL --output-root test-assets/skyboxes/_viewer/DL
ratchet-ps2 skybox export-gltf-batch --game UYA --input-root test-assets/skyboxes/UYA --output-root test-assets/skyboxes/_viewer/UYA
python3 -m http.server 8000 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8000/tools/skybox-viewer/index.html
```

## `tfrag`

Commands for tfrag terrain geometry files.

```bash
ratchet-ps2 tfrag [command] [options]
```

Subcommands:

- `export-gltf`: Export tfrag terrain geometry to a glTF model.
- `export-gltf-batch`: Export a directory of tfrag terrain files and write a
  viewer manifest.

Tfrag support currently accepts `UYA` and `DL`.

### `tfrag export-gltf`

Export tfrag terrain geometry from a `terrain.bin` file to a glTF model grouped
by LOD and chunk.

```bash
ratchet-ps2 tfrag export-gltf --game <UYA|DL> --input <input> --output <output> [--texture-directory <directory>]
```

Options:

- `--game <game>`: Required game ID. Currently `UYA` and `DL` are supported.
- `--input <input>`: Required path to the input `terrain.bin` tfrag binary.
- `--output <output>`: Required path to write the output `.gltf` file.
- `--texture-directory <directory>`: Optional directory containing `tex.####.0.png`
  files. Defaults to the input terrain's directory.

Output behavior:

- A sibling `.buffer.bin` is written for binary glTF buffers.
- A sibling `.diagnostics.json` is written with chunk, LOD, VIF packet, triangle,
  and texture counts.
- PNG textures are copied to a sibling `textures/` folder and referenced by glTF
  materials when matching `tex.####.0.png` files are present.
- The glTF node hierarchy contains a root `tfrag` node with separate `lod_0`,
  `lod_1`, and `lod_2` groups; decoded chunk nodes are placed under the matching
  LOD group.
- LOD ranges follow the runtime tfrag upload behavior: `lod_2` uses
  `lod_2_ofs/lod_2_size`, `lod_1` uses `shared_ofs/lod_1_size`, and `lod_0`
  uses `shared_ofs/common_size` plus `lod_0_ofs/lod_0_size`.
- Texture assignment is currently a preview mapping from decoded topology packets
  to sequential tfrag texture entries, and is recorded in glTF extras and
  diagnostics.

Examples:

```bash
ratchet-ps2 tfrag export-gltf --game DL --input terrain.bin --output terrain.gltf
ratchet-ps2 tfrag export-gltf --game UYA --input test-assets/tfrags/UYA/level3/terrain/terrain.bin --output /tmp/uya-tfrag/terrain.gltf
```

### `tfrag export-gltf-batch`

Export a directory of tfrag terrain files to glTF and write a manifest for
`tools/tfrag-viewer`.

```bash
ratchet-ps2 tfrag export-gltf-batch --game <UYA|DL> --input-root <input-root> --output-root <output-root> [--terrain-file-name <name>] [--manifest-name <name>] [--limit <count>]
```

Options:

- `--game <game>`: Required game ID. Currently `UYA` and `DL` are supported.
- `--input-root <input-root>`: Required directory to scan recursively.
- `--output-root <output-root>`: Required directory for exported models and the
  manifest.
- `--terrain-file-name <name>`: Tfrag terrain binary file name to scan for.
  Defaults to `terrain.bin`.
- `--manifest-name <name>`: Viewer manifest file name. Defaults to
  `manifest.json`.
- `--limit <count>`: Optional maximum number of terrain files to export.

Output behavior:

- Each source terrain file gets an output subdirectory containing `terrain.gltf`,
  `terrain.buffer.bin`, `terrain.diagnostics.json`, and copied PNG textures when
  matching files are present.
- The manifest records per-terrain chunk, LOD, triangle, texture, and conversion
  timing metadata.
- `tools/tfrag-viewer/index.html` loads
  `/test-assets/tfrags/_viewer/DL/manifest.json` by default and can switch to
  `/test-assets/tfrags/_viewer/UYA/manifest.json` from the game dropdown.

Examples:

```bash
ratchet-ps2 tfrag export-gltf-batch --game DL --input-root test-assets/tfrags/DL --output-root test-assets/tfrags/_viewer/DL
ratchet-ps2 tfrag export-gltf-batch --game UYA --input-root test-assets/tfrags/UYA --output-root test-assets/tfrags/_viewer/UYA
python3 -m http.server 8000 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8000/tools/tfrag-viewer/index.html
```

## `shrub`

Commands for shrub static foliage geometry files.

```bash
ratchet-ps2 shrub [command] [options]
```

Subcommands:

- `export-gltf`: Export one shrub to a glTF model.
- `export-gltf-batch`: Export a directory of shrubs and write a viewer manifest.

Shrub support currently accepts `GC`, `UYA`, and `DL`.

### `shrub export-gltf`

Export shrub geometry to a glTF model. The input must be a packed shrub class
binary, such as `core.bin`.

```bash
ratchet-ps2 shrub export-gltf --game <GC|UYA|DL> --input <input> --output <output> [--texture-directory <directory>]
```

Options:

- `--game <game>`: Required game ID. Currently `GC`, `UYA`, and `DL` are supported.
- `--input <input>`: Required packed shrub binary.
- `--output <output>`: Required path to write the output `.gltf` file.
- `--texture-directory <directory>`: Optional directory containing PNG textures
  for packed shrub binaries. Defaults to the input shrub's directory.

Output behavior:

- A sibling `.buffer.bin` is written for binary glTF buffers.
- A sibling `.diagnostics.json` is written with mesh, triangle, texture, source,
  and billboard metadata.
- Packed shrub binaries are decoded from the 0x40-byte `ShrubClass` header,
  packet table, three VIF unpack payloads, fixed 24-normal table, and optional
  billboard record.

Examples:

```bash
ratchet-ps2 shrub export-gltf --game UYA --input test-assets/shrubs/1091/core.bin --output /tmp/uya-shrub/shrub.gltf
ratchet-ps2 shrub export-gltf --game DL --input test-assets/shrubs/1146/core.bin --output /tmp/dl-shrub/shrub.gltf
```

### `shrub export-gltf-batch`

Export a directory of shrubs to glTF and write a manifest for
`tools/shrub-viewer`.

```bash
ratchet-ps2 shrub export-gltf-batch --input-root <input-root> --output-root <output-root> [--game <auto|GC|UYA|DL>] [--source-kind <kind>] [--core-file-name <name>] [--manifest-name <name>] [--limit <count>]
```

Options:

- `--game <game>`: Optional game ID override. Defaults to `auto`, which reads
  sibling `.fbx.meta` labels and accepts `GC`, `UYA`, or `DL`.
- `--input-root <input-root>`: Required directory to scan recursively.
- `--output-root <output-root>`: Required directory for exported models and the manifest.
- `--source-kind <kind>`: `auto` or `packed`. Defaults to `auto`.
- `--core-file-name <name>`: Packed shrub class binary file name. Defaults to `core.bin`.
- `--manifest-name <name>`: Viewer manifest file name. Defaults to `manifest.json`.
- `--limit <count>`: Optional maximum number of shrubs to export.

Output behavior:

- `auto` scans packed `core.bin` files.
- Each source shrub gets an output subdirectory containing `shrub.gltf`,
  `shrub.buffer.bin`, `shrub.diagnostics.json`, and referenced PNG textures.
- The manifest records per-shrub mesh, primitive, vertex, triangle, texture,
  billboard, source labels, inferred game, and conversion timing metadata.
- `tools/shrub-viewer/index.html` loads
  `/test-assets/shrubs/_viewer/DL/manifest.json` by default and can switch to
  `/test-assets/shrubs/_viewer/UYA/manifest.json` from the game dropdown.

Examples:

```bash
ratchet-ps2 shrub export-gltf-batch --input-root test-assets/shrubs --output-root test-assets/shrubs/_viewer
ratchet-ps2 shrub export-gltf-batch --game DL --input-root test-assets/shrubs --output-root /tmp/dl-shrub-view --limit 25
python3 -m http.server 8000 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8000/tools/shrub-viewer/index.html
```

## `tie`

Commands for tie static world geometry files.

```bash
ratchet-ps2 tie [command] [options]
```

Subcommands:

- `inspect`: Inspect a tie class binary and dump its currently understood structure.
- `export-gltf`: Export tie geometry to a glTF model.
- `export-gltf-batch`: Export a directory of tie class binaries and write a viewer manifest.

Tie support currently accepts `GC`, `UYA`, and `DL`.

### `tie inspect`

Inspect a tie class binary and dump its currently understood structure.

```bash
ratchet-ps2 tie inspect --game <GC|UYA|DL> --input <input> [--output <output>]
```

Options:

- `--game <game>`: Required game ID. Currently `GC`, `UYA`, and `DL` are supported.
- `--input <input>`: Required path to the input `tie.bin` class binary.
- `--output <output>`: Optional path to write the structural report. The report is always printed to stdout.

Example:

```bash
ratchet-ps2 tie inspect --game DL --input tie.bin --output tie-report.txt
```

### `tie export-gltf`

Export tie geometry to a glTF model.

```bash
ratchet-ps2 tie export-gltf --game <GC|UYA|DL> --input <input> --output <output> [--lod <lod>] [--texture-directory <directory>]
```

Options:

- `--game <game>`: Required game ID. Currently `GC`, `UYA`, and `DL` are supported.
- `--input <input>`: Required path to the input `tie.bin` class binary.
- `--output <output>`: Required path to write the output `.gltf` file.
- `--lod <lod>`: LOD packet group to export: `0`, `1`, or `2`. Defaults to `0`.
- `--texture-directory <directory>`: Optional directory containing Wrench numeric PNGs or `tex.####.0.png` files. Defaults to the input tie's directory.

Output behavior:

- A sibling `.buffer.bin` is written for binary glTF buffers.
- A sibling `.diagnostics.json` is written with export counts and packet summaries.
- Diagnostics include structured packet tables, setup-row word roles, and
  consistency checks for decoded shader switches/references and row counts.
- When matching PNGs are found, they are copied into a sibling `textures/`
  folder and referenced by glTF materials.
- Texture alpha is scanned while copying. Materials that reference non-opaque
  opacity textures use glTF `MASK` or `BLEND` alpha mode. Reflective-mask
  materials keep their alpha metadata but stay opaque in glTF, and diagnostics
  include texture alpha min/max metadata.
- Glow RGBA remains separate from vertex colors through `_TIE_GLOW_0` and
  emissive preview material variants. Standalone tie exports do not bake
  instance-sourced vertex colors into `COLOR_0`.

Example:

```bash
ratchet-ps2 tie export-gltf --game DL --input tie.bin --output tie.gltf
ratchet-ps2 tie export-gltf --game DL --input tie.bin --output tie-lod1.gltf --lod 1
ratchet-ps2 tie export-gltf --game DL --input tie.bin --output tie.gltf --texture-directory textures
```

### `tie export-gltf-batch`

Export a directory of tie class binaries to glTF and write a manifest for
`tools/tie-viewer`.

```bash
ratchet-ps2 tie export-gltf-batch --game <GC|UYA|DL> --input-root <input-root> --output-root <output-root> [--core-file-name <name>] [--manifest-name <name>] [--lod <lod>] [--limit <count>]
```

Options:

- `--game <game>`: Required game ID. Currently `GC`, `UYA`, and `DL` are supported.
- `--input-root <input-root>`: Required directory to scan recursively.
- `--output-root <output-root>`: Required directory for exported models and the manifest.
- `--core-file-name <name>`: Tie class binary file name to scan for. Defaults to `core.bin`.
- `--manifest-name <name>`: Viewer manifest file name. Defaults to `manifest.json`.
- `--lod <lod>`: LOD packet group to export: `0`, `1`, or `2`. Defaults to `0`.
- `--limit <count>`: Optional maximum number of ties to export.

Output behavior:

- Each source tie gets an output subdirectory containing `tie.gltf`,
  `tie.buffer.bin`, `tie.diagnostics.json`, and copied `textures/` when PNGs
  are available.
- The manifest records per-model header, geometry, packet, glow, texture, and
  conversion timing metadata.
- Manifest totals include found/succeeded/failed counts, total conversion time,
  average and median successful export time, and total input/output bytes.
- Aggregate packet metadata includes multipass type, shader count, and setup
  tail-word distributions.

Example:

```bash
ratchet-ps2 tie export-gltf-batch --game DL --input-root "test-assets/DL Ties/ALL DL" --output-root "test-assets/DL Ties/ALL DL/_viewer"
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
