# Oracle Support

Data API builder supports Oracle Database 19c and later through
`Oracle.ManagedDataAccess.Core`.

## Supported

- REST and GraphQL queries, filters, ordering, selection, and cursor pagination
- REST and GraphQL create, update, delete, and upsert operations
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

Oracle stored procedures and functions can be invoked as REST or GraphQL operations. `SqlExecuteStructure` is implemented for Oracle: subprograms are invoked from a PL/SQL anonymous block (never the SQL*Plus-only `EXEC` keyword), engine-generated `@paramN` bind references are translated to Oracle `:paramN` syntax, and a trailing `:dab_result` REF CURSOR OUT bind exposes the result set to ODP.NET. Package-qualified subprograms (`schema.package.subprogram`) are supported, as are standalone functions invoked via `SELECT ... FROM DUAL`.

Oracle stored-procedure metadata discovery (including OUT parameters and REF CURSOR metadata) is used to validate signatures for schema generation. Invoking a procedure whose metadata cannot be resolved, or that returns an unsupported result shape, surfaces an error at request time.

### Session context

Oracle session-context forwarding is disabled by default. The current DAB command pipeline prepends session setup text to the SQL command. Oracle requires session-context calls to run inside a PL/SQL block, so prepending a standalone `BEGIN ... END;` block would produce invalid command text.

A future implementation must use a valid Oracle application context package and execute session setup together with the request command, or use a separate command on the same connection. Until then, Oracle database policies must not depend on DAB-forwarded session claims.

### Autoentities and aggregation

Oracle autoentity discovery remains disabled; autoentity generation is a database-provider-specific contract limited to providers that implement it. The SQL aggregation GraphQL surface (groupBy) is enabled for Oracle and emits `GROUP BY`, `HAVING`, and aggregation columns (COUNT/SUM/AVG/MIN/MAX) through the shared query-builder contracts.

## Identifier casing

Oracle stores unquoted identifiers in uppercase. DAB exposes Oracle column names using the normalized lower-case form used by its REST and GraphQL schemas, while generated SQL emits uppercase physical identifiers. Quoted, case-sensitive Oracle objects created with a different casing require explicit configuration mappings and are not covered by the default convention.
