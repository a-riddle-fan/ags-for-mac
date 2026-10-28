# Phase 1 – Class Library Isolation Plan

Source analysed: `ags/` (upstream AGS, branch `master` @ `b99b05272`, version 3.6.4 dev).

## 1. What the upstream editor actually consists of

| Project | Files | Uses WinForms | Target | Portability verdict |
|---|---|---|---|---|
| `AGS.Types` | 255 | 19 | .NET Fx 4.6 | **Mostly portable.** WinForms only in `PropertyGridExtras/*UIEditor*`, `EditorFeatures/*`, 3 interfaces, `BaseFolderCollection.cs`. |
| `AGS.CScript.Compiler` | 53 | 0 | .NET Fx 4.6 | **Portable as-is** (legacy C# compiler; the editor uses the native one, see below). |
| `AGS.Controls` | 103 | 24 | .NET Fx 4.6 | **Not portable** – WinForms controls; rewrite in Avalonia (Phase 2/3). |
| `AGS.Editor` | 333 | 225 | WinExe 4.6 | **Mixed.** Business logic (`DataFileWriter`, `Tasks`, `AGSEditor`, `ImportExport`, `BuildTargets`, component logic) is entangled with UI and must be split. |
| `AGS.Native` | C++/CLI | – | Windows-only | **Blocker.** Mixed-mode C++/CLI cannot run on macOS/CoreCLR. Must be replaced. |
| `AGS.Editor.Tests` | 11 | 1 | NUnit | Port alongside. |

Other facts that drive the design:

* `NativeProxy` (`AGS.Editor/NativeProxy.cs`) is the single C#→native seam. ~40 distinct calls: sprite-file I/O (`acsprset.spr`), room load/save (`.crm`), fonts (TTF/WFN/SCI), mask drawing/undo, palette, template import, `SaveGame`, `GetNativeConstant` (128 call sites), `RenderBufferToHDC` (Win32 GDI).
* The script compiler that the editor uses is the **native** one (`AGS.Native/ScriptCompiler.cpp` → `Compiler/` C++), surfaced as `AGS.Native.IScriptCompiler`.
* `System.Drawing` is used in ~290 files. `Color/Point/Size/Rectangle` are cross-platform (`System.Drawing.Primitives`), but `Bitmap/Graphics` (`System.Drawing.Common`) are **Windows-only since .NET 6**.
* `Common/` (C++) already builds on macOS via CMake for the engine; it holds the authoritative readers/writers for sprites, rooms, fonts and game data. Reusing it is the safest route to byte-exact compatibility.

### Finding from step 1.1: upstream already has a native headless build pipeline

`ags/Tools` builds cleanly on macOS arm64 and contains `agsbuild` ("AGS game project build tool": `agsbuild <Game.agf> <out-dir>`), which drives `agfexport`, `agf2dta`, `agf2dlgasc`, `agscc`, `crmpak`, `spriteimport`, `spritepak`, `agspak` and `trac`. That means the full **game build** (`.agf` → compiled scripts → game data package) already works on macOS without the Windows editor.

Consequences for the plan:
* Game builds in `AGS.Editor.Core` call these upstream tools as subprocesses (`IBuildToolchain`). This avoids re-implementing `DataFileWriter`/`SaveGame`, and output matches what upstream produces.
* `libagsnative.dylib` (step 1.5) shrinks to what the **interactive** editor needs: sprite decode/encode for display and import, room background/mask read/write/paint, font rendering, constants. The script compiler is still useful in-process for fast syntax checks, but it's no longer on the build path.
* Golden-file comparison (step 1.7) is done against `agsbuild` output first, then against the Windows editor.

## 2. Key decisions

1. **Runtime: `net10.0` (LTS)** instead of .NET 8/9. .NET 8 goes out of support Nov 2026, .NET 9 the same month; .NET 10 is the current LTS.
2. **TFM: plain `net10.0` + RID `osx-arm64`**, not `net8.0-macos`. The `-macos` TFM is the Apple-bindings workload (AppKit), which Avalonia does not need and which complicates the build. Avalonia + its native macOS backend run on plain `net10.0`.
3. **Native layer: C-ABI shim `libagsnative.dylib`** built with CMake from `Common/` + `Compiler/` + a new thin `extern "C"` façade, consumed by C# via `[LibraryImport]`. This replaces C++/CLI while reusing upstream's file-format code verbatim (compat > elegance). A pure-C# port of individual formats can come later where it's cheap.
4. **Imaging:** a managed `PixelImage`/`IImage` abstraction replaces `System.Drawing.Bitmap`. Decoding uses upstream's own code: native `Common/gfx/image_file` for BMP/PCX/PNG (keeps palette indices) and Magick.NET for GIF/JPEG/TIFF, as the upstream editor does. *(Revised in step 1.4: SkiaSharp is not used for decoding; Avalonia's Skia rendering covers drawing.)*
5. **Source strategy: link, don't copy.** New projects live in `src/` and compile upstream files via `<Compile Include="..\..\ags\Editor\AGS.Types\**\*.cs" Exclude="…">`. Files that must change get copied into `src/` with a `// ags4mac: ported from <path>` header. This keeps upstream merges tractable; every forked file is listed in `docs/FORKED_FILES.md`.
6. **PropertyGrid metadata:** keep `[Category]`, `[Description]`, `[Browsable]`, `[TypeConverter]` (all cross-platform in `System.ComponentModel`). `[Editor(typeof(XyzUIEditor), typeof(UITypeEditor))]` attributes are rewritten to string type names (`[Editor("AGS.Types.ViewUIEditor", "…UITypeEditor")]`), which compile without WinForms; Phase 2 maps those names to Avalonia editors.

