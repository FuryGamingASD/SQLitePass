#!/usr/bin/env bash
set -euo pipefail

image=$1
feed=$2
evidence=$3
probe=${4:---verify-appimage-update}
unset APPIMAGE_EXTRACT_AND_RUN

"$image" "$probe" "$feed" "$evidence"

# Keep the X server and D-Bus session alive after the original app exits.
for ((attempt = 0; attempt < 220; attempt++)); do
    if [[ -s "$evidence/after.json" ]]; then
        exit 0
    fi
    sleep 0.25
done

echo 'The in-place AppImage upgrade did not restart the new app.' >&2
exit 1
