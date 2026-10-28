#!/bin/zsh
# Builds libagsnative.dylib (arm64) from native/ into build/native/.
# AGS.Native.Interop copies it next to the .NET assemblies at build time.
set -euo pipefail

ROOT=${0:A:h:h}
BUILD=$ROOT/build/native-cmake

cmake -S "$ROOT/native" -B "$BUILD" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_LIBRARY_OUTPUT_DIRECTORY="$ROOT/build/native"
cmake --build "$BUILD" --target agsnative -j"$(sysctl -n hw.ncpu)"

echo "Built $ROOT/build/native/libagsnative.dylib"