## 3. Target project layout

```
ags4mac/
├─ ags/                        upstream clone (untouched where possible)
├─ native/                     CMake: libagsnative.dylib (arm64)
│  ├─ CMakeLists.txt           pulls ../ags/Common, ../ags/Compiler
│  └─ shim/agsnative_c.{h,cpp} extern "C" façade (opaque handles, RGBA buffers, UTF-8 strings)
├─ src/
│  ├─ AGS.Types.Core/          net10.0 – data model + .agf XML serialization
│  ├─ AGS.Imaging/             net10.0 – SkiaSharp-backed bitmap abstraction
│  ├─ AGS.Native.Interop/      net10.0 – P/Invoke to libagsnative + managed NativeProxy replacement
│  ├─ AGS.CScript.Compiler/    net10.0 – linked as-is (legacy, kept for parity/tests)
│  ├─ AGS.Editor.Core/         net10.0 – UI-free editor logic: load/save/build/tasks
│  ├─ AGS.Editor.Cli/          net10.0 – headless `ags-build <Game.agf>` (Phase 1 acceptance harness)
│  └─ AGS.Editor.Avalonia/     Phase 2
├─ tests/
│  └─ AGS.Editor.Core.Tests/   xUnit; round-trip + golden-file tests
└─ AGS4Mac.sln, Directory.Build.props (net10.0, osx-arm64, nullable off for linked code)
```

## 4. Step-by-step

### 1.1 Toolchain
* Install .NET 10 SDK (arm64) and CMake (Homebrew or official pkgs). Xcode CLT is already present.
* Verify `ags/` CMake engine build works on this Mac (`cmake -S ags -B build/engine -DCMAKE_OSX_ARCHITECTURES=arm64`) – proves `Common/` compiles natively.

### 1.2 Solution skeleton
* `AGS4Mac.sln`, `Directory.Build.props` (`TargetFramework=net10.0`, `RuntimeIdentifier=osx-arm64`, `LangVersion=latest`, `TreatWarningsAsErrors=false` for linked upstream code).
* Empty projects from §3 with project references: `Types.Core ← Imaging ← Native.Interop ← Editor.Core ← Editor.Cli`.

### 1.3 `AGS.Types.Core`
* Link all of `ags/Editor/AGS.Types/**/*.cs`, excluding:
  * `PropertyGridExtras/*UIEditor*.cs`, `StringListUIEditorControl*.cs`, `PropertyTabEvents.cs`, `PropertyTabInteractions.cs` → moved to Phase 2 (Avalonia property editors).
  * `EditorFeatures/EditorContentPanel.cs`, `ContentDocument.cs`, `MenuCommand.cs` → forked: replace `Control`/`Keys`/`Image` with UI-neutral types (`object View`, own `KeyGesture` record, `IImage`).
  * `Interfaces/IScriptEditorControl.cs`, `IEditorComponent.cs`, `Docking/IDockingContainer.cs` → forked with neutral types.
  * `BaseFolderCollection.cs` → forked (remove `TreeNode` usage).
