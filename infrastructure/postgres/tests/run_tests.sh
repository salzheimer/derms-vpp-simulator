#!/usr/bin/env bash
# Database tests in throwaway containers (no named volumes; removed on exit):
#   rva_core      - built from core/rva_core_init.sql, then ddl_lint.sql and the pgTAP suite in pgtap/
#   rva_telemetry - built from telemetry/telemetry_init.sql, then ddl_lint.sql
#
#   infrastructure/postgres/tests/run_tests.sh            # lint + pgTAP, one line per assertion
#   infrastructure/postgres/tests/run_tests.sh --perf     # also run the timing checks in perf/
#   infrastructure/postgres/tests/run_tests.sh pgtap/gt05_local_overload.sql   # only these test files
#
# Exits non-zero if initialization, the lint, or any pgTAP assertion fails. Known schema gaps are written as pgTAP
# TODO tests: they print "not ok ... # TODO <reason>" but do not fail the run until the schema is fixed.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
postgres_dir="$(dirname "$here")"
image=rva-core-db-test
core=rva_core_db_test_$$
telemetry=rva_telemetry_db_test_$$

perf=false
files=()
for arg in "$@"; do
    case "$arg" in
        --perf) perf=true ;;
        *)      files+=("/tests/${arg#infrastructure/postgres/tests/}") ;;
    esac
done
[ ${#files[@]} -eq 0 ] && files=(/tests/pgtap)
$perf && files+=(/tests/perf)

cleanup() { docker rm -f "$core" "$telemetry" >/dev/null 2>&1 || true; }
trap cleanup EXIT

# Waits for the entrypoint to finish the init scripts and restart the server. Init failures stop the container,
# so print its log and fail instead of waiting out the timeout.
wait_ready() {
    local container=$1 db=$2
    for _ in $(seq 1 120); do
        if [ "$(docker inspect -f '{{.State.Running}}' "$container")" != true ]; then
            docker logs "$container" 2>&1 | tail -40
            echo "FAILED: $db initialization stopped the container" >&2
            return 1
        fi
        if docker logs "$container" 2>&1 | grep -q 'PostgreSQL init process complete' \
           && docker exec "$container" pg_isready -q -U postgres -d "$db"; then
            return 0
        fi
        sleep 1
    done
    echo "FAILED: $db did not become ready in 120 s" >&2
    return 1
}

echo "# building $image"
docker build -q -t "$image" "$here" >/dev/null

# Same init mounts as infrastructure/docker/docker-compose.yml: core replaces the whole init directory (suppressing the
# image's postgis_topology setup); telemetry adds one file after the image's own TimescaleDB scripts.
docker run -d --name "$core" -e POSTGRES_DB=rva_core -e POSTGRES_PASSWORD=test \
    -v "$postgres_dir/core:/docker-entrypoint-initdb.d:ro" -v "$here:/tests:ro" "$image" >/dev/null
docker run -d --name "$telemetry" -e POSTGRES_DB=rva_telemetry -e POSTGRES_PASSWORD=test \
    -v "$postgres_dir/telemetry/telemetry_init.sql:/docker-entrypoint-initdb.d/100_telemetry_init.sql:ro" \
    -v "$here:/tests:ro" timescale/timescaledb:latest-pg17 >/dev/null

wait_ready "$core" rva_core
wait_ready "$telemetry" rva_telemetry

echo "# ddl lint: rva_core"
docker exec "$core" psql -X -q -U postgres -d rva_core -v ON_ERROR_STOP=1 -f /tests/ddl_lint.sql
echo "# ddl lint: rva_telemetry"
docker exec "$telemetry" psql -X -q -U postgres -d rva_telemetry -v ON_ERROR_STOP=1 -f /tests/ddl_lint.sql

echo "# pgTAP: rva_core"
docker exec "$core" pg_prove -U postgres -d rva_core --ext .sql --recurse --verbose "${files[@]}"
