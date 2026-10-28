#!/bin/sh
# AGS4MAC_SCREENSHOTS=1 scripts/test-all.sh also updates the screenshots in docs/screenshots.
# Builds once (no MSBuild node reuse: reused idle nodes can deadlock `dotnet run`) and runs every test project.
set -e
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
cd "$(dirname "$0")/.."
for t in tests/*/; do
  dotnet build "$t" -v q -nodeReuse:false 2>&1 | grep -E " error |Fehler" | grep -v " 0 Fehler" || true
done
status=0
for t in tests/*/; do
  # summary line, and for failures the message and the first stack lines
  (cd "$t" && dotnet run --no-build 2>&1 | grep -E -A6 "Total:|\[FAIL\]" | grep -vE "^\s*(\x1b\[[0-9;]*m)*\s*$|^--$") || status=1
done
exit $status
