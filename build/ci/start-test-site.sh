#!/usr/bin/env bash
# Starts the test site (built by build.sh) in the background on a clean SQLite database and waits until the backoffice
# answers 200. The site installs itself unattended and imports the uSync set on this first boot.
#
#   bash build/ci/start-test-site.sh                 https://localhost:44300 and http://localhost:44301
#   PV_PORT=45000 bash build/ci/start-test-site.sh   https://localhost:45000 and http://localhost:45001
#
# Environment (besides common.sh):
#   PV_PORT              https port (default 44300); the http port is PV_HTTP_PORT, default PV_PORT + 1
#   PV_START_TIMEOUT     seconds to wait for the backoffice (default 300)
#   PV_KEEP_DATABASE     true keeps umbraco/Data (default: deleted, so every run starts from the seed)
#   PV_STATE_DIR         where the PID file and the console log go (default artifacts/test-site)
# Stop it with stop-test-site.sh.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

http_port=${PV_HTTP_PORT:-$((PV_PORT + 1))}
timeout=${PV_START_TIMEOUT:-300}
base_url=https://localhost:$PV_PORT
pid_file=$PV_STATE_DIR/test-site.pid
log_file=$PV_STATE_DIR/test-site.log
site_dll=bin/$CONFIGURATION/$TARGET_FRAMEWORK/$TEST_SITE_NAME.dll

[ -f "$TEST_SITE_DIR/$site_dll" ] || fail "$TEST_SITE_DIR/$site_dll not found; run build/ci/build.sh first"

if [ -f "$pid_file" ]; then
	previous_pid=$(cat "$pid_file")
	if [[ $previous_pid =~ ^[0-9]+$ ]] && kill -0 "$previous_pid" 2> /dev/null && is_test_site_process "$previous_pid"; then
		fail "the test site is already running (PID $previous_pid); stop it with build/ci/stop-test-site.sh"
	fi
	echo "Removing the stale $pid_file (PID $previous_pid is not the test site)"
	rm -f "$pid_file"
fi
if curl -k -s -o /dev/null --max-time 5 "$base_url/"; then
	fail "something already answers on $base_url; choose another port with PV_PORT"
fi

group "Test site: Umbraco version"
umbraco=$(test_site_umbraco_version)
echo "The test site is built against Umbraco.Cms.Core ${umbraco:-unknown}"
if [ -n "$UMBRACO_CMS_VERSION" ] && ! same_version "$umbraco" "$UMBRACO_CMS_VERSION"; then
	fail "the test site is built against Umbraco.Cms.Core $umbraco, UMBRACO_CMS_VERSION is $UMBRACO_CMS_VERSION; run build.sh again"
fi
end_group

if [ "${PV_KEEP_DATABASE:-false}" != "true" ]; then
	group "Test site: clean database"
	# Another site on this content root (dotnet run, or this script with another PV_PORT) shares umbraco/Data: deleting
	# it would pull the database from under that site. fuser (Linux) names such a process; on Windows the delete itself
	# fails on the locked SQLite file.
	data_dir=$TEST_SITE_DIR/umbraco/Data
	if [ -d "$data_dir" ] && command -v fuser > /dev/null; then
		holders=$(fuser "$data_dir"/* 2> /dev/null | tr -s ' ' || true)
		if [ -n "${holders// /}" ]; then
			fail "process$holders has $data_dir open (a test site started with dotnet run?); stop it first, a second site needs its own checkout"
		fi
	fi
	run rm -rf "$data_dir" || fail "could not delete $data_dir (a test site started with dotnet run still using it?); stop it first"
	end_group
fi

# Kestrel serves https with the ASP.NET Core development certificate. Linux has none until one is generated; it lives in
# the user's .NET certificate store and does not need to be trusted (curl -k, Playwright ignoreHTTPSErrors).
if [ "$(uname -s)" = "Linux" ]; then
	group "Test site: development certificate"
	if dotnet dev-certs https --check; then
		echo "Reusing the existing ASP.NET Core development certificate"
	else
		run dotnet dev-certs https
	fi
	end_group
fi

group "Test site: start on $base_url"
mkdir -p "$PV_STATE_DIR"
# The content root is the project folder, as with dotnet run: the acceptance suite changes appsettings.json and writes
# the rules file there. The application URL follows the port, so backoffice links point at this instance.
(
	cd "$TEST_SITE_DIR"
	export ASPNETCORE_ENVIRONMENT=Development
	export Umbraco__CMS__WebRouting__UmbracoApplicationUrl=$base_url
	exec nohup dotnet "$site_dll" --urls "https://localhost:$PV_PORT;http://localhost:$http_port"
) > "$log_file" 2>&1 < /dev/null &
pid=$!
echo "$pid" > "$pid_file"
echo "PID $pid, console log $log_file"

started=$SECONDS
deadline=$((SECONDS + timeout))
status=000
while :; do
	status=$(curl -k -s -o /dev/null -w '%{http_code}' --max-time 10 "$base_url/umbraco" || true)
	[ "$status" = "200" ] && break
	if ! kill -0 "$pid" 2> /dev/null; then
		echo "--- last lines of $log_file"
		tail -n 100 "$log_file" || true
		rm -f "$pid_file"
		fail "the test site exited before it answered (last status $status)"
	fi
	if [ "$SECONDS" -ge "$deadline" ]; then
		echo "--- last lines of $log_file"
		tail -n 100 "$log_file" || true
		kill "$pid" 2> /dev/null || true
		rm -f "$pid_file"
		fail "the test site did not answer 200 on $base_url/umbraco within ${timeout}s (last status $status)"
	fi
	sleep 3
done
echo "The test site answers 200 on $base_url/umbraco after $((SECONDS - started))s"
end_group
