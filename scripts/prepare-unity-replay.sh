#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
input="${1:-$root/artifacts/sample-42.json}"
if [[ ! -f "$input" ]]; then
  echo "ERROR: Replay input not found: $input. Generate it with the existing CLI match command first." >&2
  exit 2
fi
input="$(realpath "$input")"
destination="$root/unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/sample-42.json"
dotnet="${DOTNET:-$root/.tools/dotnet/dotnet}"
command -v "$dotnet" >/dev/null || { echo 'ERROR: Set DOTNET to an installed dotnet executable.' >&2; exit 2; }
cd "$root"
"$dotnet" build tests/TennisSim.ViewerChecks --nologo
# The bundled fixture is deliberately seed 42; arbitrary valid files can be loaded in the HUD.
"$dotnet" run --project tests/TennisSim.ViewerChecks --no-build -- "$input"
mkdir -p "$(dirname "$destination")"
if [[ "$input" != "$destination" ]]; then cp -- "$input" "$destination"; fi
source_hash="$(sha256sum "$input" | cut -d ' ' -f 1)"
copy_hash="$(sha256sum "$destination" | cut -d ' ' -f 1)"
[[ "$source_hash" == "$copy_hash" ]] || { echo 'ERROR: Replay copy hash mismatch' >&2; exit 1; }
printf 'REPLAY_INPUT=%s\nVIEWER_REPLAY_FILE=%s\nSHA256=%s\nHASH_IDENTICAL=true\n' "$input" "$destination" "$copy_hash"
