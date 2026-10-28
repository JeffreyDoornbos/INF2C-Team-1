"""Generate a psql data import for Anmol's existing EF Core schema.

Only uses the Python standard library. Does not connect to a database or change
the source JSON, EF Core entities, migrations, or application configuration.
"""

import hashlib
import json
import re
from datetime import date, datetime, timezone
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = Path(__file__).resolve().parent
# Parent records must be loaded before the records that reference them.
ENTITIES = [
    ("warehouse", "Warehouse"), ("client", "Client"), ("supplier", "Supplier"),
    ("item_line", "ItemLine"), ("item_group", "ItemGroup"), ("item_type", "ItemType"),
    ("location", "Location"), ("item", "Item"), ("inventory", "Inventory"),
    ("order", "Order"), ("order_item", "OrderItem"), ("shipment", "Shipment"),
    ("shipment_item", "ShipmentItem"), ("transfer", "Transfer"),
    ("transfer_item", "TransferItem"),
]
MIGRATIONS = ["20261015111621_InitialBaseline", "20261015113203_EnforceApprovedPilotRules"]


def quoted(value):
    return '"' + value.replace('"', '""') + '"'


def sql_string(value):
    return "'" + value.replace("'", "''") + "'"


def table_name(table):
    return "public." + quoted(table)


def entity_columns(entity):
    """Read the authoritative property names/types; mirror DbContext snake_case."""
    path = ROOT / "api-csharp" / "Domain" / f"{entity}.cs"
    properties = re.findall(r"public\s+(\w+\??)\s+(\w+)\s*\{\s*get;\s*set;\s*\}", path.read_text())
    if not properties:
        raise ValueError(f"No entity properties found: {path}")
    columns = [(re.sub(r"(?<!^)([A-Z])", r"_\1", name).lower(), kind) for kind, name in properties]
    allowed_types = {"int", "int?", "string", "string?", "DateOnly", "DateTimeOffset", "decimal"}
    if any(kind not in allowed_types for _, kind in columns):
        raise ValueError(f"Unsupported property type in {entity}")
    return columns


def parse_timestamp(value):
    if not isinstance(value, str):
        raise ValueError("Expected an ISO timestamp string")
    timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if timestamp.tzinfo is None:
        raise ValueError("Timestamp has no timezone; refusing to guess")
    return timestamp.astimezone(timezone.utc)


def convert_value(table, column, kind, value):
    if value is None:
        if kind.endswith("?"):
            return None
        raise ValueError("Null in a required property")
    if kind.rstrip("?") == "int":
        if type(value) is not int or not -(2**31) <= value < 2**31:
            raise ValueError("Expected a 32-bit integer")
        return str(value)
    if kind.rstrip("?") == "string":
        if table == "item" and column == "commodity_code" and type(value) is int:
            return str(value)
        if not isinstance(value, str):
            raise ValueError("Expected text")
        if "\x00" in value:
            raise ValueError("NUL character cannot be stored in PostgreSQL text")
        return value
    if kind == "DateOnly":
        if isinstance(value, str) and re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
            return date.fromisoformat(value).isoformat()
        return parse_timestamp(value).date().isoformat()
    if kind == "DateTimeOffset":
        return parse_timestamp(value).isoformat()
    if kind == "decimal":
        if not isinstance(value, (int, Decimal)) or isinstance(value, bool):
            raise ValueError("Expected a numeric JSON value")
        number = Decimal(value)
        scale = 6 if (table, column) == ("item", "unit_weight") else 4
        if not number.is_finite() or abs(number) >= Decimal(10) ** (18 - scale):
            raise ValueError("Value is outside the model's numeric precision")
        if number != number.quantize(Decimal(10) ** -scale):
            raise ValueError("Value would be rounded by the model's numeric scale")
        return format(number, "f")
    raise ValueError(f"Unsupported type: {kind}")


def copy_value(value):
    if value is None:
        return r"\N"
    return (value.replace("\\", "\\\\").replace("\t", "\\t")
            .replace("\n", "\\n").replace("\r", "\\r"))


