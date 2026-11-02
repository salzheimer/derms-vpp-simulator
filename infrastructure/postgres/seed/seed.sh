#!/usr/bin/env bash
# Seeds rva_core with the catalog (catalog.sql) and the development topology (dev_topology.sql), in one transaction:
# either everything is committed or nothing is. Idempotent: a second run adds no rows and no history. A catalog row that
# differs from catalog.sql is left unchanged and reported as a WARNING (drift). Development only: tests never load the
# development topology (plan p1-s01b, D12), and the host never seeds at startup.
#
# Connects with psql and the standard libpq settings (PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE; the database
# defaults to rva_core). To run psql inside the compose database container instead, which needs no local psql:
#
#   RVA_SEED_CONTAINER=rva_core_db infrastructure/postgres/seed/seed.sh
#
# Exit status: 0 seeded; 2 the database could not be reached; 3 the database has no rva_core schema; other non-zero
# values come from psql when a statement fails (nothing is committed).
set -euo pipefail

seed_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PGDATABASE="${PGDATABASE:-rva_core}"

if [[ -n "${RVA_SEED_CONTAINER:-}" ]]; then
    psql_cmd=(docker exec -i "$RVA_SEED_CONTAINER" psql -U "${PGUSER:-postgres}" -d "$PGDATABASE")
else
    psql_cmd=(psql)
fi

run_psql() {
    "${psql_cmd[@]}" -X -q -v ON_ERROR_STOP=1 "$@"
}

# Fail early, and clearly, on a database rva_core_init.sql has not initialized.
if ! has_schema="$(run_psql -t -A -c "SELECT to_regclass('topology.assets') IS NOT NULL AND to_regclass('topology.hardware_profiles') IS NOT NULL")"; then
    echo "seed: could not query database '$PGDATABASE'" >&2
    exit 2
fi
if [[ "$has_schema" != "t" ]]; then
    echo "seed: database '$PGDATABASE' has no rva_core schema (topology.assets); initialize it with infrastructure/postgres/core/rva_core_init.sql first" >&2
    exit 3
fi

# One transaction for both files. ON_ERROR_STOP makes psql exit at the first error, and the open transaction is rolled
# back when the connection closes.
{
    echo 'BEGIN;'
    cat "$seed_dir/catalog.sql" "$seed_dir/dev_topology.sql"
    echo 'COMMIT;'
} | run_psql -f -

echo "seed: database '$PGDATABASE' seeded"
