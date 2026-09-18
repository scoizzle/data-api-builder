# Oracle Support

Data API builder supports Oracle Database 19c and later through
`Oracle.ManagedDataAccess.Core`.

## Supported

- REST and GraphQL queries, filters, ordering, selection, and cursor pagination
- REST and GraphQL create, update, delete, and upsert operations
- GraphQL nested multiple-create (`runtime.graphql.multiple-mutations.create.enabled` / `--graphql.multiple-mutations.create.enabled`), same surface as MSSQL
- Composite primary keys
- Database-policy predicates for read, create, update, and delete operations
- Oracle `NUMBER`, character, date/time, `RAW`, and `BLOB` values
- Oracle `RAW` and `BLOB` values serialized as base64 byte values
- Identity and default-valued inserts through Oracle PL/SQL DML blocks
- Type-aware `RETURNING INTO` output binds for numeric, date/time, and binary values
- Managed identity password-token replacement when configured
- Oracle metadata lookup through `ALL_TAB_COLUMNS`, `ALL_CONSTRAINTS`, and related views

## Current Limitations

### Stored procedures

Oracle stored procedures and functions can be invoked as REST operations, and as GraphQL operations for result sets DAB can describe. `SqlExecuteStructure` is implemented for Oracle: subprograms are invoked from a PL/SQL anonymous block (never the SQL*Plus-only `EXEC` keyword), engine-generated `@paramN` bind references are translated to Oracle `:paramN` syntax, and a trailing `:dab_result` REF CURSOR OUT bind exposes the result set to ODP.NET. Package-qualified subprograms (`schema.package.subprogram`) are supported, as are standalone functions invoked via `SELECT ... FROM DUAL`.

Known limitations:

- **GraphQL stored-procedure result typing.** Oracle metadata discovery describes a subprogram's REF CURSOR by its OUT parameter name (e.g. `CURSOR`), not by the columns the cursor returns. REST invocations are unaffected (the response is keyed by the actual returned columns), but the GraphQL schema for a cursor-returning subprogram exposes the cursor parameter name rather than the rowset's columns, so GraphQL queries over stored-procedure result sets are not reliably typed. Prefer REST for stored-procedure invocations on Oracle until cursor-column discovery is implemented.
- **Scalar OUT/IN OUT parameters.** Subprograms whose result is a scalar OUT/IN OUT parameter (no REF CURSOR) are invoked with only their IN arguments; the OUT argument is not bound, so such subprograms fail at request time. Only subprograms with IN parameters plus an optional REF CURSOR OUT parameter are supported.

Oracle stored-procedure metadata discovery (including OUT parameters and REF CURSOR metadata) is used to validate signatures for schema generation. Invoking a procedure whose metadata cannot be resolved, or that returns an unsupported result shape, surfaces an error at request time.

### Session context

Oracle session-context forwarding is disabled by default. The current DAB command pipeline prepends session setup text to the SQL command. Oracle requires session-context calls to run inside a PL/SQL block, so prepending a standalone `BEGIN ... END;` block would produce invalid command text.

A future implementation must use a valid Oracle application context package and execute session setup together with the request command, or use a separate command on the same connection. Until then, Oracle database policies must not depend on DAB-forwarded session claims.

### Autoentities and aggregation

Oracle autoentity discovery is supported: tables with a primary key are discovered through `ALL_TABLES` (Oracle-maintained system schemas are excluded) and materialized as entities according to the include/exclude/name patterns, both at engine startup and through `dab auto-config-simulate`. Generated entity names are lowercased so REST paths and GraphQL type names stay stable regardless of catalog folding; unmapped columns still surface as the catalog spelling (see Identifier casing). The SQL aggregation GraphQL surface (groupBy) is enabled for Oracle and emits `GROUP BY`, `HAVING`, and aggregation columns (COUNT/SUM/AVG/MIN/MAX) through the shared query-builder contracts.

### GraphQL multiple-create