* Rewrite `[Editor(typeof(...))]` to string form in the forked files; for linked files use a Roslyn-free trick: add stub types `System.Drawing.Design.UITypeEditor` + the `*UIEditor` names as empty classes in a `Compat/` folder so attributes still compile. (Cheaper than forking ~40 files; decide per file during 1.3.)
* Replace `System.Drawing.Bitmap/Image` members with `AGS.Imaging.IImage` in forked files only.
* **Done when:** project builds on macOS arm64 with zero WinForms references.

### 1.4 `AGS.Imaging`
* `IImage` (width, height, pixel format, `ReadPixels`/`WritePixels`, `Save(png|bmp)`, `Load`), 8-bit-indexed + palette support (AGS needs paletted sprites and masks), backed by `SKBitmap`.
* BMP/PNG/GIF import parity with editor (`GIFLoader/` is pure C# – link it).

### 1.5 Native shim `libagsnative.dylib`
* Inventory every `NativeProxy` method → C signature table in `native/shim/API.md`.
* Implement in groups, each with a C# wrapper in `AGS.Native.Interop` exposing the **same method names** as `NativeProxy` so `Editor.Core` code stays near-identical:
  1. constants (`GetNativeConstant`), version
  2. sprite file read/write/index (`acsprset.spr`, `sprindex.dat`)
  3. fonts (TTF via FreeType already in `libsrc`, WFN, SCI import)
  4. rooms: load/save `.crm`, backgrounds, masks, `DrawLineOntoMask`, undo buffers
  5. game data: `SaveGame` / data file writing pieces still native
  6. script compiler (`Compiler/` + preprocessor) → `ags_compile_script(...)` returning bytecode + errors
* `RenderBufferToHDC`, `UpdateFileIcon`, `UpdateFileVersionInfo` are Windows-only: drop or replace (render → RGBA buffer handed to Skia in Phase 3; icon/version → no-op on macOS, handled by Mac build target).
* **Done when:** a C# xUnit test loads a sample game's sprites + one room through the shim and gets identical pixel data to a Windows-exported reference.

### 1.6 `AGS.Editor.Core` extraction
* Triage all 333 `AGS.Editor` files into **Core / UI / Split** (output: `docs/EDITOR_FILE_TRIAGE.md`). Expected Core: `DataFileWriter`, `Tasks`, `ImportExport`, `AGSEditor` (build pipeline), `BuildTargets/*` (minus Windows-specific signing/resources), `Utils/*`, `TextProcessing/*`, `Entities/*`, `Config/*`.
* For *Split* files (components with logic + UI), extract logic into Core services; leave a UI stub for Phase 2.
* Replace UI callbacks (`MessageBox`, `Factory.GUIController`) with an `IEditorHost` interface (messages, confirmations, progress, output log) – CLI implements it with console, Avalonia implements it in Phase 2.
* Replace Windows paths/registry (`Registry`, `%APPDATA%`, `\\`) with `Path.Combine` + `~/Library/Application Support/AGS`.

### 1.7 Acceptance harness `AGS.Editor.Cli`
* `ags-build <path/Game.agf> [--target=mac|data]`: load → compile scripts → write `game28.dta`/`.ags` → exit code.
* Golden tests using a sample game (e.g. upstream template "Sierra-style" / "BASS"):
  * `.agf` load → save produces semantically identical XML.
  * Compiled data files byte-identical to Windows editor output of the same version (references generated once on Windows/CI, stored in `tests/golden/`).

## 5. Risks

* **Exact binary compatibility** depends on reusing `Common/`; any pure-C# re-implementation must be golden-tested.
* **Upstream drift:** linking sources minimises fork surface; keep forked-file list small.
* **`GetNativeConstant` (128 sites)** – trivial but must match the native build flags exactly.
* **Plugins:** existing editor plugins are .NET Framework WinForms DLLs → not loadable on macOS. Out of scope; document.
* **Windows build target** (exe + icon/version patching) cannot be produced on macOS without a resource editor – evaluate later (Phase 4).
