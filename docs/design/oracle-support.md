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
- **Scalar OUT/IN OUT parameters.** Startup rejects a subprogram that has a scalar OUT parameter, an IN OUT parameter, or more than one REF CURSOR OUT parameter. The error is HTTP 503 `ErrorInInitialization` and names the parameter. A single REF CURSOR OUT, IN parameters, and a function RETURN (scalar or REF CURSOR) are supported. DAB does not bind scalar OUT or IN OUT values, so those signatures are refused before the first request instead of failing later with `PLS-00306`.
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

Oracle `RAW` values are serialized as base64 through one `UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE(...))` conversion. That conversion stops at 4000 bytes, which is enough for `RAW` (at most 2000 bytes) and not enough for `BLOB` or `LONG RAW`: base64 of about 3000 raw bytes crosses the cap and the page comes back empty.

`BLOB` values are base64-encoded in 1800-byte chunks (`DBMS_LOB.SUBSTR`) and aggregated as a CLOB, so the 4000-byte cap does not apply. 1800 is a multiple of 3, so only the last chunk can end with `=`. `LONG RAW` uses the same CLOB aggregation with `UTL_RAW.SUBSTR`. A `LONG RAW` value larger than 32767 bytes still cannot be passed to `UTL_RAW` and is not covered. NULL binary values stay NULL (`UTL_ENCODE.BASE64_ENCODE(NULL)` raises `ORA-29261`).

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

After resolve, `QuoteRelation` emits the base catalog names. A quoted base object keeps the spelling `ALL_SYNONYMS` returned.

## Identifier casing

Oracle folds unquoted identifiers to uppercase in the catalog. DAB quotes every identifier, so SQL must use the **catalog spelling** (physical backing name). REST/GraphQL/OData names are a separate **exposed** layer.

- Backing names on `SourceDefinition` (columns, primary key, FK column lists) are the catalog spelling: `ID`, `BOOK_ID`, or the exact spelling of a quoted identifier.
- Exposed names are the name **as provided to the engine**: the entity config mapping / field alias verbatim when one is configured, otherwise the catalog spelling of the column. Unmapped unquoted columns therefore surface as `ID` / `BOOK_ID`, and quoted columns keep their exact catalog spelling (e.g. `"ID Number"`). There is no lowercase fallback.
- Generated SQL quotes backing names as-is. Schema, table, package, and procedure names go through `QuoteRelation` / `QuoteCatalogObject`, which emit the stored catalog spelling. DAB-generated aliases go through `QuoteTableAlias` and stay uppercase.
- Quoted identifiers are supported. In config, wrap a part in double quotes (`"Hr"."Customers"`, or `"Sch.ema"."Tab.le"` when the name contains a dot). `""` inside quotes is one quote character. An unquoted part is folded to uppercase once, at parse, and that spelling is what catalog lookups and SQL use. `[brackets]` remain only a way to escape `.` or `]` in an unquoted name; the text inside still folds, and a token starts with either `"` or `[` (they do not nest). `User Id=hr` resolves to schema `HR`. `User Id="Hr"` keeps `Hr`. `SELECT USER` is already the catalog spelling and is used as returned.
- A quoted object that is missing fails startup with `Could not resolve Oracle object "Hr"."Customers"`, naming that spelling. Two columns that differ only by case (`"DATA"` and `"data"`) fail startup, because the column map is case-insensitive. Two relations that differ only by case are also rejected: `DatabaseObject` equality ignores case, so a quoted table and its uppercase twin would otherwise be treated as one object. GraphQL still cannot expose a name that fails its identifier rules (for example a space); those columns stay REST-only.
- Autoentity sources are built from the catalog spelling. A mixed-case or otherwise quoted name is emitted with quotes (`HR."Customers"`) so the next parse does not fold it. An ordinary uppercase name stays bare (`SYSTEM.BOOKS`). Linking sources keep the authored string and go through the same parser.
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
- Nested JSON values are marked `FORMAT JSON`. The root `JSON_ARRAYAGG` already needed that marker so an inline `JSON_OBJECT` was not quoted as text. Child `JSON_ARRAYAGG(json_doc …)` and each `VALUE` of a relationship column use the same marker, including `COALESCE(data, TO_CLOB(JSON_ARRAY()))` and `CASE … ELSE NULL`. A select list with a nested relationship column uses an explicit `JSON_OBJECT` (so that one column can be marked); a flat select list stays `JSON_OBJECT(*)`. `ResolveObject` / `ResolveList` reparse a JSON string into an object or array, so a passing GraphQL response does not prove the driver returned an object. The statements in `OracleNestedJsonFormatTests.LiveProbes` are the 19c/23c check; they were not executed in the change that added the marker.
- Depth-2 and deeper **ranked** relationships are restricted to the parent page. A ranked child that is itself restricted and has descendants is hoisted into `"<alias>_rows_cte"` (`/*+ MATERIALIZE */`, required because that CTE reads `dab_page_cte` or another rows CTE and would otherwise hit `ORA-32036`). Each descendant then joins `SELECT DISTINCT <parent keys> FROM "<alias>_rows_cte" WHERE "rn" <= N`. The same rule repeats, so a 4-level list is bounded at every ranked level. Depth-1 SQL does not hoist. A query with no page CTE (no order, no limit, or only flattened children) does not hoist either. Multiple-create follow-ups use this page CTE too. `Limit()` is already the final-query cap (default page size for a list), so the page `FETCH NEXT` and the final query share that cap.
- **Scope cut.** Associative children (a link-table join) and flattened to-one children do not export a key restriction to their own descendants. Those descendant aggregates still rank and aggregate the full related table. A to-one that is not flattened (for example because it has a read policy) does export its rows CTE, without an `"rn"` filter when the correlation is the child's primary key. Descendant-aware flattening, and a key source for associative children, are follow-up work. A 3-level benchmark against a large leaf table was not run here; there was no live Oracle available.

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

