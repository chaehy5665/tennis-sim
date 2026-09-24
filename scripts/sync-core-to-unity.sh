#!/usr/bin/env bash
# Builds TennisSim.Core (netstandard2.1) and copies the DLL into the Unity project for the coach UI.
# The copy is generated and gitignored: Core stays the single source. Run after every Core change.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${DOTNET:-}"
if [[ -z "$dotnet" ]]; then
  if [[ -x "$root/.tools/dotnet/dotnet" ]]; then dotnet="$root/.tools/dotnet/dotnet"; else dotnet="$(command -v dotnet || true)"; fi
fi
[[ -n "$dotnet" ]] || { echo "ERROR: dotnet not found; set DOTNET or put dotnet on PATH." >&2; exit 2; }
out="$root/artifacts/unity-core"
"$dotnet" build "$root/src/TennisSim.Core/TennisSim.Core.csproj" -c Release --nologo -o "$out" >/dev/null
target="$root/unity/TennisSim.UnityViewer/Assets/TennisSim/Coach/Plugins"
mkdir -p "$target"
cp "$out/TennisSim.Core.dll" "$target/TennisSim.Core.dll"
echo "CORE_DLL=$target/TennisSim.Core.dll sha256=$(python3 -c 'import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],"rb").read()).hexdigest())' "$target/TennisSim.Core.dll")"
