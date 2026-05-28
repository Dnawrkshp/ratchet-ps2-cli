# RatchetPs2.Experimental

This project is reserved for research and sandbox workflows that should not be
treated as part of the stable Core SDK surface.

The `Moby/` namespace contains generated/custom-static import experiments,
player-moby probe logic, topology budget exploration, diagnostics, and other
workflows that may change shape as the format work continues.

Some custom-static importer internals still live under
`RatchetPs2.Core/Moby/CustomStatic` while their implementation depends on
private `MobyGltfImporter` partial-class state. Keep unstable options, debug
entry points, and exploratory diagnostics here; move lower-level implementation
pieces here once they have clean data boundaries.
