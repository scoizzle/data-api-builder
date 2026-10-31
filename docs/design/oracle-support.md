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
- Managed identity (Microsoft Entra) token authentication through `OracleConnection.AccessToken`, controlled by the `managed-identity` data-source option
- Oracle metadata lookup through `ALL_TAB_COLUMNS`, `ALL_CONSTRAINTS`, and related views
- Session-context claim forwarding through Oracle application contexts (`set-session-context`)

## Hosted and managed-identity security

- When the runtime config is late-configured (hosted/DAB-as-a-service scenario) the Oracle
  connection must already be TCPS with server certificate DN matching turned on.
  `SSL_SERVER_DN_MATCH` accepts `TRUE`, `yes`, or `on`. A `tcps://` URL is accepted when its
  query sets `ssl_server_dn_match` to one of those values. DAB checks this at startup and does
  not rewrite the descriptor, inject `(SECURITY=(ENCRYPTION_CLIENT=...))`, or assign the
  process-wide `OracleConfiguration.SqlNetEncryptionClient` property. ODP.NET rejects a later
  change to that static after any connection has opened (`ORA-50099`), which breaks hot reload.
  There is no switch that turns the check off. TNS aliases, cleartext `host:port/service` and
  `tcp://` URLs, descriptors without `PROTOCOL=TCPS`, and descriptors that mix TCP with TCPS
  fail startup. Local and dev configs (not late-configured) keep the operator's connection
  string, including plain TCP. The v2 configuration-controller path sets the late-configured
  flag before the executor and metadata provider are constructed, so the check covers that
  path too.
- Managed identity authentication sets `OracleConnection.AccessToken`; the token is never written
  to the connection string. ODP.NET requires token connections to use `User Id=/` with no password
  or proxy credentials, so DAB rewrites `User Id` to `/` and removes `Password`, `Proxy User Id`
  and `Proxy Password`. Every other attribute, including the Data Source descriptor and its
  TLS/wallet options (for example `PROTOCOL=TCPS`,
  `SECURITY=(MY_WALLET_DIRECTORY=...)(SSL_SERVER_DN_MATCH=TRUE)`), is preserved. Those options
  are only valid inside the descriptor; standalone `Wallet Location`/`SSL_SERVER_DN_MATCH`
  keywords are rejected by ODP.NET.
- The `managed-identity` data-source option is tri-state and configuration-file only (there is no
  CLI flag; set it under `data-source.options`). It accepts `true`/`false` or `"1"`/`"0"`; any other
  value is rejected when the config is loaded.
  - Precedence: explicit `managed-identity: true`/`false` > wallet or external-authentication
    markers (`WALLET_LOCATION=`, `MY_WALLET_DIRECTORY=` in the descriptor, or `User Id=/`) disable
    the token > an empty password enables it. A configuration-controller token does not override a
    configured password. Wallet detection matches only those attribute names, so a host such as
    `wallet-db.corp` is not treated as a wallet.
  - A `TCPS` descriptor that carries a wallet and should still use a token needs
    `managed-identity: true`.
  - With `managed-identity: true`, a failure to obtain a token (for example
    `DefaultAzureCredential` finds no credential) fails the request with a 503 instead of
    silently connecting without a token; token acquisition is retried on the next request. When
    the option is omitted, token use is best effort and a missing credential is logged as a
    warning and disables token use for that data source.
- Stored-procedure metadata discovery binds schema/package/subprogram names as parameters
  (`@param0`/`@param1`/`@param2`) instead of interpolating them into the `ALL_ARGUMENTS` query.

## Session context

Oracle session-context forwarding mirrors MSSQL's `sp_set_session_context` behavior and is opt-in
through the data-source option `set-session-context` (default `false`; also settable at init with
`dab init --set-session-context true` or with `dab configure --data-source.options.set-session-context`).
Before each statement, DAB executes a parameterized PL/SQL block as a **separate command on the same
connection** (an Oracle anonymous block cannot be prepended to another statement in one
`OracleCommand`):

```sql
BEGIN DAB_SESSION_CONTEXT_PKG.CLEAR_CLAIMS;
      DAB_SESSION_CONTEXT_PKG.SET_CLAIM(:dab_claim_key0, :dab_claim_value0); ... END;
```