Startup catalog discovery (synonym resolution, `ALL_TAB_COLUMNS`, trigger bodies, and `ALL_ARGUMENTS`) shares one connection for that pass and passes the startup cancellation token into `Open` and each read. Each statement is still a `CreateMetadataCommand`, so the same command timeout applies. Cancelling startup is not swallowed by the `open_cursors` probe. A metadata call made outside that pass opens its own connection. The session-user lookup used when the connection string has no User ID stays a separate synchronous open.

## TIMESTAMP, CLOB, and RETURNING sizes

A .NET `DateTime` that arrives without a `DbType` hint is bound as `OracleDbType.TimeStamp`. `OracleQueryExecutor.PopulateDbTypeForParameter` sets the Oracle type and the UTC wall-clock value together. Assigning the value first lets ODP.NET 23.7 infer `TimeStamp`, and a later type change is too late to be the decision. `DateTimeKind.Local` is still converted with `ToUniversalTime`. `DateTimeOffset` stays `TimeStampTZ`. A `DATE` value stays at midnight (`00:00:00`) because Oracle `DATE` has no fractional seconds and the wall-clock conversion does not invent a time of day. `TypeHelper` is unchanged, so MSSQL and PostgreSQL keep their own date mapping.

`RETURNING INTO` uses the catalog type, not only the CLR type. `DATE` and `TIMESTAMP` are both `System.DateTime`. A `DATE` column binds as `OracleDbType.Date`. `TIMESTAMP(6)` binds as `TimeStamp`. `TIMESTAMP WITH TIME ZONE` binds as `TimeStampTZ`, and `TIMESTAMP WITH LOCAL TIME ZONE` as `TimeStampLTZ`. A `DateTime` column with no catalog type keeps the `TimeStamp` bind.

`CHAR` and `NCHAR` filter and mutation parameters bind as `OracleDbType.Char` / `NChar` with `Size` set to the column's character length (`CHAR_LENGTH`, falling back to `DATA_LENGTH` when the catalog row has no character length). Oracle blank-pads that comparison, so `CHAR(4)` value `'AB  '` matches the filter `'AB'`. `VARCHAR2` stays a variable bind. `NVARCHAR2` stays `NVarchar2` and is not blank-padded. Binding those fixed types as `VARCHAR2` / `NVARCHAR2` is a silent miss.

Column facts choose string binds. `CLOB` binds as `OracleDbType.Clob`, `NCLOB` as `NClob`, and `NVARCHAR2` as `NVarchar2`. A national variable-length value whose UTF-8 length is above 4000 bytes binds as `NClob`. A string with no column fact stays the driver's `Varchar2` at or below 4000 UTF-8 bytes and binds as `Clob` above that, which covers a multi-byte `VARCHAR2(4000 CHAR)` value that would otherwise be sent as `VARCHAR2` (`ORA-01461` on a server that enforces the byte limit). Nulls still receive the column type. The facts live beside `DbConnectionParam`; that shared type has no Oracle-only fields.

Catalog type names are normalized before the CLR map. `TIMESTAMP(6)`, `TIMESTAMP(6) WITH TIME ZONE`, and `TIMESTAMP(9) WITH LOCAL TIME ZONE` match the unqualified timestamp types. `INTERVAL DAY(n) TO SECOND(n)` and `INTERVAL YEAR(n) TO MONTH` map to `TimeSpan`. `XMLTYPE` maps to `string`. An unknown type still maps to `object`.

