#!/bin/zsh
# Builds and starts the AGS Editor for macOS. Optional argument: path to a Game.agf to open.
# Build/Run inside the editor need the AGS tools (scripts/build-upstream-tools.sh) and rooms need
# libagsnative (scripts/build-native.sh).
set -euo pipefail
ROOT=${0:A:h:h}
dotnet build "$ROOT/src/AGS.Editor.Avalonia" -v q
exec dotnet "$ROOT/src/AGS.Editor.Avalonia/bin/Debug/net10.0/osx-arm64/AGSEditor.dll" "$@"
