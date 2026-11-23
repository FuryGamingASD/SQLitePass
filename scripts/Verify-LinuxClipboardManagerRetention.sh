#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
    echo "Usage: bash Verify-LinuxClipboardManagerRetention.sh <app-executable> <new-report-directory>" >&2
    exit 2
fi

app=$(realpath "$1")
report_dir=$(realpath -m "$2")
if [[ ! -x $app || ! -f $app || -e $report_dir ]]; then
    echo 'The app must exist and the report directory must be new.' >&2
    exit 2
fi
for command in xvfb-run dbus-run-session xfce4-clipman xclip busctl python3; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Missing Linux clipboard test dependency: $command" >&2
        exit 2
    fi
done
if [[ -n ${DBUS_SESSION_BUS_ADDRESS:-} ]]; then
    echo 'Run this check outside an existing D-Bus session.' >&2
    exit 2
fi

mkdir -- "$report_dir"
qa_dir=$(mktemp -d /tmp/sqlitepass-clipman.XXXXXXXX)
if [[ ! $qa_dir =~ ^/tmp/sqlitepass-clipman\.[A-Za-z0-9]{8}$ ]]; then
    echo 'Unexpected temporary QA directory.' >&2
    exit 2
fi
cleanup() {
    if [[ -n ${qa_dir:-} && $qa_dir =~ ^/tmp/sqlitepass-clipman\.[A-Za-z0-9]{8}$ &&
          $(realpath "$qa_dir") == "$qa_dir" && $(stat -c '%U' "$qa_dir") == "$USER" ]]; then
        rm -rf -- "$qa_dir"
    fi
}
trap cleanup EXIT

export HOME="$qa_dir/home"
export XDG_CACHE_HOME="$qa_dir/cache"
export XDG_CONFIG_HOME="$qa_dir/config"
export XDG_DATA_HOME="$qa_dir/data"
export XDG_RUNTIME_DIR="$qa_dir/runtime"
export SQLITEPASS_CLIPMAN_FIXTURE='SQLitePass WinRT protected clipboard fixture Ω'
mkdir -p "$HOME" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"

xvfb-run -a dbus-run-session -- bash -s -- "$app" "$report_dir" <<'INNER'
set -euo pipefail
app=$1
report_dir=$2
manager_log="$report_dir/clipman.log"
probe_log="$report_dir/clipboard-probe.log"
guard_log="$report_dir/clipboard-manager-guard.log"
manager_pid=
probe_pid=
cleanup_inner() {
    if [[ -n ${probe_pid:-} ]] && kill -0 "$probe_pid" 2>/dev/null; then
        kill -TERM "$probe_pid" 2>/dev/null || true
        wait "$probe_pid" 2>/dev/null || true
    fi
    if [[ -n ${manager_pid:-} ]] && kill -0 "$manager_pid" 2>/dev/null; then
        kill -TERM "$manager_pid" 2>/dev/null || true
        wait "$manager_pid" 2>/dev/null || true
    fi
}
trap cleanup_inner EXIT

xfce4-clipman >"$manager_log" 2>&1 &
manager_pid=$!
manager_ready=0
for attempt in $(seq 1 50); do
    if ! kill -0 "$manager_pid" 2>/dev/null; then
        echo 'Clipman exited during startup.' >&2
        cat "$manager_log" >&2
        exit 1
    fi
    if busctl --user --no-pager list 2>/dev/null | grep -Fq 'org.xfce.clipman'; then
        manager_ready=1
        break
    fi
    sleep 0.1
done
if [[ $manager_ready != 1 ]]; then
    echo 'Clipman D-Bus service did not become ready.' >&2
    cat "$manager_log" >&2
    exit 1
fi
manager_selection_owner=$(python3 - <<'PY'
import ctypes
import ctypes.util

x11 = ctypes.CDLL(ctypes.util.find_library("X11"))
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
x11.XInternAtom.restype = ctypes.c_ulong
x11.XGetSelectionOwner.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x11.XGetSelectionOwner.restype = ctypes.c_ulong
display = x11.XOpenDisplay(None)
if not display:
    raise SystemExit("Could not connect to the isolated X server.")
atom = x11.XInternAtom(display, b"CLIPBOARD_MANAGER", 0)
print(x11.XGetSelectionOwner(display, atom))
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
x11.XCloseDisplay(display)
PY
)