`RETURNING INTO` character output size is `min(max(characterLength, 1) * 4, 32767)` when the column length is known, so a character length counted in characters still has room for the byte length ODP.NET reports. An unknown length uses 4000. The registrar clamps every character output size to `[1, 32767]`. `RAW` stays on its existing size (2000 when the hint has none).

### Test database

The Oracle integration suite issues several hundred distinct statements, so it exhausts the default `open_cursors=300` partway through a full `TestCategory=ORACLE` run. The test container must either set `open_cursors=1500` or the test connection string must bound the statement cache; with either in place the full category passes. This is cursor-cache sizing, not a DAB query-path leak: 500 repeated list queries, 400 repeated upserts, and repeated `DatabaseSchema-Oracle.sql` re-initialization all leave the cursor count flat or plateaued.

## Accepted limitations

These items from the Oracle review are documented here and are not treated as open engine defects.

- **Keyset pagination and NULL or blank sort keys.** Keyset predicates are `col > :cursor` with no `NULLS FIRST` / `NULLS LAST`. A NULL sort key is never reached. On Oracle `''` is NULL, so a blank string is in that same gap. Use a non-null sort key.
- **NUMBER above 2^53 on GraphQL.** Unconstrained `NUMBER` and fractional `NUMBER` stay `decimal` in the engine and are exposed to GraphQL as Float (IEEE double). Integers above 2^53 round on GraphQL reads and filters. REST still carries the decimal. A non-IEEE GraphQL scalar would be a public schema change and is not in this preview.
- **Retry inside an ambient transaction.** The local ReadCommitted mutation path does not use Polly. A REST write that does not re-read still runs under `TransactionScope`. Polly retry of a transient error on that path can open a second ODP.NET session and promote to XA, which .NET does not support. Do not wrap those writes in an ambient transaction that expects a retry.
- **GraphQL `in` lists longer than 100.** The shared GraphQL filter parser rejects `in` with more than 100 values (HTTP 400). That limit is not Oracle-specific.
- **Health check error text.** The shared health response includes `ex.Message` for a failed probe. On Oracle that string can contain a TNS host, service, or wallet path. The detail stays in the process logs as well. This preview does not change the shared health payload.
- **Session context costs one extra round trip** per statement while `set-session-context` is on. The clear-then-set block is what keeps pooled sessions from leaking claims. It is not cached.

## NuGet restore

`Oracle.ManagedDataAccess.Core` 23.7.0 is declared in `src/Directory.Packages.props` and referenced by `src/Core/Azure.DataApiBuilder.Core.csproj`. The repository `Nuget.config` clears package sources and adds only the private `data_api_builder_build_packages` Azure Artifacts feed. A cold restore (`dotnet restore --no-cache`) was **not** run for this change. A restore that reused the machine's global packages folder is not evidence that a clean machine can restore.

To try a cold restore, use a feed that can serve `Oracle.ManagedDataAccess.Core` 23.7.0 (nuget.org, or an Azure Artifacts feed with a nuget.org upstream) and run:

```sh
dotnet restore src/Azure.DataApiBuilder.sln --no-cache
```

## Stress script

`scripts/oracle-stress-test.py` is the GraphQL and REST battery that was removed from the merge branch and restored with this preview. It starts a local engine, runs the checks, deletes the rows it inserted, and exits non-zero on failure. It expects the star-trek demo schema (7 series, 36 actors, 36 characters, 12 species) and a config that points at it. The script is not part of CI. Benchmark numbers in this document that cite a large child table were measured outside this repository and are not reproduced by the script.

## Mutation errors

A local Oracle mutation rolls back when the work throws. If that rollback also throws, the rollback error is logged and the original mutation error is what the caller sees. A commit failure propagates and is not followed by a rollback.

## Upgrade notes

- **Insert with no supplied columns plus a create policy is now denied (403).** A request that supplies no column values (for example `POST` with `{}`) to an entity whose role has a create database policy used to be executed as a `DEFAULT VALUES` insert on MSSQL, PostgreSQL and DWSQL, so the policy was never evaluated and the endpoint returned 201 with a row. It now fails closed with HTTP 403 and substatus `DatabasePolicyFailure` (policies that reference a column return 403 `AuthorizationCheckFailed`). This is a security fix and has no opt-out; do not retry the same empty body. Requests for roles without a create policy are unchanged. MySQL does not accept create policies, so it is unaffected. Release notes for the Oracle PR should carry this item, as the repository has no `CHANGELOG.md`.