Nested GraphQL create (parent/child/linking inserts in FK order, then a follow-up SELECT of created keys) is supported for Oracle when the CLI flag or config option is enabled, the same as MSSQL. The path uses a **local `OracleTransaction`** on a single connection (`ExecuteQueryOnConnection`); it is **not XA** and does not rely on `TransactionScope` promotion.

Oracle rejects the `AS` keyword on table aliases (`INNER JOIN t AS alias`); generated join/FROM/EXISTS SQL omits `AS`.

Create-policy failure on a non-linking insert returns **403** (`DatabasePolicyFailure`). Trigger-assigned primary keys that cannot be returned to the mutation engine result in **500** and **rollback** of the nested graph.

This path is **GraphQL-only**. REST batch/array create does not share it (same as MSSQL).

### Binary values

Oracle `RAW` values are serialized as base64. `BLOB` columns are typed as byte[] and null-guarded during base64 encoding; values larger than roughly 2000 bytes are not covered because `UTL_ENCODE.BASE64_ENCODE` accepts `RAW` and the implicit `BLOB`-to-`RAW` conversion is size-limited.

### Synonyms

Oracle resolves an **unqualified** name in this order: object in the current schema, then a **private synonym** owned by the current user, then a **public** synonym (`OWNER = 'PUBLIC'`). A **schema-qualified** name never uses a public synonym.

DAB always schema-qualifies and quotes (`QuoteRelation` → `"USER"."OBJECT"`), so public synonyms would be skipped if the synonym name were emitted as-is. Catalog views (`ALL_TAB_COLUMNS`, `ALL_CONSTRAINTS`, …) describe the **base** object, not the synonym.

At startup, `OracleMetadataProvider` resolves each source (and FK pair table) to the local base object, then FillSchema and SQL use those physical names.

Resolution (Oracle’s own order):

1. Unqualified source (schema is the connected user, or `PUBLIC`): current-schema table/view/procedure, else private synonym, else public synonym (`OWNER = 'PUBLIC'`).
2. Schema-qualified source other than the connected user / `PUBLIC`: object or private synonym in that schema only — no public fallback.
3. Follow nested synonyms with a cycle/depth guard (max 10).
4. If `DB_LINK` is set, fail startup (local objects only).
5. Packaged stored procedures (`schema.package.sub`) are left unchanged; standalone subprogram synonyms are resolved.

Autoentity discovery stays on `ALL_TABLES`. Synonym sources are opt-in via entity config.

No change to `QuoteRelation` / shared SQL: after resolve, emit the base catalog names.

## Identifier casing

Oracle folds unquoted identifiers to uppercase in the catalog. DAB quotes every identifier, so SQL must use the **catalog spelling** (physical backing name). REST/GraphQL/OData names are a separate **exposed** layer.

- Backing names on `SourceDefinition` (columns, primary key, FK column lists) are the catalog spelling: `ID`, `BOOK_ID`, or the exact spelling of a quoted identifier.
- Exposed names are the name **as provided to the engine**: the entity config mapping / field alias verbatim when one is configured, otherwise the catalog spelling of the column. Unmapped unquoted columns therefore surface as `ID` / `BOOK_ID`, and quoted columns keep their exact catalog spelling (e.g. `"ID Number"`). There is no lowercase fallback.
- Generated SQL quotes backing names as-is. Schema, table, package, and procedure names go through `QuoteRelation` / `QuoteCatalogObject` (uppercase, quoted). DAB-generated aliases go through `QuoteTableAlias`. Quoted mixed-case *tables* are not covered yet.
- The Oracle test config supplies explicit lowercase mappings for the columns the shared API suite expects (e.g. `publisher_id`, `categoryid`) so the shared contract is preserved; entities injected by the test harness (`magazine`, `bar_magazine`) get the same mappings in `TestHelper.AddMissingEntitiesToConfig`.

Casing translation lives in `OracleMetadataProvider` (config name → physical backing name) and `OracleQueryBuilder` (physical name → SQL identifier). Shared SQL/GraphQL code talks to the existing backing/exposed maps (`TryGetBackingColumn` / `TryGetExposedColumnName`) and does not special-case Oracle.

## Authorization policies