- **Database prerequisite.** A DBA runs `docker/oracle-session-context.sql` once: it creates the
  `DAB_SESSION_CONTEXT` namespace (`CREATE CONTEXT DAB_SESSION_CONTEXT USING DAB_SESSION_CONTEXT_PKG;`)
  and the `DAB_SESSION_CONTEXT_PKG` package. Oracle only permits a namespace's context to be set from
  its associated package. The DAB login needs `EXECUTE` on the package.
- **Claim values.** Claims come from the shared, provider-agnostic pipeline
  (`AuthorizationResolver.GetProcessedUserClaims`): a single-valued claim is forwarded as a scalar, and
  multiple claims of the same name as a JSON array string (e.g. `["group1","group2"]`). `roles` resolves
  to the active `X-MS-API-ROLE` value; the token's roles are exposed as `original_roles`.
- **No stale claims.** Every statement that goes through the query executor clears the namespace
  (`CLEAR_CLAIMS`) before setting the current request's claims, including statements with no
  `HttpContext` (MCP stdio, background work, and metadata commands). A null context clears and sets
  nothing, so a pooled connection cannot keep a previous caller's claims. (This intentionally differs
  from MSSQL, which only overwrites the keys it sets.) The clear is one extra PL/SQL round trip per
  statement while the option is on, and zero round trips while it is off.
- **Mutations.** The context command runs after the statement command is enlisted in the local
  transaction. When that transaction is an `OracleTransaction`, the context command is enlisted in it
  too. The async request path passes the request-abort token to the context command. The synchronous
  query path has no token.
- **Health check.** The data-source health query is `SELECT 1 FROM DUAL` on a raw pooled connection
  opened by `HttpUtilities.ExecuteDbQueryAsync`, not by the query executor. That statement does not read
  `SYS_CONTEXT`, so it cannot observe or return a previous caller's claims. It also does not clear the
  namespace; the next executor checkout does. Enabling `set-session-context` does not require the DBA
  package to exist at health-check time.
- **Injection safety.** Claim names and values are always bound parameters; claim text is never
  concatenated into SQL.
- **Caching.** Enabling the option disables response caching for the data source, the same as MSSQL.
- **Consumption.** Database-side policies read values with `SYS_CONTEXT('DAB_SESSION_CONTEXT', '<claim>')`
  (for example in a `DBMS_RLS.ADD_POLICY` predicate function or a view mapped as an entity), and
  multi-valued claims are expanded with `JSON_TABLE`:

```sql
WHERE region IN (
  SELECT jt.value
  FROM JSON_TABLE(SYS_CONTEXT('DAB_SESSION_CONTEXT', 'regions'), '$[*]'
       COLUMNS (value VARCHAR2(4000) PATH '$')) jt)
```

- **Limits.** `DBMS_SESSION.SET_CONTEXT` values are `VARCHAR2(4000)` (MSSQL session context allows up
  to 1 MB), and context attribute names are length-limited (30 bytes before Oracle 12.2, 128 after), so
  a very large claims array or a URI-style claim name cannot be forwarded.

DAB-config database policies (`@claims.x` in `permissions`) are a separate mechanism: they bind claim
values in-process and use only the first value of a duplicated claim; they do not read session context.

## Current Limitations

### Stored procedures

Oracle stored procedures and functions can be invoked as REST operations, and as GraphQL operations for result sets DAB can describe. `SqlExecuteStructure` is implemented for Oracle: subprograms are invoked from a PL/SQL anonymous block (never the SQL*Plus-only `EXEC` keyword), engine-generated `@paramN` bind references are translated to Oracle `:paramN` syntax, and a trailing `:dab_result` REF CURSOR OUT bind exposes the result set to ODP.NET. Package-qualified subprograms (`schema.package.subprogram`) are supported, as are standalone functions invoked via `SELECT ... FROM DUAL`. For stored procedures, unmapped result-set columns surface the catalog spelling (uppercase for unquoted identifiers), with no lowercase fallback.

Known limitations:

- **GraphQL stored-procedure result typing.** Oracle metadata discovery describes a subprogram's REF CURSOR by its OUT parameter name (e.g. `CURSOR`), not by the columns the cursor returns. REST invocations are unaffected (the response is keyed by the actual returned columns), but the GraphQL schema for a cursor-returning subprogram exposes the cursor parameter name rather than the rowset's columns, so GraphQL queries over stored-procedure result sets are not reliably typed. Prefer REST for stored-procedure invocations on Oracle until cursor-column discovery is implemented.
- **Scalar OUT/IN OUT parameters.** Subprograms whose result is a scalar OUT/IN OUT parameter (no REF CURSOR) are invoked with only their IN arguments; the OUT argument is not bound, so such subprograms fail at request time. Only subprograms with IN parameters plus an optional REF CURSOR OUT parameter are supported.
- **Overloaded subprograms.** When `ALL_ARGUMENTS` lists more than one overload, the configured parameter names must equal one overload's input formals (IN and IN/OUT). Omitting a defaulted formal is not a match, and DAB does not fall back to the lowest `OVERLOAD`. Append `#n` to the source object (`schema.package.subprogram#2`) to pin that overload's metadata. The suffix is metadata only: the PL/SQL call stays a named invocation, and Oracle resolves which body runs. A single overload is used as-is. Startup fails when zero or several overloads match.

Oracle stored-procedure metadata discovery (including OUT parameters and REF CURSOR metadata) is used to validate signatures for schema generation. Invoking a procedure whose metadata cannot be resolved, or that returns an unsupported result shape, surfaces an error at request time.

### NUMBER precision and trigger-assigned keys

`NUMBER(p,0)` with precision 1–9 is `Int`, precision 10–18 is `Long`, and a scale above 0 or an unconstrained `NUMBER` stays `Decimal`. Column facts come from `ALL_TAB_COLUMNS` (`DATA_PRECISION`, `DATA_SCALE`), not from an unbounded `GetSchema("Columns")` call.

A `BEFORE EACH ROW` insert trigger that assigns a column only inside `IF :new.column IS NULL` (or `IF INSERTING AND :new.column IS NULL`) is generated by default: GraphQL create input keeps the field and makes it optional (`@generatedByDefault`), and REST accepts an explicit value. An unguarded sequence assignment stays always generated (`@autoGenerated`, omitted from create). `AFTER` and compound triggers do not mark the column.

### Autoentities and aggregation

Oracle autoentity discovery is supported: tables with a primary key are discovered through `ALL_TABLES` (Oracle-maintained system schemas are excluded) and materialized as entities according to the include/exclude/name patterns, both at engine startup and through `dab auto-config-simulate`. Generated entity names are lowercased so REST paths and GraphQL type names stay stable regardless of catalog folding; unmapped columns still surface as the catalog spelling (see Identifier casing). The SQL aggregation GraphQL surface (groupBy) is enabled for Oracle and emits `GROUP BY`, `HAVING`, and aggregation columns (COUNT/SUM/AVG/MIN/MAX) through the shared query-builder contracts.

### GraphQL multiple-create

Nested GraphQL create (parent/child/linking inserts in FK order, then a follow-up SELECT of created keys) is supported for Oracle when the CLI flag or config option is enabled, the same as MSSQL. The path uses a **local `OracleTransaction`** on a single connection (`ExecuteQueryOnConnection`); it is **not XA** and does not rely on `TransactionScope` promotion.

Oracle rejects the `AS` keyword on table aliases (`INNER JOIN t AS alias`); generated join/FROM/EXISTS SQL omits `AS`.

Create-policy failure on a non-linking insert returns **403** (`DatabasePolicyFailure`); for inserts that supply no column values, see *Inserts with no column values* under Authorization policies. Trigger-assigned primary keys that cannot be returned to the mutation engine result in **500** and **rollback** of the nested graph.

This path is **GraphQL-only**. REST batch/array create does not share it (same as MSSQL).

### Binary values

Oracle `RAW` values are serialized as base64. `BLOB` columns are typed as byte[] and null-guarded during base64 encoding; values larger than roughly 2000 bytes are not covered because `UTL_ENCODE.BASE64_ENCODE` accepts `RAW` and the implicit `BLOB`-to-`RAW` conversion is size-limited.

### Date and time filter values

