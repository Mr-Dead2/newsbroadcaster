#!/usr/bin/env bash
# Compile-check NewsBroadcaster.cs, run the functional tests and (optionally) render UI screenshots.
#   ./run.sh            compile (C# 6 + C# 7, .NET Framework 4.8 API) + tests
#   ./run.sh previews   ... and render PNG screenshots to out/png (needs node + playwright)
#   ./run.sh video      ... and also render the animation preview to out/anim (needs ffmpeg)
#   ./run.sh bench      time and memory per screen, save and broadcast (plugin code only, Release build)
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${1:-}" == "bench" ]]; then
  dotnet build Harness/Harness.csproj -c Release -nologo -clp:ErrorsOnly -o bin/bench
  dotnet bin/bench/Harness.dll bench
  exit 0
fi

echo "== strict compile (C# 7, net48)"
dotnet build Plugin/Plugin.csproj -f net48 --no-incremental -nologo -clp:ErrorsOnly
echo "== strict compile (C# 6, net48)"
dotnet build Plugin/Plugin.csproj -f net48 --no-incremental -nologo -clp:ErrorsOnly -p:PluginLangVersion=6

echo "== functional tests"
dotnet build Harness/Harness.csproj -nologo -clp:ErrorsOnly -o bin/harness
dotnet bin/harness/Harness.dll tests

if [[ "${1:-}" == "previews" || "${1:-}" == "video" ]]; then
  echo "== screenshots"
  rm -rf out/json out/png
  dotnet bin/harness/Harness.dll previews out/json
  node render/render.js out/json out/png 1 "NewsBroadcaster preview"
  echo "screenshots in $(pwd)/out/png"
fi

if [[ "${1:-}" == "video" ]]; then
  echo "== animation video"
  mkdir -p out/anim
  dotnet bin/harness/Harness.dll animation out/anim/timeline.json
  node render/anim.js out/anim/timeline.json out/anim/newsbroadcaster-animations.mp4 out/anim/newsbroadcaster-animations.gif
fi
