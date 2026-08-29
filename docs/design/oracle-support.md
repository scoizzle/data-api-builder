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

Oracle autoentity discovery is supported: tables with a primary key are discovered through `ALL_TABLES` (Oracle-maintained system schemas are excluded) and materialized as entities according to the include/exclude/name patterns, both at engine startup and through `dab auto-config-simulate`. Generated entity names are lowercased so REST paths and GraphQL names match the lowercase exposed-column convention. The SQL aggregation GraphQL surface (groupBy) is enabled for Oracle and emits `GROUP BY`, `HAVING`, and aggregation columns (COUNT/SUM/AVG/MIN/MAX) through the shared query-builder contracts.

### GraphQL multiple-create

Nested GraphQL create (parent/child/linking inserts in FK order, then a follow-up SELECT of created keys) is supported for Oracle when the CLI flag or config option is enabled, the same as MSSQL. The path uses a **local `OracleTransaction`** on a single connection (`ExecuteQueryOnConnection`); it is **not XA** and does not rely on `TransactionScope` promotion.

Oracle rejects the `AS` keyword on table aliases (`INNER JOIN t AS alias`); generated join/FROM/EXISTS SQL omits `AS`.

Create-policy failure on a non-linking insert returns **403** (`DatabasePolicyFailure`). Trigger-assigned primary keys that cannot be returned to the mutation engine result in **500** and **rollback** of the nested graph.

This path is **GraphQL-only**. REST batch/array create does not share it (same as MSSQL).

### Binary values

Oracle `RAW` values are serialized as base64. `BLOB` columns are typed as byte[] and null-guarded during base64 encoding; values larger than roughly 2000 bytes are not covered because `UTL_ENCODE.BASE64_ENCODE` accepts `RAW` and the implicit `BLOB`-to-`RAW` conversion is size-limited.

## Identifier casing

Oracle stores unquoted identifiers in uppercase. DAB exposes Oracle column names using the normalized lower-case form used by its REST and GraphQL schemas, while generated SQL emits uppercase physical identifiers. Quoted, case-sensitive Oracle objects created with a different casing require explicit configuration mappings and are not covered by the default convention.