RUNNER_ENVIRONMENT=github-hosted "$app" --verify-startup-and-clipboard >"$probe_log" 2>&1 &
probe_pid=$!
probe_finished=0
for attempt in $(seq 1 600); do
    if ! kill -0 "$probe_pid" 2>/dev/null; then
        probe_finished=1
        break
    fi
    sleep 0.1
done
if [[ $probe_finished != 1 ]]; then
    kill -TERM "$probe_pid" 2>/dev/null || true
    wait "$probe_pid" 2>/dev/null || true
    probe_pid=
    probe_exit=124
else
    if wait "$probe_pid"; then probe_exit=0; else probe_exit=$?; fi
    probe_pid=
fi

fixture=$SQLITEPASS_CLIPMAN_FIXTURE
clipboard_after=$(timeout 2s xclip -selection clipboard -out 2>/dev/null || true)
if [[ $clipboard_after == "$fixture" ]]; then clipboard_fixture_after=true; else clipboard_fixture_after=false; fi
if RUNNER_ENVIRONMENT=github-hosted "$app" --verify-linux-clipboard-manager-guard >"$guard_log" 2>&1; then
    guard_exit=0
else
    guard_exit=$?
fi

kill -TERM "$manager_pid" 2>/dev/null || true
wait "$manager_pid" 2>/dev/null || true
manager_pid=
cache="$XDG_CACHE_HOME/xfce4/clipman"
if [[ -d $cache ]] && grep -R -a -F -q -- "$fixture" "$cache"; then
    history_retained=true
else
    history_retained=false
fi
if grep -Fq 'The native clipboard still held text after the deadline.' "$probe_log"; then
    retention_probe_confirmed=true
else
    retention_probe_confirmed=false
fi
if [[ $probe_exit != 0 && $retention_probe_confirmed == true &&
      $clipboard_fixture_after == true && $history_retained == true ]]; then
    retention_demonstrated=true
else
    retention_demonstrated=false
fi
manager_version=$(dpkg-query -W -f='${Version}' xfce4-clipman-plugin)
python3 - "$report_dir/clipboard-manager-result.json" "$manager_version" "$probe_exit" "$clipboard_fixture_after" "$history_retained" "$fixture" "$cache" "$manager_selection_owner" "$guard_exit" "$retention_probe_confirmed" <<'PY'
import json
import sys

path, version, exit_code, clipboard_after, history_retained, fixture, cache, selection_owner, guard_exit, retention_probe_confirmed = sys.argv[1:]
retention_demonstrated = int(exit_code) != 0 and retention_probe_confirmed == "true" and clipboard_after == "true" and history_retained == "true"
manager_guard_passed = int(guard_exit) == 0
result = {
    "Manager": "xfce4-clipman",
    "ManagerVersion": version,
    "ClipboardManagerSelectionOwner": int(selection_owner),
    "Fixture": fixture,
    "NativeClipboardProbeExitCode": int(exit_code),
    "ClipboardRetentionProbeConfirmed": retention_probe_confirmed == "true",
    "FixtureStillOnActiveClipboardAfterProbe": clipboard_after == "true",
    "FixtureFoundInManagerCacheAfterClipboardExpiryAndManagerExit": history_retained == "true",
    "ManagerGuardProbeExitCode": int(guard_exit),
    "ManagerGuardPassed": manager_guard_passed,
    "OverallPassed": retention_demonstrated and manager_guard_passed,
    "ManagerCachePath": cache,
}
with open(path, "w", encoding="utf-8") as handle:
    json.dump(result, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY
cat "$report_dir/clipboard-manager-result.json"
echo 'Clipman log:'
tail -30 "$manager_log"
echo 'Native clipboard probe log:'
tail -40 "$probe_log"
echo 'Clipboard-manager guard log:'
tail -20 "$guard_log"
if [[ $retention_demonstrated != true || $guard_exit != 0 ]]; then
    echo 'The clipboard-manager retention or guard assertion failed.' >&2
    exit 1
fi
INNER
