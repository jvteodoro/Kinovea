#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: WINEPREFIX=... KINOVEA_EXE=... $0 <scenario.json> <new-output-directory> [oracle options...]" >&2
  exit 2
fi
if [[ -z "${WINEPREFIX:-}" || ! -d "$WINEPREFIX" ]]; then
  echo "WINEPREFIX must point to the existing verified Wine prefix" >&2
  exit 2
fi

scenario=$(realpath "$1")
output=$(realpath -m "$2")
shift 2
repo_root=$(cd "$(dirname "$0")/../.." && pwd)
executable=${KINOVEA_EXE:-"$WINEPREFIX/drive_c/Program Files/Kinovea/Kinovea.exe"}
executable=$(realpath "$executable")
case "$executable" in
  "$WINEPREFIX"/*) relative_executable=${executable#"$WINEPREFIX"/} ;;
  *) echo "KINOVEA_EXE must be located under WINEPREFIX for copy-on-write isolation" >&2; exit 2 ;;
esac
case "$output" in
  "$WINEPREFIX"/*) echo "output directory must not be inside the original prefix" >&2; exit 2 ;;
esac
if [[ -e "$output" ]]; then
  echo "output directory already exists: $output" >&2
  exit 2
fi

overlay_root=$(mktemp -d "${TMPDIR:-/tmp}/kinovea-wine-oracle.XXXXXXXX")
mkdir -p "$overlay_root"/{upper,work,merged}
set +e
unshare -Urmpf --mount-proc /bin/sh -c '
  set -eu
  lower=$1; overlay=$2; relative=$3; root=$4; scenario=$5; output=$6
  shift 6
  mount --make-rprivate /
  mount -t overlay overlay -o "lowerdir=$lower,upperdir=$overlay/upper,workdir=$overlay/work" "$overlay/merged"
  export WINEPREFIX="$overlay/merged"
  export KINOVEA_ORACLE_OVERLAY=1
  exe="$WINEPREFIX/$relative"
  set +e
  python3 "$root/tools/wine-oracle/oracle.py" run --scenario "$scenario" --executable "$exe" --prefix "$WINEPREFIX" --output "$output" --repo-root "$root" --collect-app-logs "$@"
  scenario_status=$?
  set -e
  timeout 15s wineserver -w || true
  if ! umount "$overlay/merged"; then
    echo "could not unmount copy-on-write Wine prefix; overlay preserved at $overlay" >&2
    exit 70
  fi
  exit "$scenario_status"
' sh "$WINEPREFIX" "$overlay_root" "$relative_executable" "$repo_root" "$scenario" "$output" "$@"
status=$?
set -e
if [[ $status -eq 0 ]]; then
  rm -rf "$overlay_root"
else
  echo "scenario status $status; disposable prefix overlay retained at $overlay_root" >&2
fi
exit "$status"
