#!/usr/bin/env bash
set -euo pipefail

runtime=$(mktemp -d)
export XDG_RUNTIME_DIR="$runtime"
export XDG_CURRENT_DESKTOP=sway
export XDG_SESSION_TYPE=wayland
export WLR_BACKENDS=headless
export WLR_HEADLESS_OUTPUTS=1
export WLR_LIBINPUT_NO_DEVICES=1
export WLR_RENDERER=pixman
export LIBGL_ALWAYS_SOFTWARE=1
export DBUS_SYSTEM_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS"
export SQLITEPASS_VERIFY_WAYLAND_CLIPBOARD=1
export SQLITEPASS_WAYLAND_DESKTOP_FILE="$runtime/display.env"
unset DISPLAY WAYLAND_DISPLAY SWAYSOCK

cat > "$runtime/sway.conf" <<'CONFIG'
xwayland force
swaynag_command -
output * resolution 1280x800
exec sh -c 'printf "%s\n%s\n" "$DISPLAY" "$WAYLAND_DISPLAY" > "$SQLITEPASS_WAYLAND_DESKTOP_FILE"'
CONFIG

sway --config "$runtime/sway.conf" --debug > "$runtime/sway.log" 2>&1 &
compositor=$!
cleanup() {
    result=$?
    kill "$compositor" 2>/dev/null || true
    wait "$compositor" 2>/dev/null || true
    if ((result != 0)); then tail -n 45 "$runtime/sway.log" >&2; fi
}
trap cleanup EXIT

for ((attempt = 0; attempt < 100; attempt++)); do
    kill -0 "$compositor" 2>/dev/null || break
    if [[ -s "$SQLITEPASS_WAYLAND_DESKTOP_FILE" ]]; then
        mapfile -t desktop < "$SQLITEPASS_WAYLAND_DESKTOP_FILE"
        if ((${#desktop[@]} == 2)) && [[ ${desktop[0]} =~ ^:[0-9]+$ && -S "$runtime/${desktop[1]}" ]]; then
            export DISPLAY=${desktop[0]} WAYLAND_DISPLAY=${desktop[1]}
            if xdpyinfo >/dev/null 2>&1; then
                "$@"
                exit
            fi
        fi
    fi
    sleep 0.1
done

echo 'The isolated Sway XWayland display did not become ready.' >&2
exit 1
