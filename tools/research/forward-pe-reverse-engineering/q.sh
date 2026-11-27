#!/bin/sh
# Read-only query helper: reads SQL from stdin, prints pipe-separated rows.
docker exec -i financial-copilot-postgres-local sh -c 'PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=120000" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -P pager=off "$@"' -- "$@"