def main():
    tables = []
    sources = {}
    model_hashes = {}
    for table, entity in ENTITIES:
        source_path = ROOT / "data" / f"{table}.json"
        raw = source_path.read_bytes()
        rows = json.loads(raw, parse_float=Decimal)
        if not isinstance(rows, list):
            raise ValueError(f"Expected a JSON array: {source_path}")
        columns = entity_columns(entity)
        expected = {name for name, _ in columns}
        converted = []
        date_conversions = {}
        for index, row in enumerate(rows):
            if not isinstance(row, dict) or set(row) != expected:
                raise ValueError(f"{table}.json record {index}: fields do not match {entity}.cs")
            output_row = []
            for column, kind in columns:
                try:
                    value = convert_value(table, column, kind, row[column])
                    if kind == "DateOnly" and row[column] != value:
                        date_conversions[column] = date_conversions.get(column, 0) + 1
                    output_row.append(value)
                except (ValueError, TypeError, ArithmeticError) as exc:
                    raise ValueError(f"{table}.json record {index}, column {column}: {exc}") from exc
            converted.append(output_row)
        tables.append((table, columns, converted))
        sources[table] = {
            "file": f"data/{table}.json", "records": len(rows),
            "sha256": hashlib.sha256(raw).hexdigest(),
            "timestamp_to_date_conversions": date_conversions,
        }
        model_path = ROOT / "api-csharp" / "Domain" / f"{entity}.cs"
        model_hashes[str(model_path.relative_to(ROOT))] = hashlib.sha256(model_path.read_bytes()).hexdigest()

    # Generate everything in memory first, so invalid source data does not replace
    # the previous valid generated import.
    sql = [
        "-- CargoHub legacy JSON import for Anmol's EF Core migrations.",
        "-- DATA ONLY: first run dotnet ef database update --project api-csharp.",
        "-- Execute with psql against your chosen database. No database is created or switched.",
        "-- Does not delete/update existing records. All 15 target tables must be empty.",
        "-- user.json is excluded: API key management is outside the EF Core model.",
        "-- DateOnly fields use the source UTC date; the time of day is discarded.",
        r"\set ON_ERROR_STOP on", r"\encoding UTF8", "BEGIN;",
        "SET LOCAL TIME ZONE 'UTC';", "SET LOCAL standard_conforming_strings = on;",
        "SET LOCAL lock_timeout = '10s';", "",
        "DO $preflight$", "BEGIN",
        "    IF to_regclass('public.\"__EFMigrationsHistory\"') IS NULL THEN",
        "        RAISE EXCEPTION 'EF Core migrations are missing. Run dotnet ef database update --project api-csharp first.';",
        "    END IF;",
    ]
    for migration in MIGRATIONS:
        sql += [
            f'    IF NOT EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" = {sql_string(migration)}) THEN',
            f"        RAISE EXCEPTION 'Required EF Core migration missing: {migration}';",
            "    END IF;",
        ]
    sql += ["END;", "$preflight$;", "", "-- Block competing writers throughout the emptiness check and import.",
            "LOCK TABLE " + ", ".join(table_name(table) for table, _, _ in tables) + " IN SHARE ROW EXCLUSIVE MODE;",
            "", "DO $empty_check$", "BEGIN"]
    for table, _, _ in tables:
        sql += [f"    IF EXISTS (SELECT 1 FROM {table_name(table)}) THEN",
                f"        RAISE EXCEPTION 'Import refused: public.{table} is not empty. No existing data has been changed.';",
                "    END IF;"]
    sql += ["END;", "$empty_check$;", ""]
    for table, columns, rows in tables:
        source = sources[table]
        sql += [f"-- {source['file']}: {len(rows)} records, sha256: {source['sha256']}",
                f"COPY {table_name(table)} (" + ", ".join(quoted(column) for column, _ in columns) + ") FROM stdin;"]
        sql.extend("\t".join(copy_value(value) for value in row) for row in rows)
        sql += [r"\.", ""]
    sql += ["DO $verify_counts$", "BEGIN"]
    for table, _, rows in tables:
        sql += [f"    IF (SELECT COUNT(*) FROM {table_name(table)}) <> {len(rows)} THEN",
                f"        RAISE EXCEPTION 'Unexpected record count in public.{table}';", "    END IF;"]
    sql += ["END;", "$verify_counts$;", "", "-- Transactional sequence resets: new IDs follow the existing IDs."]
    for table, columns, rows in tables:
        names = [name for name, _ in columns]
        if "id" in names:
            next_id = max((int(row[names.index("id")]) for row in rows), default=0) + 1
            sql.append(f"ALTER TABLE {table_name(table)} ALTER COLUMN id RESTART WITH {next_id};")
    sql += ["", "COMMIT;", "", "-- Imported record counts:"]
    counts = [f"SELECT {sql_string(table)} AS table_name, COUNT(*) AS records FROM {table_name(table)}" for table, _, _ in tables]
    sql.append("\nUNION ALL\n".join(counts) + "\nORDER BY table_name;")
    sql.append("")

    manifest = {
        "target_schema": "public", "required_migrations": MIGRATIONS,
        "total_records": sum(len(rows) for _, _, rows in tables),
        "excluded_files": {"data/user.json": "Not part of Anmol's EF Core model; handled separately in CH-020."},
        "conversions": {
            "order.order_date": "UTC calendar date only, following DateOnly",
            "order.request_date": "UTC calendar date only, following DateOnly",
            "shipment.shipment_date": "UTC calendar date only, following DateOnly",
            "item.commodity_code": "JSON integer converted to text",
            "created_at/updated_at": "UTC timestamp, retaining the instant",
        },
        "sources": sources, "entity_sha256": model_hashes,
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "import-legacy-json.sql").write_text("\n".join(sql), encoding="utf-8")
    (OUTPUT / "legacy-import-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Generated import-legacy-json.sql: {manifest['total_records']} records across {len(tables)} EF Core tables.")


if __name__ == "__main__":
    main()
