#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
    echo "Usage: bash Verify-AvaloniaLinuxSessionLock.sh <app-executable> <new-report-directory>" >&2
    exit 2
fi
app=$(realpath "$1")
report_dir=$(realpath -m "$2")
if [[ -n ${DBUS_SYSTEM_BUS_ADDRESS:-} ]]; then
    echo 'Use the host system D-Bus for this check.' >&2
    exit 2
fi

service=org.freedesktop.login1
session_iface=$service.Session
session_path=$(busctl --system call "$service" /org/freedesktop/login1 "$service.Manager" GetSessionByPID u "$$" | cut -d '"' -f 2)
if [[ ! $session_path =~ ^/org/freedesktop/login1/session/[[:alnum:]_]+$ ]]; then
    echo 'This process must run inside a logind-managed SSH session.' >&2
    exit 2
fi
session_id=$(busctl --system get-property "$service" "$session_path" "$session_iface" Id | cut -d '"' -f 2)
if [[ $(loginctl show-session "$session_id" -p Remote --value) != yes ||
      $(loginctl show-session "$session_id" -p Service --value) != sshd ||
      $(loginctl show-session "$session_id" -p Name --value) != "$(id -un)" ||
      $(loginctl show-session "$session_id" -p LockedHint --value) != no ]]; then
    echo 'The check needs its own unlocked SSH session.' >&2
    exit 2
fi

mkdir -- "$report_dir"
export XDG_SESSION_ID="$session_id"
set_hint() {
    busctl --system call "$service" "$session_path" "$session_iface" SetLockedHint b "$1"
}
probe_pid=
hint_changed=0
cleanup() {
    if (( hint_changed )); then set_hint false || true; fi
    if [[ -n $probe_pid ]]; then
        kill "$probe_pid" 2>/dev/null || true
        wait "$probe_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT

xvfb-run -a dbus-run-session -- "$app" --verify-linux-session-lock "$report_dir/session-lock.json" > "$report_dir/probe.log" 2>&1 &
probe_pid=$!

wait_for_probe() {
    local expected=$1 attempt
    for (( attempt = 0; attempt < 160; attempt++ )); do
        if grep -Fqx -- "$expected" "$report_dir/probe.log"; then return; fi
        kill -0 "$probe_pid" 2>/dev/null || break
        sleep 0.1
    done
    cat "$report_dir/probe.log" >&2
    echo "Timed out waiting for the probe message '$expected'." >&2
    return 1
}

wait_for_probe 'SQLitePass dialog probe ready.'
hint_changed=1
set_hint true
wait_for_probe 'SQLitePass observed the dialog lock.'
set_hint false
hint_changed=0
wait_for_probe 'SQLitePass observed the dialog unlock.'
wait_for_probe 'SQLitePass operation probe ready.'
hint_changed=1
set_hint true
wait_for_probe 'SQLitePass observed the operation lock.'
set_hint false
hint_changed=0
wait_for_probe 'SQLitePass observed the operation unlock.'
wait "$probe_pid"
probe_pid=
cat "$report_dir/session-lock.json"
