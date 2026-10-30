# Upstream patches

Fixes for bugs in upstream AGS code found while porting. `native/CMakeLists.txt` applies each patch
to a copy of the file in the build tree (`build/native-cmake/patched-upstream/`); the `ags/` checkout
is never modified. If a patch stops applying after an upstream update, configuration fails — check
whether upstream fixed it and drop the patch, or refresh it.

The patches are plain unified diffs against upstream `master` @ `edf5bb991` and can be submitted to
https://github.com/adventuregamestudio/ags as they are.

| Patch | Bug | Impact upstream |
|---|---|---|
| `0005-Tools-keep-mask-color-converting-32-to-16-bit.patch` | `FixMaskColorImpl()` compares 32-bit pixels (with alpha, `0xFFFF00FF`) against `MASK_COLOR_32` (`0xFF00FF`, without alpha) and ignores alpha. Reworked per upstream review: zero alpha or opaque magenta → mask, semi-transparent magenta stays visible. Upstream PR [#3144](https://github.com/adventuregamestudio/ags/pull/3144) also fixes the Windows editor the same way. | `spriteimport` and the Windows editor: magenta ("magic pink") and zero-alpha pixels in 32-bit images imported into 16-bit games become visible. |
| `0007-Engine-editor-debugging-on-non-Windows-platforms.patch` | The editor debugger only existed on Windows (named pipes); elsewhere `--enabledebugger` quit and breaking/error reporting were compiled out, although `FileBasedAGSDebugger` existed. | No debugging (breakpoints, stepping, error breaks) outside Windows. Now `--enabledebugger <token> [<pipe\|file>[=<config>]]` selects the transport (default: pipes on Windows, files elsewhere, the syntax suggested in the PR review); the file-based debugger uses `dbgsend_<token>.tmp` / `dbgrecv_<token>.tmp` in the folder from `file=dir:<folder>`, with atomic writes. Maintainer answer in [#3159](https://github.com/adventuregamestudio/ags/issues/3159): file-based is fine for now, a command line switch at least; pipe/socket on all platforms preferred long term. Upstream PR [#3162](https://github.com/adventuregamestudio/ags/pull/3162). |
| `0009-Common-fix-CopyPixelsRegion-with-a-vertical-offset.patch` | `PixelOp::CopyPixelsRegion()` passes the full byte offset (row × pitch + column × bpp) to `Memory::BlockCopy()`, which expects the offset within a row and copies nothing when it is ≥ the pitch. | Every region with a y offset > 0 comes out empty: `spriteimport` tiles below the first row (sprite sheets, cropped sprites reimported from source) become fully transparent; the engine's DrawingSurface pixel array copies at a y position are empty. Upstream PR [#3164](https://github.com/adventuregamestudio/ags/pull/3164) (regression from `8b34d19e5`, master only). |
| `0010-agsbuild-deep-copy-task-strings-thread-race.patch` | `AGS::Common::String` shares buffers with a non-thread-safe reference count; `TaskPipedProcess` and `TaskDeleteFiles` kept shallow copies, so the tool paths shared by many tasks (agscc for every script) were copied and released concurrently on the worker threads. Upstream PR [#3163](https://github.com/adventuregamestudio/ags/pull/3163). | `agsbuild` sporadically crashed with SIGSEGV in `~Task` / `~TaskManager` after the build (exit code 139; macOS crash reports in ~/Library/Logs/DiagnosticReports). Showed up as rare test failures. |

The patches apply to the upstream targets `common`, `libtools`, `engine` and `agsbuild` inside `native/CMakeLists.txt`.
`libtools` compiles its own copy of `Common/gfx/bitmapdata.cpp`, so 0009 is applied to it as well;
`scripts/build-upstream-tools.sh` builds the tools and the engine from that same patched project.

## Dropped (merged upstream)

0001–0004 ([#3140](https://github.com/adventuregamestudio/ags/pull/3140)–[#3143](https://github.com/adventuregamestudio/ags/pull/3143)),
0006 ([#3145](https://github.com/adventuregamestudio/ags/pull/3145)) and 0008 ([#3160](https://github.com/adventuregamestudio/ags/pull/3160))
are in upstream `master` since `edf5bb991`; the numbers are not reused.

## Noted, not patched

- `LoadPNG()` (`Common/gfx/image_png.cpp`), truecolor path without channel swap: `memcpy(..., width * height)`
  copies only one byte per pixel. Only reached when Allegro's 32-bit RGB shifts equal PNG order
  (R=0, G=8, B=16 — Allegro's compile-time defaults). The engine and the editor always set B,G,R
  shifts first, so the path isn't hit there; command-line tools that never set shifts would be affected.
- 24-bit `SavePNG()` and `CopySwapRGBA` assume tightly packed rows (they ignore `BitmapData::GetStride()`).
  Native AGS bitmaps are always packed, so it's harmless upstream; libagsnative repacks rows before saving.
