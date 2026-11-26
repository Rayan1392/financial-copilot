"""Read-only extraction of the research inputs from the local Sapio database into data/*.csv.

Every query runs with default_transaction_read_only=on. Usage: py -3 extract.py
"""
import pathlib, subprocess, sys

HERE = pathlib.Path(__file__).parent
CONTAINER = "financial-copilot-postgres-local"
PSQL = ('PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=120000" '
        'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 --csv -q')

def run(sql_file: pathlib.Path) -> None:
    out = HERE / "data" / (sql_file.stem + ".csv")
    res = subprocess.run(["docker", "exec", "-i", CONTAINER, "sh", "-c", PSQL],
                         input=sql_file.read_bytes(), capture_output=True)
    if res.returncode != 0:
        sys.exit(f"{sql_file.name}: {res.stderr.decode('utf-8', 'replace')}")
    out.write_bytes(res.stdout)
    rows = res.stdout.count(b"\n") - 1
    print(f"{sql_file.name}: {rows} rows -> {out.name}")

if __name__ == "__main__":
    only = sys.argv[1:]
    for f in sorted((HERE / "sql").glob("*.sql")):
        if not only or any(o in f.name for o in only):
            run(f)
