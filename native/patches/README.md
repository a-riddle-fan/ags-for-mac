# Upstream patches

Fixes for bugs in upstream AGS code found while porting. `native/CMakeLists.txt` applies each patch
to a copy of the file in the build tree (`build/native-cmake/patched-upstream/`); the `ags/` checkout
is never modified. If a patch stops applying after an upstream update, configuration fails — check
whether upstream fixed it and drop the patch, or refresh it.

The patches are plain unified diffs against upstream `master` @ `c4585d2d7` and can be submitted to
https://github.com/adventuregamestudio/ags as they are.

| Patch | Bug | Impact upstream |
|---|---|---|
| `0007-Engine-editor-debugging-on-non-Windows-platforms.patch` | The editor debugger only existed on Windows (named pipes); elsewhere `--enabledebugger` quit and breaking/error reporting were compiled out, although `FileBasedAGSDebugger` existed. | No debugging (breakpoints, stepping, error breaks) outside Windows. Now `--enabledebugger <token> [<pipe\|file>[=<config>]]` selects the transport (default: pipes on Windows, files elsewhere, the syntax suggested in the PR review); the file-based debugger uses `dbgsend_<token>.tmp` / `dbgrecv_<token>.tmp` in the folder from `file=dir:<folder>`, with atomic writes. Maintainer answer in [#3159](https://github.com/adventuregamestudio/ags/issues/3159): file-based is fine for now, a command line switch at least; pipe/socket on all platforms preferred long term. Upstream PR [#3162](https://github.com/adventuregamestudio/ags/pull/3162). |

The patch applies to the upstream target `engine` inside `native/CMakeLists.txt`.
`scripts/build-upstream-tools.sh` builds the tools and the engine from that same patched project.

## Dropped (merged upstream)

0001–0004 ([#3140](https://github.com/adventuregamestudio/ags/pull/3140)–[#3143](https://github.com/adventuregamestudio/ags/pull/3143)),
0006 ([#3145](https://github.com/adventuregamestudio/ags/pull/3145)) and 0008 ([#3160](https://github.com/adventuregamestudio/ags/pull/3160))
are in upstream `master` since `edf5bb991`; 0005 ([#3144](https://github.com/adventuregamestudio/ags/pull/3144)) and
0010 ([#3163](https://github.com/adventuregamestudio/ags/pull/3163)) since `63ebe34fd`; 0009
([#3164](https://github.com/adventuregamestudio/ags/pull/3164)) since `c4585d2d7`. The numbers are not reused.

## Noted, not patched

- 24-bit `SavePNG()` and `CopySwapRGBA` assume tightly packed rows (they ignore `BitmapData::GetStride()`).
  Native AGS bitmaps are always packed, so it's harmless upstream; libagsnative repacks rows before saving.

The `LoadPNG()` truecolor copy noted here earlier (only `width * height` bytes copied) was fixed upstream in `fc59f7a92`.
