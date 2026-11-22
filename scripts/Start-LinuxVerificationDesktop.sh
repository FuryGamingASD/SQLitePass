#!/usr/bin/env bash
set -euo pipefail

# Xvfb supplies an X server. Openbox handles the real minimize and restore requests.
openbox &
window_manager=$!
/usr/bin/python3 "$SQLITEPASS_LOGIN_SESSION_PROBE" &
login_service=$!
trap 'kill "$window_manager" "$login_service" 2>/dev/null || true' EXIT

for ((attempt = 0; attempt < 50; attempt++)); do
    if xprop -root _NET_SUPPORTING_WM_CHECK | grep -Eq 'window id # 0x[0-9a-f]+'; then
        setxkbmap -layout it
        # The private bus also provides the Xfce screen saver for the session lock check.
        DBUS_SYSTEM_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS" XDG_SESSION_ID=sqlitepass-verification XDG_CURRENT_DESKTOP=XFCE "$@"
        exit
    fi
    sleep 0.1
done

echo 'Openbox did not become ready on the isolated display.' >&2
exit 1
