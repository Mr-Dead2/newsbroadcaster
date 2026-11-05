#!/usr/bin/env bash
# Compile-check NewsBroadcaster.cs, run the functional tests and (optionally) render UI screenshots.
#   ./run.sh            compile (C# 6 + C# 7, .NET Framework 4.8 API) + tests
#   ./run.sh previews   ... and render PNG screenshots to out/png (needs node + playwright)
set -euo pipefail
cd "$(dirname "$0")"

echo "== strict compile (C# 7, net48)"
dotnet build Plugin/Plugin.csproj -f net48 --no-incremental -nologo -clp:ErrorsOnly
echo "== strict compile (C# 6, net48)"
dotnet build Plugin/Plugin.csproj -f net48 --no-incremental -nologo -clp:ErrorsOnly -p:PluginLangVersion=6

echo "== functional tests"
dotnet build Harness/Harness.csproj -nologo -clp:ErrorsOnly -o bin/harness
dotnet bin/harness/Harness.dll tests

if [[ "${1:-}" == "previews" ]]; then
  echo "== screenshots"
  rm -rf out/json out/png
  dotnet bin/harness/Harness.dll previews out/json
  node render/render.js out/json out/png 1 "NewsBroadcaster preview"
  echo "screenshots in $(pwd)/out/png"
fi