GraphQL filter literals for `DATE`/`TIMESTAMP` columns arrive as strings. Left as a `VARCHAR2` bind, Oracle converts the string with the session NLS date format (default `DD-MON-RR`), so ISO 8601 filter values raise `ORA-01861`/`ORA-01843` and only the Oracle-specific `DD-MON-RR` spelling works. `BaseSqlQueryStructure.MakeDbConnectionParam` parses such values into the column's date/time type before binding; the invariant-culture parse accepts both ISO 8601 (`2026-01-01`, `2026-01-01T10:23:00Z`) and the `DD-MON-RR` spelling, so filters no longer depend on NLS settings. `TIMESTAMP WITH TIME ZONE` columns keep the offset (bound as `TimeStampTZ` by `OracleQueryExecutor`); plain `DATE`/`TIMESTAMP` values are bound as the UTC wall clock. OData date literals (REST/MCP) are converted by the same executor conversion.

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
- Permission `include`/`exclude` entries may be authored with the exposed name (field alias, field name, or mapping value) or the column name in any case. At metadata load they are rewritten to the backing (catalog) spelling, because authorization subtracts excluded from included in backing space; `*` is kept as is. `@item.` tokens in database policies stay in exposed spelling (the OData model is built from exposed names). A permission entry that matches no column of a table entity fails startup (`ErrorInInitialization`) instead of silently leaving the column readable; stored procedure and view entities are not checked.

Casing translation lives in `OracleMetadataProvider` (config name → physical backing name) and `OracleQueryBuilder` (physical name → SQL identifier). Shared SQL/GraphQL code talks to the existing backing/exposed maps (`TryGetBackingColumn` / `TryGetExposedColumnName`) and does not special-case Oracle.

## Authorization policies

Database policies (`permissions[].actions[].policy.database`) use the shared engine-agnostic pipeline: `AuthorizationPolicyHelpers` resolves the policy for the role/operation (compound upsert operations expand to Update + Create), claim references (`@claims.x`) are replaced with typed bound parameters, `ODataASTVisitor` maps `@item.field` exposed names to physical backing columns and quotes them, and each builder injects the resulting predicate via `GetDbPolicyForOperation`.

Oracle specifics:

- **Predicate placement.** Read/update/delete policies are ANDed into the `WHERE` clause exactly like the other engines. Create (and the insert branch of upsert) cannot use `INSERT … SELECT … WHERE`, so Oracle gates the insert with `SELECT COUNT(*) INTO v FROM (SELECT value AS "COL", … FROM DUAL) WHERE <policy>` and opens an empty REF CURSOR when the policy is unsatisfied, which surfaces as HTTP 403.
- **Inserts with no column values.** When the request body supplies no insertable columns and the create policy references no column (for example `true`), the policy cannot be evaluated against a row, so the request is rejected with HTTP **403** `DatabasePolicyFailure` (the same status as every other database policy denial) instead of falling back to a `DEFAULT VALUES` insert. This is enforced for all engines that accept a create policy; the other engines previously bypassed the create policy in that case. A policy that references a column is rejected earlier with HTTP 403 `AuthorizationCheckFailed` ("One or more fields referenced by the database policy are not present in the request body."), so the two substatuses are distinct. The Oracle builder additionally refuses (500) to emit an insert for this state if the structure check is ever bypassed. See *Upgrade notes*.
- **Empty string is NULL.** Oracle cannot store `''` distinctly from `NULL`, so a policy comparing a column to `''` is rewritten to the equivalent `IS NULL` / `IS NOT NULL` predicate (`@item.col eq ''` → `"COL" IS NULL`, `@item.col ne ''` → `"COL" IS NOT NULL`). Other engines compare against a real empty string.
- **String comparison is case-sensitive** under Oracle's default binary collation.
- **Numeric comparisons** bind policy literals for `NUMBER` columns as `decimal`; boolean-style predicates against `NUMBER(1)` columns are not supported.
- **Invalid policy fields** (a field the entity does not expose) are rejected while processing the policy with a clear authorization error, rather than producing a malformed predicate.
- **Multiple-create** applies the read policy of each created/related entity, combined with the OR'd primary-key predicate, in the same way as MSSQL.

## Nested reads: keyed CTE aggregation

Nested GraphQL relationship selections are generated as **keyed aggregate CTEs** by `OracleQueryBuilder` instead of correlated `LEFT OUTER JOIN LATERAL` subqueries:

