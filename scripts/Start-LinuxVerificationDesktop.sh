#!/usr/bin/env bash
set -euo pipefail

# Xvfb supplies an X server. Openbox handles the real minimize and restore requests.
openbox &
window_manager=$!
trap 'kill "$window_manager" 2>/dev/null || true' EXIT

for ((attempt = 0; attempt < 50; attempt++)); do
    if xprop -root _NET_SUPPORTING_WM_CHECK | grep -Eq 'window id # 0x[0-9a-f]+'; then
        setxkbmap -layout it
        "$@"
        exit
    fi
    sleep 0.1
done

echo 'Openbox did not become ready on the isolated display.' >&2
exit 1
