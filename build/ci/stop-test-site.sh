#!/usr/bin/env bash
# Stops the test site started by start-test-site.sh (the PID in PV_STATE_DIR/test-site.pid): SIGTERM for a graceful
# shutdown that flushes the logs, SIGKILL after 30 seconds. Does nothing when no site was started.
#
# A PID file can outlive its process (after a crash or a reboot) while the operating system hands the number to another
# process, so the PID is only signalled while it still runs the test site (see is_test_site_process in common.sh).
#
#   bash build/ci/stop-test-site.sh
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

pid_file=$PV_STATE_DIR/test-site.pid

if [ ! -f "$pid_file" ]; then
	echo "No $pid_file: no test site to stop"
	exit 0
fi

pid=$(cat "$pid_file")
if ! [[ $pid =~ ^[0-9]+$ ]]; then
	echo "$pid_file does not hold a PID ('$pid'); removing it"
elif ! kill -0 "$pid" 2> /dev/null; then
	echo "The test site (PID $pid) is not running any more"
elif ! is_test_site_process "$pid"; then
	echo "PID $pid is not the test site any more (a stale $pid_file); leaving that process alone"
else
	echo "Stopping the test site (PID $pid)"
	kill "$pid" 2> /dev/null || true
	for _ in $(seq 1 30); do
		kill -0 "$pid" 2> /dev/null || break
		sleep 1
	done
	if kill -0 "$pid" 2> /dev/null && is_test_site_process "$pid"; then
		echo "Still running after 30 seconds, killing it"
		kill -9 "$pid" 2> /dev/null || true
	fi
fi
rm -f "$pid_file"