- Each relationship whose correlation predicates are plain column equalities becomes one `WITH "<alias>_cte" AS (…)` block. Inside it, child rows are ranked with `ROW_NUMBER() OVER (PARTITION BY <foreign keys> ORDER BY <child order>)`, the per-parent top-N is kept (`N` is the child's limit, including the extra row used for pagination), and the JSON fragment is aggregated once per parent key (`JSON_ARRAYAGG` for lists, the single `JSON_OBJECT` for to-one).
- The parent joins the CTE on the foreign keys (`LEFT OUTER JOIN "<alias>_cte" ON …`). CTEs are emitted leaf-first, so a nested CTE can reference the CTEs of its own relationships. List fragments are wrapped in `COALESCE(…, TO_CLOB(JSON_ARRAY()))` so a parent with no children still deserializes as `[]`.
- Relationships that cannot be de-correlated (non-equality correlation predicates, nested `groupBy`) fall back to the previous correlated LATERAL form, so the JSON contract is unchanged.
- The request page is materialized first (`dab_page_cte`, with the root filters/policy/keyset predicate/order/limit applied), direct child aggregates are restricted to the page keys through an `INNER JOIN (SELECT DISTINCT …)`, and the final query reads the page CTE when the root has no associative joins so the root predicates run once.
- The page CTE is emitted with an explicit `MATERIALIZE` hint. It is read by the final query and by every direct child aggregate, and its definition can itself reference a set-based filter CTE; some Oracle versions inline a query name used more than once when its definition references another query name and then fail with `ORA-32036: unsupported case for inlining of query name in WITH clause`. The hint applies Oracle's documented workaround and also keeps the page computation (and its filter scan) from being evaluated once per reader.
- Nested-relationship filters (GraphQL filters on a related entity) are normally rendered as correlated `EXISTS` subqueries. For the page computation - and for the root query when no page CTE feeds it - ANDed nested filters are rewritten as set-based `SELECT DISTINCT <child correlation columns> …` key CTEs that are joined once, so the query never probes the child table per candidate parent. Filters nested under `OR`/`NOT` keep the correlated `EXISTS` to preserve boolean semantics.

Motivation: Oracle's transformation of the LATERAL form can pick a per-parent plan (`INDEX FULL SCAN` + `WINDOW … PUSHED RANK` that only stops after accumulating `N` matches per parent), which rescans the child table once per parent. On a synthetic skewed shape (2,000 parents, 1M children, 5 parents with 50k children) the generated CTE form measured ~0.4 s / ~6.6k buffer gets versus ~9.6 s / ~1.3M buffer gets for the LATERAL form, with byte-identical JSON (`JSON_EQUAL`). A second motivating case is a 45.6M-row child table where the correlated nested-filter `EXISTS` was planned as an index join over the whole date window (minutes per page); the set-based key CTE turned the same page into a single window scan plus key joins, with the estimated cost dropping from 38M to 5.8K.

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

  When the connection string sets neither `Statement Cache Size` / `StatementCacheSize` nor
  `Self Tuning` / `SelfTuning`, DAB appends `Statement Cache Size=20;Self Tuning=false` on both
  the executor's pooled connections and the metadata provider's connection. The keywords are
  appended; the string is not rebuilt with `OracleConnectionStringBuilder.ToString()`, so a
  DESCRIPTION descriptor stays intact. If either keyword is present, the string is unchanged.
  That includes an explicit `Statement Cache Size=0`, which disables the cache and leaves
  self-tuning at the driver default.

  ODP.NET's builder reports an unset cache size as 0 and leaves self-tuning on. The driver's
  own default is a self-tuning cache (commonly cited as 10) that can grow until the session
  hits `open_cursors`. 20 is a fixed cap just above that cited default. It was not chosen from
  a measured cursor plateau, and operators who need a different ceiling set the keywords
  themselves. Example of an operator override, which DAB preserves:

  ```json
  "connection-string": "Data Source=...;User Id=...;Password=...;Statement Cache Size=50;Self Tuning=false;"
  ```

  After catalog names are resolved, DAB reads `open_cursors` from `v$parameter` when that view
  is granted. A value below 1500 logs one warning that names the 20-cursor default and the
  `ALTER SYSTEM` example above. `ORA-00942` and any other probe failure are ignored and do not
  fail startup.

  The health check opens its connection from the raw configuration string. In hosted mode that
  string has already passed the TCPS check. The health check does not apply the statement-cache
  bound; it runs one `SELECT 1`, so the extra cursor is negligible. Sharing the internal policy
  type with the service project would have required a new public API for that single cursor.

### Command timeout

ODP.NET's command timeout defaults to 0 (wait forever), while the other ADO.NET providers DAB uses default to 30 seconds. The Oracle executor caps every request command (`OracleCommand.CommandTimeout`, applied in `PrepareDbCommand`), and Oracle metadata commands (column catalog, triggers, synonyms, and `ALL_ARGUMENTS`) use the same per-data-source cap through `CreateMetadataCommand`. A runaway statement fails with `ORA-01013` instead of pinning startup, hot reload, or the request. The default is 30 seconds and can be overridden per data source:

```json
"data-source": {
  "database-type": "oracle",
  "connection-string": "...",
  "options": { "command-timeout": 60 }
}
```

`command-timeout` is in seconds; `0` disables the cap. The cap is applied per command rather than through ODP.NET's process-wide `OracleConfiguration.CommandTimeout`, which ODP.NET rejects once any connection has been opened (`ORA-50099`) and would therefore break hot reload and long-lived processes.

## TIMESTAMP, CLOB, and RETURNING sizes

A .NET `DateTime` that arrives without a `DbType` hint is bound as `OracleDbType.TimeStamp`. `OracleQueryExecutor.PopulateDbTypeForParameter` sets the Oracle type and the UTC wall-clock value together. Assigning the value first lets ODP.NET 23.7 infer `TimeStamp`, and a later type change is too late to be the decision. `DateTimeKind.Local` is still converted with `ToUniversalTime`. `DateTimeOffset` stays `TimeStampTZ`. A `DATE` value stays at midnight (`00:00:00`) because Oracle `DATE` has no fractional seconds and the wall-clock conversion does not invent a time of day. `TypeHelper` is unchanged, so MSSQL and PostgreSQL keep their own date mapping.

Column facts choose string binds. `CLOB` binds as `OracleDbType.Clob`, `NCLOB` as `NClob`, and `NVARCHAR2` / `NCHAR` as `NVarchar2`. A national value whose UTF-8 length is above 4000 bytes binds as `NClob`. A string with no column fact stays the driver's `Varchar2` at or below 4000 UTF-8 bytes and binds as `Clob` above that, which covers a multi-byte `VARCHAR2(4000 CHAR)` value that would otherwise be sent as `VARCHAR2` (`ORA-01461` on a server that enforces the byte limit). Nulls still receive the column type. The facts live beside `DbConnectionParam`; that shared type has no Oracle-only fields.

`RETURNING INTO` character output size is `min(max(characterLength, 1) * 4, 32767)` when the column length is known, so a character length counted in characters still has room for the byte length ODP.NET reports. An unknown length uses 4000. The registrar clamps every character output size to `[1, 32767]`. `RAW` stays on its existing size (2000 when the hint has none).

### Test database

The Oracle integration suite issues several hundred distinct statements, so it exhausts the default `open_cursors=300` partway through a full `TestCategory=ORACLE` run. The test container must either set `open_cursors=1500` or the test connection string must bound the statement cache; with either in place the full category passes. This is cursor-cache sizing, not a DAB query-path leak: 500 repeated list queries, 400 repeated upserts, and repeated `DatabaseSchema-Oracle.sql` re-initialization all leave the cursor count flat or plateaued.

## Upgrade notes

- **Insert with no supplied columns plus a create policy is now denied (403).** A request that supplies no column values (for example `POST` with `{}`) to an entity whose role has a create database policy used to be executed as a `DEFAULT VALUES` insert on MSSQL, PostgreSQL and DWSQL, so the policy was never evaluated and the endpoint returned 201 with a row. It now fails closed with HTTP 403 and substatus `DatabasePolicyFailure` (policies that reference a column return 403 `AuthorizationCheckFailed`). This is a security fix and has no opt-out; do not retry the same empty body. Requests for roles without a create policy are unchanged. MySQL does not accept create policies, so it is unaffected. Release notes for the Oracle PR should carry this item, as the repository has no `CHANGELOG.md`.
