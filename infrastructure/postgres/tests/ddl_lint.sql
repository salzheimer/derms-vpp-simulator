/*
DDL lint: checks a database's schema against .ai/rules/sql-ddl.md by reading the system catalog, so it sees the
schema as built (including tables created dynamically, such as the topology _history twins), not the script text.

  docker exec -i rva_core_db psql -U postgres -d rva_core -v ON_ERROR_STOP=1 < infrastructure/postgres/tests/ddl_lint.sql

Checks every schema except public, PostgreSQL's own, and extension-owned ones (PostGIS, TimescaleDB), so it can run
against any RVA database. Application tables in public are themselves a violation, so nothing escapes the checks. Prints "ddl lint: PASS" on success; otherwise raises one error that lists every violation.
Runs in one transaction and rolls back, leaving nothing behind.
*/

BEGIN;

-- Type text as written in DDL and as returned by format_type(), reduced to one comparable form.
CREATE FUNCTION pg_temp.type_key(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT lower(regexp_replace(
               regexp_replace(regexp_replace(regexp_replace(t,
                   'character varying', 'varchar', 'gi'),
                   'timestamp with time zone', 'timestamptz', 'gi'),
                   'timestamp without time zone', 'timestamp', 'gi'),
               '\s+', '', 'g'))
$$;

-- Facts-tag items reduced to one comparable form (case and spaces ignored).
CREATE FUNCTION pg_temp.item_key(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT lower(regexp_replace(t, '\s+', '', 'g'))
$$;

CREATE TEMP TABLE lint_violations (rule text, object text, detail text) ON COMMIT DROP;

CREATE TEMP TABLE lint_schemas ON COMMIT DROP AS
SELECT n.oid, n.nspname
FROM pg_namespace n
WHERE n.nspname NOT IN ('public', 'information_schema', 'toolkit_experimental')
  AND n.nspname NOT LIKE 'pg\_%'
  AND n.nspname NOT LIKE '\_timescaledb%'
  AND n.nspname NOT LIKE 'timescaledb\_%'
  AND NOT EXISTS (SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_namespace'::regclass AND d.objid = n.oid AND d.deptype = 'e');

CREATE TEMP TABLE lint_tables ON COMMIT DROP AS
SELECT c.oid,
       format('%I.%I', s.nspname, c.relname) AS qname,
       c.relname::text AS table_name,
       -- a <table>_history twin of a versioned table: its columns are copies, documented by enable_versioning
       (c.relname LIKE '%\_history' AND EXISTS (SELECT 1 FROM pg_class m
                                                WHERE m.relnamespace = c.relnamespace AND m.relkind IN ('r', 'p')
                                                  AND m.relname = left(c.relname, -length('_history')))) AS is_history
FROM pg_class c
JOIN lint_schemas s ON s.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition
  AND NOT EXISTS (SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e');


-- ---------------------------------------------------------------- schemas and tables

INSERT INTO lint_violations
SELECT 'naming', format('%I', s.nspname), 'schema name is not lowercase snake_case'
FROM lint_schemas s
WHERE s.nspname !~ '^[a-z][a-z0-9_]*$';

-- public is not checked, so nothing of ours may live there (extension objects such as PostGIS's spatial_ref_sys excepted).
INSERT INTO lint_violations
SELECT 'module-schema', format('public.%I', c.relname), 'application tables belong in a module schema, not public'
FROM pg_class c
WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r', 'p') AND NOT c.relispartition
  AND NOT EXISTS (SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e');

INSERT INTO lint_violations
SELECT 'table-comment', t.qname, 'table has no comment'
FROM lint_tables t
WHERE coalesce(btrim(obj_description(t.oid, 'pg_class')), '') = '';

INSERT INTO lint_violations
SELECT 'primary-key', t.qname, 'table has no primary key'
FROM lint_tables t
WHERE NOT t.is_history
  AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid = t.oid AND k.contype = 'p');

INSERT INTO lint_violations
SELECT 'naming', t.qname, 'table name is not lowercase snake_case'
FROM lint_tables t
WHERE t.table_name !~ '^[a-z][a-z0-9_]*$';


-- ---------------------------------------------------------------- columns: comment and facts tag

DO $$
DECLARE
    c               record;
    obj             text;
    cmt             text;
    purpose         text;
    tag             text;
    items           text[];
    item            text;
    in_pk           boolean;
    expected_unique text[];
    expected_fk     text[];
    in_check        boolean;
    nullability     text[];
BEGIN
    FOR c IN
        SELECT t.oid AS relid, t.qname, t.is_history, a.attnum, a.attname::text AS column_name,
               format_type(a.atttypid, a.atttypmod) AS type_name, a.attnotnull,
               a.attidentity <> '' AS is_identity, a.attgenerated <> '' AS is_generated,
               a.atthasdef AND a.attidentity = '' AND a.attgenerated = '' AS has_default
        FROM lint_tables t
        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY t.qname, a.attnum
    LOOP
        obj := c.qname || '.' || c.column_name;
        cmt := btrim(col_description(c.relid, c.attnum));

        IF c.column_name !~ '^[a-z][a-z0-9_]*$' THEN
            INSERT INTO lint_violations VALUES ('naming', obj, 'column name is not lowercase snake_case');
        END IF;

        IF coalesce(cmt, '') = '' THEN
            INSERT INTO lint_violations VALUES ('column-comment', obj, 'column has no comment');
            CONTINUE;
        END IF;

        tag := substring(cmt FROM '\[([^\[\]]*)\]$');
        IF tag IS NULL THEN
            INSERT INTO lint_violations VALUES ('column-comment', obj, 'comment does not end with a [facts] tag');
            CONTINUE;
        END IF;
        purpose := btrim(regexp_replace(cmt, '\[[^\[\]]*\]$', ''));
        IF purpose = '' THEN
            INSERT INTO lint_violations VALUES ('column-comment', obj, 'comment has no purpose before its [facts] tag');
        END IF;

        SELECT array_agg(btrim(x) ORDER BY n) INTO items
        FROM unnest(string_to_array(tag, ';')) WITH ORDINALITY AS u(x, n);

        -- type: always the first item
        IF pg_temp.type_key(items[1]) IS DISTINCT FROM pg_temp.type_key(c.type_name) THEN
            INSERT INTO lint_violations VALUES ('facts-type', obj,
                format('tag type "%s" does not match the column type "%s"', items[1], c.type_name));
        END IF;
        items := items[2:];

        -- nullability: exactly one item, matching the column
        nullability := ARRAY(SELECT upper(x) FROM unnest(items) x WHERE upper(x) IN ('NULL', 'NOT NULL'));
        IF cardinality(nullability) <> 1 THEN
            INSERT INTO lint_violations VALUES ('facts-null', obj, 'tag must contain exactly one of NULL or NOT NULL');
        ELSIF (nullability[1] = 'NOT NULL') <> c.attnotnull THEN
            INSERT INTO lint_violations VALUES ('facts-null', obj,
                format('tag says %s but the column is %s', nullability[1], CASE WHEN c.attnotnull THEN 'NOT NULL' ELSE 'NULL' END));
        END IF;

        -- presence-only items: IDENTITY, DEFAULT, GENERATED
        IF EXISTS (SELECT 1 FROM unnest(items) x WHERE upper(x) = 'IDENTITY') <> c.is_identity THEN
            INSERT INTO lint_violations VALUES ('facts-identity', obj,
                CASE WHEN c.is_identity THEN 'identity column: tag needs IDENTITY' ELSE 'tag says IDENTITY but the column is not one' END);
        END IF;
        IF EXISTS (SELECT 1 FROM unnest(items) x WHERE x ~* '^DEFAULT\s+\S') <> c.has_default THEN
            INSERT INTO lint_violations VALUES ('facts-default', obj,
                CASE WHEN c.has_default THEN 'column has a default: tag needs DEFAULT <description>' ELSE 'tag says DEFAULT but the column has none' END);
        END IF;
        IF EXISTS (SELECT 1 FROM unnest(items) x WHERE x ~* '^GENERATED\s+\S') <> c.is_generated THEN
            INSERT INTO lint_violations VALUES ('facts-generated', obj,
                CASE WHEN c.is_generated THEN 'generated column: tag needs GENERATED <description>' ELSE 'tag says GENERATED but the column is not generated' END);
        END IF;

        -- PK
        in_pk := EXISTS (SELECT 1 FROM pg_constraint k
                         WHERE k.conrelid = c.relid AND k.contype = 'p' AND c.attnum = ANY (k.conkey));
        IF EXISTS (SELECT 1 FROM unnest(items) x WHERE upper(x) = 'PK') <> in_pk THEN
            INSERT INTO lint_violations VALUES ('facts-pk', obj,
                CASE WHEN in_pk THEN 'primary key column: tag needs PK' ELSE 'tag says PK but the column is not in the primary key' END);
        END IF;

        -- UNIQUE: one item per unique constraint or unique index on the column (primary key excluded)
        expected_unique := ARRAY(
            SELECT CASE WHEN i.indnkeyatts = 1 THEN 'UNIQUE'
                        ELSE 'UNIQUE (' || (SELECT string_agg(a.attname, ', ' ORDER BY k.n)
                                            FROM unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, n)
                                            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                                            WHERE k.n <= i.indnkeyatts) || ')' END
            FROM pg_index i
            WHERE i.indrelid = c.relid AND i.indisunique AND NOT i.indisprimary
              AND c.attnum IN (SELECT k.attnum FROM unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, n)
                               WHERE k.n <= i.indnkeyatts));
        IF ARRAY(SELECT pg_temp.item_key(x) FROM unnest(expected_unique) x ORDER BY 1)
           IS DISTINCT FROM ARRAY(SELECT pg_temp.item_key(x) FROM unnest(items) x WHERE x ~* '^UNIQUE\M' ORDER BY 1) THEN
            INSERT INTO lint_violations VALUES ('facts-unique', obj,
                format('expected unique items {%s}', array_to_string(expected_unique, ' | ')));
        END IF;

        -- FK: one item per foreign key the column is part of, with the full referenced column list
        expected_fk := ARRAY(
            SELECT 'FK ' || format('%I.%I', rn.nspname, rc.relname)
                   || '(' || (SELECT string_agg(a.attname, ', ' ORDER BY k.n)
                              FROM unnest(f.confkey) WITH ORDINALITY AS k(attnum, n)
                              JOIN pg_attribute a ON a.attrelid = f.confrelid AND a.attnum = k.attnum) || ')'
                   || CASE f.confdeltype WHEN 'c' THEN ' ON DELETE CASCADE' WHEN 'n' THEN ' ON DELETE SET NULL'
                                         WHEN 'd' THEN ' ON DELETE SET DEFAULT' WHEN 'r' THEN ' ON DELETE RESTRICT' ELSE '' END
            FROM pg_constraint f
            JOIN pg_class rc ON rc.oid = f.confrelid
            JOIN pg_namespace rn ON rn.oid = rc.relnamespace
            WHERE f.conrelid = c.relid AND f.contype = 'f' AND c.attnum = ANY (f.conkey));
        IF ARRAY(SELECT pg_temp.item_key(x) FROM unnest(expected_fk) x ORDER BY 1)
           IS DISTINCT FROM ARRAY(SELECT pg_temp.item_key(x) FROM unnest(items) x WHERE x ~* '^FK\M' ORDER BY 1) THEN
            INSERT INTO lint_violations VALUES ('facts-fk', obj,
                format('expected foreign key items {%s}', array_to_string(expected_fk, ' | ')));
        END IF;

        -- CHECK: present exactly when the column takes part in a check constraint
        in_check := EXISTS (SELECT 1 FROM pg_constraint k
                            WHERE k.conrelid = c.relid AND k.contype = 'c' AND c.attnum = ANY (k.conkey));
        IF EXISTS (SELECT 1 FROM unnest(items) x WHERE x ~* '^CHECK\s+\S') <> in_check THEN
            INSERT INTO lint_violations VALUES ('facts-check', obj,
                CASE WHEN in_check THEN 'column is in a CHECK constraint: tag needs CHECK <description>'
                     ELSE 'tag says CHECK but the column is in no check constraint' END);
        END IF;

        -- REF: an _id column that is not a key of its own (primary, foreign, single-column unique, identity) must say
        -- what it refers to. Being part of a composite unique key does not exempt it.
        IF NOT c.is_history AND c.column_name LIKE '%\_id' AND NOT in_pk AND NOT c.is_identity
           AND cardinality(expected_fk) = 0 AND NOT 'UNIQUE' = ANY (expected_unique)
           AND NOT EXISTS (SELECT 1 FROM unnest(items) x WHERE x ~* '^REF\s+\S') THEN
            INSERT INTO lint_violations VALUES ('facts-ref', obj,
                'an _id column with no foreign key needs REF <target and how it is validated>');
        END IF;

        -- anything else is not part of the tag grammar
        FOREACH item IN ARRAY items LOOP
            IF item !~* '^(NULL|NOT NULL|IDENTITY|PK|UNIQUE|UNIQUE\s*\(.+\)|FK\s+\S.*|REF\s+\S.*|CHECK\s+\S.*|DEFAULT\s+\S.*|GENERATED\s+\S.*)$' THEN
                INSERT INTO lint_violations VALUES ('facts-syntax', obj, format('unknown tag item "%s"', item));
            END IF;
        END LOOP;
    END LOOP;
END $$;


-- ---------------------------------------------------------------- types and defaults

INSERT INTO lint_violations
SELECT 'types', t.qname || '.' || a.attname,
       format('type %s is not allowed (use TIMESTAMPTZ, JSONB, VARCHAR/TEXT)', format_type(a.atttypid, a.atttypmod))
FROM lint_tables t
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped
WHERE format_type(a.atttypid, a.atttypmod) ~ '^(json|money|character(\(\d+\))?|timestamp(\(\d\))? without time zone)$';

INSERT INTO lint_violations
SELECT 'defaults', t.qname || '.' || a.attname, format('default "%s": %s', pg_get_expr(d.adbin, d.adrelid),
       CASE WHEN pg_get_expr(d.adbin, d.adrelid) ~ 'nextval\(' THEN 'use GENERATED ALWAYS AS IDENTITY, not serial'
            ELSE 'use clock_timestamp(), not the transaction start time' END)
FROM lint_tables t
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped
JOIN pg_attrdef d ON d.adrelid = t.oid AND d.adnum = a.attnum
WHERE pg_get_expr(d.adbin, d.adrelid) ~* '\m(now|transaction_timestamp|statement_timestamp)\(\)|\mcurrent_timestamp\M|\mlocaltimestamp\M|nextval\(';

INSERT INTO lint_violations
SELECT 'types', format('%I.%I', s.nspname, ty.typname), 'ENUM types are not allowed; use a lookup table with a foreign key'
FROM pg_type ty
JOIN lint_schemas s ON s.oid = ty.typnamespace
WHERE ty.typtype = 'e';


-- ---------------------------------------------------------------- keys and names

INSERT INTO lint_violations
SELECT 'cross-schema-fk', t.qname, format('foreign key %s references %s in another schema', k.conname, k.confrelid::regclass)
FROM lint_tables t
JOIN pg_constraint k ON k.conrelid = t.oid AND k.contype = 'f'
JOIN pg_class rc ON rc.oid = k.confrelid
WHERE rc.relnamespace <> (SELECT relnamespace FROM pg_class WHERE oid = t.oid);

-- Named constraints use a prefix; inline ones may keep PostgreSQL's generated name. NOT NULL constraints are skipped.
INSERT INTO lint_violations
SELECT 'naming', t.qname, format('constraint %s: name must start with pk_, uk_, ck_, fk_, or ex_ (or keep the generated name)', k.conname)
FROM lint_tables t
JOIN pg_constraint k ON k.conrelid = t.oid AND k.contype IN ('p', 'u', 'c', 'f', 'x')
WHERE k.conname !~ '^(pk|uk|ck|fk|ex)_[a-z0-9_]+$'
  AND k.conname !~ '^[a-z0-9_]+_(pkey|key|check|fkey|excl)$';

INSERT INTO lint_violations
SELECT 'naming', t.qname, format('index %s: name must start with ix_ (or ux_ for a unique index)', ic.relname)
FROM lint_tables t
JOIN pg_index i ON i.indrelid = t.oid
JOIN pg_class ic ON ic.oid = i.indexrelid
WHERE NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = i.indexrelid AND k.conrelid = t.oid)
  AND ic.relname !~ '^(ix|ux)_[a-z0-9_]+$';

INSERT INTO lint_violations
SELECT 'naming', t.qname, format('trigger %s: name must start with trg_', g.tgname)
FROM lint_tables t
JOIN pg_trigger g ON g.tgrelid = t.oid AND NOT g.tgisinternal
WHERE g.tgname !~ '^trg_[a-z0-9_]+$';

INSERT INTO lint_violations
SELECT 'naming', format('%I.%I', s.nspname, p.proname), 'function name is not lowercase snake_case'
FROM pg_proc p
JOIN lint_schemas s ON s.oid = p.pronamespace
WHERE p.proname !~ '^[a-z][a-z0-9_]*$'
  AND NOT EXISTS (SELECT 1 FROM pg_depend d
                  WHERE d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e');


-- ---------------------------------------------------------------- result

DO $$
DECLARE
    report      text;
    violations  int;
    tables      int;
    columns     int;
BEGIN
    SELECT count(*), string_agg(format('  [%s] %s: %s', rule, object, detail), E'\n' ORDER BY object, rule, detail)
    INTO violations, report
    FROM lint_violations;

    IF violations > 0 THEN
        RAISE EXCEPTION E'ddl lint: % violation(s) (rules: .ai/rules/sql-ddl.md)\n%', violations, report;
    END IF;

    SELECT count(*) INTO tables FROM lint_tables;
    SELECT count(*) INTO columns
    FROM lint_tables t JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped;
    RAISE NOTICE 'ddl lint: PASS (% tables, % columns)', tables, columns;
END $$;

ROLLBACK;
