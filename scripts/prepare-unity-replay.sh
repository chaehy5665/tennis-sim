#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
input="${1:-$root/artifacts/sample-42.json}"
if [[ ! -f "$input" ]]; then
  echo "ERROR: Replay input not found: $input. Generate it with the existing CLI match command first." >&2
  exit 2
fi
command -v python3 >/dev/null || { echo "ERROR: Python 3 is required." >&2; exit 2; }
input="$(python3 -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).resolve())' "$input")"
name="${2:-sample-42.json}"
[[ "$name" == *.json && "$name" != */* && "$name" != .* ]] || { echo "ERROR: destination must be a plain .json filename" >&2; exit 2; }
destination="$root/unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/$name"
dotnet="${DOTNET:-$(command -v dotnet || true)}"
if [[ -z "$dotnet" && "$(uname -s)" == Linux ]]; then dotnet="$root/.tools/dotnet/dotnet"; fi
command -v "$dotnet" >/dev/null || { echo 'ERROR: Set DOTNET to an installed dotnet executable.' >&2; exit 2; }
cd "$root"
"$dotnet" build tests/TennisSim.ViewerChecks --nologo
# The bundled fixture is deliberately seed 42; arbitrary valid files can be loaded in the HUD.
if [[ "$name" == sample-42.json ]]; then
  "$dotnet" run --project tests/TennisSim.ViewerChecks --no-build -- "$input"
else
  "$dotnet" run --project tests/TennisSim.ViewerChecks --no-build -- "$input" --general
fi
mkdir -p "$(dirname "$destination")"
python3 - "$input" "$destination" <<'PYHASH'
import hashlib,pathlib,shutil,sys
source,dest=map(pathlib.Path,sys.argv[1:])
hashof=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
if dest.exists() and hashof(source)!=hashof(dest):
 sys.exit('ERROR: destination exists with different bytes; choose a new filename')
if source!=dest and not dest.exists(): shutil.copyfile(source,dest)
assert hashof(source)==hashof(dest)
print('REPLAY_INPUT='+str(source)+'\nVIEWER_REPLAY_FILE='+str(dest)+'\nSHA256='+hashof(dest)+'\nHASH_IDENTICAL=true')
PYHASH