Database policies (`permissions[].actions[].policy.database`) use the shared engine-agnostic pipeline: `AuthorizationPolicyHelpers` resolves the policy for the role/operation (compound upsert operations expand to Update + Create), claim references (`@claims.x`) are replaced with typed bound parameters, `ODataASTVisitor` maps `@item.field` exposed names to physical backing columns and quotes them, and each builder injects the resulting predicate via `GetDbPolicyForOperation`.

Oracle specifics:

- **Predicate placement.** Read/update/delete policies are ANDed into the `WHERE` clause exactly like the other engines. Create (and the insert branch of upsert) cannot use `INSERT … SELECT … WHERE`, so Oracle gates the insert with `SELECT COUNT(*) INTO v FROM (SELECT value AS "COL", … FROM DUAL) WHERE <policy>` and opens an empty REF CURSOR when the policy is unsatisfied, which surfaces as HTTP 403.
- **Inserts with no column values.** When the request body supplies no insertable columns, the policy cannot be evaluated against a row, so the request is rejected with HTTP 400 `DatabasePolicyFailure` instead of falling back to a `DEFAULT VALUES` insert (this is enforced for all engines; the other engines previously bypassed the create policy in that case).
- **Empty string is NULL.** Oracle cannot store `''` distinctly from `NULL`, so a policy comparing a column to `''` is rewritten to the equivalent `IS NULL` / `IS NOT NULL` predicate (`@item.col eq ''` → `"COL" IS NULL`, `@item.col ne ''` → `"COL" IS NOT NULL`). Other engines compare against a real empty string.
- **String comparison is case-sensitive** under Oracle's default binary collation.
- **Numeric comparisons** bind policy literals for `NUMBER` columns as `decimal`; boolean-style predicates against `NUMBER(1)` columns are not supported.
- **Invalid policy fields** (a field the entity does not expose) are rejected while processing the policy with a clear authorization error, rather than producing a malformed predicate.
- **Multiple-create** applies the read policy of each created/related entity, combined with the OR'd primary-key predicate, in the same way as MSSQL.

## Cursor usage and ODP.NET statement caching

DAB disposes every `DbDataReader`/`DbCommand` after a result is read, including the REF CURSOR output parameters used by the PL/SQL DML blocks, so it does not leak cursors. However, ODP.NET's client-side statement cache retains one open cursor per **distinct** SQL statement on a connection (measured: the cursor count grows by one per distinct statement and stays flat for repeated statements). On a long-lived pooled session that executes many distinct statements this consumes the account's per-session `open_cursors` limit (Oracle default `300`), surfacing at request time as:

```
ORA-00604: Error occurred at recursive SQL level 1.
ORA-01000: maximum open cursors for session exceeded
```

Handle it either at the database or from the DAB connection string:

- **Size the database limit** for the DAB account/host, e.g. `ALTER SYSTEM SET open_cursors = 1500 SCOPE = BOTH;` (applies to new sessions). Recommended for normal deployments.
- **Bound or disable the ODP.NET statement cache** by adding ODP.NET attributes to the DAB data-source connection string:
  - `Self Tuning=false` — stop ODP.NET from auto-sizing the cache by workload.
  - `Statement Cache Size=<n>` — cap the number of retained statements (honored when `Self Tuning=false`).
  - `Statement Cache Size=0;Self Tuning=false` — disable caching entirely. Cursor usage then stays flat regardless of `open_cursors`, at the cost of re-parsing each statement.

  DAB preserves these attributes when it builds the ODP.NET connection. Example:

  ```json
  "connection-string": "Data Source=...;User Id=...;Password=...;Statement Cache Size=50;Self Tuning=false;"
  ```

### Test database

The Oracle integration suite issues several hundred distinct statements, so it exhausts the default `open_cursors=300` partway through a full `TestCategory=ORACLE` run. The test container must either set `open_cursors=1500` or the test connection string must bound the statement cache; with either in place the full category passes. This is cursor-cache sizing, not a DAB query-path leak: 500 repeated list queries, 400 repeated upserts, and repeated `DatabaseSchema-Oracle.sql` re-initialization all leave the cursor count flat or plateaued.
