#!/bin/zsh
# Builds the AGS engine and command-line tools natively (arm64) with the ags4mac fixes for upstream
# bugs applied (native/patches, via native/CMakeLists.txt), and assembles build/agstools/: the "system
# dir" that agsbuild expects (all tools side by side, plus the script API header agsdefns.sh).
set -euo pipefail

ROOT=${0:A:h:h}
BUILD=$ROOT/build/native-cmake
TOOLS=$ROOT/build/agstools
TARGETS=(agsbuild agf2dta agfexport agf2dlgasc crmpak spriteimport spritepak agspak trac agscc ags)

cmake -S "$ROOT/native" -B "$BUILD" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_LIBRARY_OUTPUT_DIRECTORY="$ROOT/build/native"
cmake --build "$BUILD" --target $TARGETS -j"$(sysctl -n hw.ncpu)"

rm -rf "$TOOLS"
mkdir -p "$TOOLS"
for t in agsbuild agf2dta agfexport agf2dlgasc crmpak spriteimport spritepak agspak trac; do
    ln -s "$BUILD/upstream/Tools/$t" "$TOOLS/$t"
done
ln -s "$BUILD/upstream/Compiler/agscc" "$TOOLS/agscc"
ln -s "$BUILD/ags" "$TOOLS/ags"
cp "$ROOT/ags/Editor/AGS.Editor/Resources/agsdefns.sh" "$TOOLS/"

echo "Tools ready in $TOOLS"
echo "Build a game:  $TOOLS/agsbuild <Game.agf> <out-dir>"
echo "Run a game:    $TOOLS/ags --windowed <absolute/path/Game.ags>"
