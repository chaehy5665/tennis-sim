#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/unity/TennisSim.UnityViewer"
mode="${1:-all}"
case "$mode" in prepare|compile|EditMode|PlayMode|all) ;; *) echo 'Usage: validate-unity-viewer.sh prepare|compile|EditMode|PlayMode|all' >&2; exit 2;; esac
command -v python3 >/dev/null || { echo "ERROR: Python 3 is required." >&2; exit 2; }
mkdir -p "$root/artifacts/unity-validation"
output="$(mktemp -d "$root/artifacts/unity-validation/editor-$(date -u +%Y%m%dT%H%M%S)-XXXXXX")"
printf 'EVIDENCE=%s\n' "$output"
if [[ -z "${UNITY_EDITOR:-}" || ! -x "$UNITY_EDITOR" ]]; then
  echo 'BLOCKED: Set UNITY_EDITOR to a real licensed Unity Editor executable. No Editor or package version has been guessed.' | tee "$output/blocked.txt" >&2
  exit 2
fi
printf '%q -version\n' "$UNITY_EDITOR" >> "$output/commands.txt"
"$UNITY_EDITOR" -version > "$output/editor-version.txt" 2>&1
run_editor() {
  local phase="$1"; shift
  printf '%q ' "$UNITY_EDITOR" "$@" -logFile "$output/$phase.log" >> "$output/commands.txt"
  printf '\n' >> "$output/commands.txt"
  local status=0
  "$UNITY_EDITOR" "$@" -logFile "$output/$phase.log" || status=$?
  printf '%s exit=%s\n' "$phase" "$status" | tee -a "$output/exit-codes.txt"
  [[ "$status" == 0 ]] || return "$status"
}
# Do not hand-write ProjectVersion or package versions. Let the actual Editor create them.
if [[ "$mode" == prepare || "$mode" == all ]]; then
  if [[ ! -f "$project/ProjectSettings/ProjectVersion.txt" ]]; then
    bootstrap="$output/BootstrapProject"
    run_editor prepare -batchmode -nographics -quit -createProject "$bootstrap"
    [[ -f "$bootstrap/ProjectSettings/ProjectVersion.txt" && -f "$bootstrap/Packages/manifest.json" ]] || { echo 'ERROR: Editor did not create project settings/packages' >&2; exit 1; }
    [[ ! -e "$project/ProjectSettings" && ! -e "$project/Packages" ]] || { echo 'ERROR: Partial project settings exist; inspect before copying.' >&2; exit 2; }
    cp -R "$bootstrap/ProjectSettings" "$project/ProjectSettings"
    cp -R "$bootstrap/Packages" "$project/Packages"
  fi
fi
[[ -f "$project/ProjectSettings/ProjectVersion.txt" ]] || { echo 'ERROR: Run prepare first.' >&2; exit 2; }
python3 - "$project/ProjectSettings/ProjectVersion.txt" "$output/editor-version.txt" <<'PYVERSION'
import pathlib,re,sys
expected=re.search(r'^m_EditorVersion: (.+)$',pathlib.Path(sys.argv[1]).read_text(),re.M)
actual=pathlib.Path(sys.argv[2]).read_text()
if not expected or not re.search(r'(?<![\w.])'+re.escape(expected[1])+r'(?![\w.])',actual):
 sys.exit('BLOCKED: Selected Editor does not match ProjectVersion.txt; use the recorded version.')
print('UNITY_EDITOR_VERSION='+expected[1])
PYVERSION
cp "$project/ProjectSettings/ProjectVersion.txt" "$output/ProjectVersion.txt"
[[ -f "$project/Packages/manifest.json" ]] && cp "$project/Packages/manifest.json" "$output/manifest.json"
# Test Framework selection belongs to the actual Editor's Package Manager; no speculative version.
python3 - "$project/Packages/manifest.json" <<'PY'
import json,sys
p=json.load(open(sys.argv[1]))
if 'com.unity.test-framework' not in p.get('dependencies',{}):
 sys.exit('BLOCKED: Open this project with the selected Editor and add its compatible Test Framework via Package Manager, then rerun. No package version was guessed.')
print('TEST_FRAMEWORK_REQUESTED='+p['dependencies']['com.unity.test-framework'])
PY
[[ "$mode" != prepare ]] || exit 0
[[ -f "$project/Assets/StreamingAssets/Replays/sample-42.json" ]] || { echo 'ERROR: Run prepare-unity-replay.sh first.' >&2; exit 2; }
python3 -c 'import hashlib,pathlib,sys; p=pathlib.Path(sys.argv[1]); print(hashlib.sha256(p.read_bytes()).hexdigest(),p)' "$project/Assets/StreamingAssets/Replays/sample-42.json" > "$output/replay.sha256"
if [[ "$mode" == compile || "$mode" == all ]]; then
  run_editor compile -batchmode -nographics -quit -projectPath "$project" -executeMethod TennisSim.Viewer.Editor.ReplaySceneSetup.Setup
  python3 -c 'import pathlib,sys; sys.exit(0 if "TENNISSIM_SCENE_READY" in pathlib.Path(sys.argv[1]).read_text() else 1)' "$output/compile.log" || { echo 'ERROR: Scene setup/compilation completion marker missing' >&2; exit 1; }
  [[ "$mode" != compile ]] || exit 0
fi
for platform in EditMode PlayMode; do
  [[ "$mode" == all || "$mode" == "$platform" ]] || continue
  # Test runner owns termination; NEVER combine -runTests with -quit.
  run_editor "$platform" -batchmode -nographics -projectPath "$project" -runTests -testPlatform "$platform" -assemblyNames "TennisSim.Viewer.$platform" -testResults "$output/$platform.xml"
  python3 "$root/scripts/check-unity-test-results.py" "$output/$platform.xml"
done
[[ ! -f "$project/Packages/packages-lock.json" ]] || cp "$project/Packages/packages-lock.json" "$output/packages-lock.json"
echo 'Headless checks finished. This does not establish UNITY_VISUAL_VERIFIED.'
